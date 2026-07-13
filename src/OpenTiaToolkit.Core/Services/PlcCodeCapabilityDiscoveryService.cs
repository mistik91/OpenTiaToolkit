using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed class PlcCodeCapabilityDiscoveryService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly IPlcService _plc;
    private readonly IVciService _vci;
    private readonly EngineeringSessionContext _context;
    private readonly Func<Task<string>> _versionProvider;
    private readonly Func<string, Task<PlcCodeEnvironmentEvidence>>? _environmentProvider;
    private readonly Func<PlcCodeTargetEvidence, PlcCodeSafetyEvidence>? _safetyProvider;

    internal PlcCodeCapabilityDiscoveryService(
        IPlcService plc,
        IVciService vci,
        EngineeringSessionContext context,
        Func<Task<string>>? versionProvider = null,
        Func<string, Task<PlcCodeEnvironmentEvidence>>? environmentProvider = null,
        Func<PlcCodeTargetEvidence, PlcCodeSafetyEvidence>? safetyProvider = null)
    {
        _plc = plc;
        _vci = vci;
        _context = context;
        _versionProvider = versionProvider ?? (() => Task.FromResult(string.Empty));
        _environmentProvider = environmentProvider;
        _safetyProvider = safetyProvider;
    }

    public async Task<PlcCodeCapabilityDiscovery> DiscoverAsync(PlcCodeCapabilityRequest request) =>
        await DiscoverWithCleanupAsync(Normalize(request)).ConfigureAwait(false);

    private async Task<PlcCodeCapabilityDiscovery> DiscoverWithCleanupAsync(PlcCodeCapabilityRequest request)
    {
        CleanupExpired();
        return await DiscoverCoreAsync(request, persist: true).ConfigureAwait(false);
    }

    public PlcCodeCapabilityDiscovery Get(string capabilityId)
    {
        string path = CapabilityPath(capabilityId);
        if (!File.Exists(path)) throw new InvalidOperationException($"Capability discovery '{capabilityId}' was not found or has expired.");
        PlcCodeCapabilityDiscovery discovery = JsonSerializer.Deserialize<PlcCodeCapabilityDiscovery>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Capability discovery manifest is invalid.");
        if (discovery.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException("Capability discovery expired. Discover capabilities again.");
        }
        return discovery;
    }

    public async Task<PlcCodeCapabilityDiscovery> ValidateCurrentAsync(string capabilityId, string plcName)
    {
        PlcCodeCapabilityDiscovery stored = Get(capabilityId);
        if (stored.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Capability discovery expired. Discover capabilities again.");
        if (!stored.Request.PlcName.Equals(plcName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Capability discovery is bound to a different PLC.");
        PlcCodeCapabilityDiscovery current = await DiscoverCoreAsync(stored.Request, persist: false).ConfigureAwait(false);
        if (!FixedEquals(stored.Fingerprint, current.Fingerprint))
            throw new InvalidOperationException("Capability discovery is stale: the TIA target, safety state, VCI mapping, language, protection, or import/export evidence changed. Discover capabilities again.");
        return stored;
    }

    public void RequireOperationRoute(PlcCodeCapabilityDiscovery capability, string operationKind)
    {
        string required = operationKind == "GenerateSclSource" ? "DirectScl"
            : operationKind == "PatchSimaticMlBlock" || operationKind == "PatchSimaticMlType" ? "SimaticML"
            : operationKind == "DeletePlcType" ? "DirectScl"
            : "DirectDocuments";
        if (!capability.SelectedRoute.Equals(required, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Capability discovery selected '{capability.SelectedRoute}', but this transaction operation requires '{required}'. Run discovery for the intended format and target.");
    }

    private async Task<PlcCodeCapabilityDiscovery> DiscoverCoreAsync(PlcCodeCapabilityRequest request, bool persist)
    {
        if (!_plc.IsSoftwareOpen || !string.Equals(_plc.CurrentPlcName, request.PlcName, StringComparison.OrdinalIgnoreCase))
            await _plc.OpenPlcAsync(request.PlcName).ConfigureAwait(false);
        if (!_plc.IsSoftwareOpen || !string.Equals(_plc.CurrentPlcName, request.PlcName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PLC software '{request.PlcName}' could not be opened exactly.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var result = new PlcCodeCapabilityDiscovery
        {
            CapabilityId = persist ? Guid.NewGuid().ToString("N") : string.Empty,
            ObservedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime),
            Request = request,
            Environment = _environmentProvider == null ? await ReadEnvironmentAsync(request.PlcName).ConfigureAwait(false) : await _environmentProvider(request.PlcName).ConfigureAwait(false),
            Target = await ReadTargetAsync(request).ConfigureAwait(false)
        };
        result.Safety = _safetyProvider == null ? ReadSafety(result.Target) : _safetyProvider(result.Target);
        result.Vci = await ReadVciAsync(request, result.Target).ConfigureAwait(false);
        result.ImportExport = await ProbeRoutesAsync(request, result.Target).ConfigureAwait(false);
        result.Routes = EvaluateRoutes(request, result.Target, result.Safety, result.Vci, result.ImportExport);
        PlcCodeRouteDecision? selected = result.Routes.Where(route => route.Eligible).OrderBy(route => route.Rank).FirstOrDefault();
        result.SelectedRoute = selected?.Route ?? "Blocked";
        var warnings = new List<string>();
        if (!result.Target.ExactIdentityResolved) warnings.Add(result.Target.IdentityIssue);
        if (!string.IsNullOrWhiteSpace(result.Vci.ProbeError)) warnings.Add("VCI discovery was incomplete: " + result.Vci.ProbeError);
        if (result.Safety.MutationBlocked) warnings.Add(result.Safety.Reason);
        else if (result.Safety.ServiceAvailable && result.Safety.PasswordSet && !result.Safety.LoggedOn) warnings.Add("The PLC has a password-protected safety program and is not logged on; standard blocks remain eligible, but fail-safe targets are blocked.");
        result.Warnings = warnings;
        result.Fingerprint = ComputeFingerprint(result);
        if (persist)
        {
            Directory.CreateDirectory(CapabilityRoot);
            File.WriteAllText(CapabilityPath(result.CapabilityId), JsonSerializer.Serialize(result, JsonOptions));
        }
        return result;
    }

    private async Task<PlcCodeTargetEvidence> ReadTargetAsync(PlcCodeCapabilityRequest request)
    {
        var target = new PlcCodeTargetEvidence
        {
            ObjectKind = request.ObjectKind,
            ObjectName = request.ObjectName,
            GroupPath = request.GroupPath,
            ObjectType = request.BlockType ?? string.Empty,
            ProgrammingLanguage = request.RequestedLanguage ?? string.Empty
        };
        SoftwareTreeNode? tree = await _plc.GetSoftwareTreeAsync().ConfigureAwait(false);
        if (request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase))
        {
            SoftwareTreeNode[] typeMatches = Flatten(tree).Where(node => node.Name.Equals(request.ObjectName, StringComparison.OrdinalIgnoreCase) && node.Path.IndexOf("/Types/", StringComparison.OrdinalIgnoreCase) >= 0 && node.Type.IndexOf("Group", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            target.IdentityAmbiguous = typeMatches.Length > 1;
            target.Exists = typeMatches.Length == 1;
            target.ObjectType = "TYPE";
            if (target.Exists)
            {
                target.GroupPath = GroupFromTreePath(typeMatches[0].Path, "Types", request.ObjectName);
                SetGroupIdentity(target, request.GroupPath);
            }
            else if (target.IdentityAmbiguous) { target.ExactIdentityResolved = false; target.IdentityIssue = "More than one PLC type has this name; supply an exact unique target identity before generation."; }
            else if (request.Operation.Equals("Modify", StringComparison.OrdinalIgnoreCase)) { target.ExactIdentityResolved = false; target.IdentityIssue = "The requested PLC type does not exist, so a modify workflow cannot be planned."; }
            return target;
        }
        IReadOnlyList<BlockInfo> blocks = await _plc.GetBlocksAsync("^" + System.Text.RegularExpressions.Regex.Escape(request.ObjectName) + "$").ConfigureAwait(false);
        BlockInfo[] matches = blocks.Where(block => block.Name.Equals(request.ObjectName, StringComparison.OrdinalIgnoreCase)).ToArray();
        target.IdentityAmbiguous = matches.Length > 1;
        BlockInfo? block = matches.Length == 1 ? matches[0] : null;
        if (block == null)
        {
            if (target.IdentityAmbiguous) { target.ExactIdentityResolved = false; target.IdentityIssue = "More than one block has this name; exact group identity is ambiguous."; }
            else if (request.Operation.Equals("Modify", StringComparison.OrdinalIgnoreCase)) { target.ExactIdentityResolved = false; target.IdentityIssue = "The requested block does not exist, so a modify workflow cannot be planned."; }
            return target;
        }
        BlockInfoDetail? detail = await _plc.GetBlockInfoAsync(request.ObjectName).ConfigureAwait(false);
        target.Exists = true;
        target.ObjectType = detail?.Type ?? block.Type;
        target.ProgrammingLanguage = detail?.ProgrammingLanguage ?? detail?.Language ?? block.Language ?? string.Empty;
        target.Number = detail?.Number ?? block.Number ?? string.Empty;
        target.IsReadOnly = detail?.IsReadOnly ?? block.IsReadOnly;
        SoftwareTreeNode[] treeMatches = Flatten(tree).Where(node => node.Name.Equals(request.ObjectName, StringComparison.OrdinalIgnoreCase) && node.Path.IndexOf("/Blocks/", StringComparison.OrdinalIgnoreCase) >= 0 && node.Type.IndexOf("Group", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
        if (treeMatches.Length == 1)
        {
            target.GroupPath = GroupFromTreePath(treeMatches[0].Path, "Blocks", request.ObjectName);
            SetGroupIdentity(target, request.GroupPath);
        }
        else
        {
            target.ExactIdentityResolved = false;
            target.IdentityIssue = treeMatches.Length > 1 ? "More than one block has this name; exact group identity is ambiguous." : "The block was found by name but its exact software-tree group identity could not be resolved.";
        }
        return target;
    }

    private async Task<PlcCodeVciEvidence> ReadVciAsync(PlcCodeCapabilityRequest request, PlcCodeTargetEvidence target)
    {
        var evidence = new PlcCodeVciEvidence();
        if (!target.Exists || !target.ExactIdentityResolved || target.IdentityAmbiguous || request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase)) return evidence;
        try
        {
            IReadOnlyList<VciWorkspaceInfo> workspaces = await _vci.ListWorkspacesAsync().ConfigureAwait(false);
            evidence.Available = workspaces.Count > 0;
            foreach (VciWorkspaceInfo workspace in workspaces.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                IReadOnlyList<VciMappingInfo> mappings = await _vci.ListMappingsAsync(workspace.Name).ConfigureAwait(false);
                VciMappingInfo? mapping = mappings.FirstOrDefault(item => item.ObjectName.Equals(request.ObjectName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(request.BlockType) || item.ObjectType.IndexOf(request.BlockType!, StringComparison.OrdinalIgnoreCase) >= 0));
                if (mapping == null) continue;
                evidence.WorkspaceName = workspace.Name;
                evidence.ExactMappingFound = true;
                evidence.MappingFileBasePath = Path.Combine(mapping.DirectoryPath ?? string.Empty, mapping.FileNameWithoutExtension ?? request.ObjectName);
                evidence.MappingFormat = mapping.FileFormat ?? string.Empty;
                evidence.MappingStatus = mapping.Status ?? string.Empty;
                break;
            }
            evidence.SupportedFormats = await _vci.GetSupportedBlockFormatsAsync(request.PlcName, request.ObjectName, request.BlockType, request.GroupPath).ConfigureAwait(false);
        }
        catch (Exception ex) { evidence.ProbeError = Message(ex); }
        return evidence;
    }

    private async Task<PlcCodeImportExportEvidence> ProbeRoutesAsync(PlcCodeCapabilityRequest request, PlcCodeTargetEvidence target)
    {
        var evidence = new PlcCodeImportExportEvidence();
        if (!target.Exists || !target.ExactIdentityResolved || target.IdentityAmbiguous) return evidence;
        string root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "capability-probes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase))
            {
                string typeXml = Path.Combine(root, request.ObjectName + ".xml");
                try { evidence.SimaticMlRouteObserved = await _plc.ExportTypeAsync(request.ObjectName, typeXml).ConfigureAwait(false) && File.Exists(typeXml); evidence.SimaticMlProbe = evidence.SimaticMlRouteObserved ? "Exact type XML export succeeded." : "Type XML export was unavailable."; }
                catch (Exception ex) { evidence.SimaticMlProbe = Message(ex); }
                return evidence;
            }
            string docs = Path.Combine(root, "documents");
            Directory.CreateDirectory(docs);
            try { evidence.DocumentRouteObserved = await _plc.ExportBlockAsDocumentsAsync(request.ObjectName, docs).ConfigureAwait(false) && Directory.EnumerateFiles(docs, "*.s7dcl").Any(); evidence.DocumentProbe = evidence.DocumentRouteObserved ? "SIMATIC SD document export succeeded." : "SIMATIC SD document export produced no S7DCL."; }
            catch (Exception ex) { evidence.DocumentProbe = Message(ex); }
            string source = Path.Combine(root, request.ObjectName + ".scl");
            try { PlcSourceOperationResult sourceResult = await _plc.ExportBlockAsSourceAsync(request.ObjectName, source).ConfigureAwait(false); evidence.SclSourceRouteObserved = sourceResult.Success && File.Exists(source); evidence.SclProbe = sourceResult.Message; }
            catch (Exception ex) { evidence.SclProbe = Message(ex); }
            string xml = Path.Combine(root, request.ObjectName + ".xml");
            try { evidence.SimaticMlRouteObserved = await _plc.ExportBlockAsync(request.ObjectName, xml).ConfigureAwait(false) && File.Exists(xml); evidence.SimaticMlProbe = evidence.SimaticMlRouteObserved ? "SimaticML block export succeeded." : "SimaticML export was unavailable."; }
            catch (Exception ex) { evidence.SimaticMlProbe = Message(ex); }
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        return evidence;
    }

    public static IReadOnlyList<PlcCodeRouteDecision> EvaluateRoutes(PlcCodeCapabilityRequest request, PlcCodeTargetEvidence target, PlcCodeSafetyEvidence safety, PlcCodeVciEvidence vci, PlcCodeImportExportEvidence io)
    {
        bool blocked = safety.MutationBlocked || target.IsReadOnly || !target.ExactIdentityResolved || target.IdentityAmbiguous;
        string language = string.IsNullOrWhiteSpace(request.RequestedLanguage) ? target.ProgrammingLanguage : request.RequestedLanguage!;
        bool sclIntent = language.IndexOf("SCL", StringComparison.OrdinalIgnoreCase) >= 0 || request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase);
        bool create = request.Operation.Equals("Create", StringComparison.OrdinalIgnoreCase);
        bool vciFormatObserved = vci.ExactMappingFound && vci.SupportedFormats.Count > 0 &&
            (string.IsNullOrWhiteSpace(vci.MappingFormat) || vci.SupportedFormats.Any(format => format.Equals(vci.MappingFormat, StringComparison.OrdinalIgnoreCase)));
        var routes = new List<PlcCodeRouteDecision>
        {
            Decision("VCI", request.PreferSourceControl ? 1 : 2, !blocked && target.Exists && vciFormatObserved,
                blocked ? "Target safety/protection/identity state blocks mutation." : !target.Exists ? "VCI requires an existing project object and mapping." : !vci.ExactMappingFound ? "No exact existing VCI mapping was observed." : !vciFormatObserved ? "The exact mapping format was not confirmed by the target's observed supported-format set." : "Exact mapping and supported formats were observed; synchronize through VCI, outside the direct-import transaction engine."),
            Decision("DirectScl", sclIntent ? request.PreferSourceControl ? 2 : 1 : 3, !blocked && sclIntent && (create || io.SclSourceRouteObserved || (request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase) && target.Exists)),
                blocked ? "Target safety/protection/identity state blocks mutation." : !sclIntent ? "The requested/current language is not SCL and the target is not a PLC type." : create ? "Toolkit SCL validation/generation is installed for this declared new target; compile verification remains mandatory." : io.SclSourceRouteObserved || (request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase) && target.Exists) ? "Native SCL/source workflow is compatible with the observed target." : "Existing target could not be exported as native source."),
            Decision("DirectDocuments", !sclIntent ? request.PreferSourceControl ? 2 : 1 : 3, !blocked && !request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase) && !sclIntent && (create || io.DocumentRouteObserved),
                blocked ? "Target safety/protection/identity state blocks mutation." : request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase) ? "SIMATIC SD block documents do not represent PLC types." : sclIntent ? "Native SCL is preferred for an SCL target." : create ? "Toolkit S7DCL validation/import is installed for this declared new standard block; compile verification remains mandatory." : io.DocumentRouteObserved ? "Exact S7DCL export/import compatibility was observed." : "S7DCL export was not observed."),
            Decision("SimaticML", 4, !blocked && target.Exists && io.SimaticMlRouteObserved,
                blocked ? "Target safety/protection/identity state blocks mutation." : !target.Exists ? "SimaticML is baseline-bound and cannot create an unobserved target." : io.SimaticMlRouteObserved ? "Exact XML export was observed; use only for a minimal baseline-bound patch when text routes are unsuitable." : "Exact XML export was not observed.")
        };
        return routes.OrderBy(route => route.Rank).ThenBy(route => route.Route, StringComparer.Ordinal).ToArray();
    }

    private async Task<PlcCodeEnvironmentEvidence> ReadEnvironmentAsync(string plcName)
    {
        object? portal = _context.Portal;
        object? project = _context.Project;
        object? item = _context.CurrentPlcDeviceItem;
        string typeIdentifier = Scalar(item, "TypeIdentifier", "TypeName", "Classification");
        string orderNumber = Scalar(item, "OrderNumber", "ArticleNumber");
        string instanceVersion = string.Empty;
        try { instanceVersion = await _versionProvider().ConfigureAwait(false); }
        catch { }
        return new PlcCodeEnvironmentEvidence
        {
            TiaProcessId = _context.AttachedPortalProcessId,
            TiaVersion = FirstNonEmpty(instanceVersion, Scalar(portal, "Version", "ProductVersion"), Scalar(project, "Version"), portal?.GetType().Assembly.GetName().Version?.ToString()),
            ProjectName = TiaReflectionHelper.GetName(project) ?? string.Empty,
            ProjectPath = TiaReflectionHelper.GetProperty(project, "Path")?.ToString() ?? string.Empty,
            PlcName = plcName,
            PlcFamily = InferFamily(typeIdentifier + " " + orderNumber),
            PlcTypeIdentifier = typeIdentifier,
            PlcOrderNumber = orderNumber
        };
    }

    private PlcCodeSafetyEvidence ReadSafety(PlcCodeTargetEvidence target)
    {
        object? service = TryGetService(_context.CurrentPlcDeviceItem, "Siemens.Engineering.Safety.SafetyAdministration");
        bool password = Bool(service, "IsSafetyOfflineProgramPasswordSet");
        bool logged = Bool(service, "IsLoggedOnToSafetyOfflineProgram");
        bool failSafe = target.ProgrammingLanguage.StartsWith("F_", StringComparison.OrdinalIgnoreCase) || target.ProgrammingLanguage.IndexOf("Safety", StringComparison.OrdinalIgnoreCase) >= 0;
        bool blocked = target.IsReadOnly || failSafe;
        return new PlcCodeSafetyEvidence
        {
            ServiceAvailable = service != null,
            IsFailSafeTarget = failSafe,
            PasswordSet = password,
            LoggedOn = logged,
            MutationBlocked = blocked,
            Reason = target.IsReadOnly ? "The target is read-only/protected." : failSafe ? "Fail-safe PLC code requires a separately validated safety authoring workflow; standard code generation is blocked." : string.Empty
        };
    }

    private static PlcCodeRouteDecision Decision(string route, int rank, bool eligible, string reason) => new PlcCodeRouteDecision { Route = route, Rank = rank, Eligible = eligible, Reason = reason };
    private static PlcCodeCapabilityRequest Normalize(PlcCodeCapabilityRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.PlcName)) throw new InvalidOperationException("plc-name is required.");
        if (string.IsNullOrWhiteSpace(request.ObjectName)) throw new InvalidOperationException("object-name is required.");
        if (!request.ObjectKind.Equals("Block", StringComparison.OrdinalIgnoreCase) && !request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("object-kind must be Block or Type.");
        if (!request.Operation.Equals("Create", StringComparison.OrdinalIgnoreCase) && !request.Operation.Equals("Modify", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("operation must be Create or Modify.");
        string kind = request.ObjectKind.Equals("Type", StringComparison.OrdinalIgnoreCase) ? "Type" : "Block";
        string operation = request.Operation.Equals("Create", StringComparison.OrdinalIgnoreCase) ? "Create" : "Modify";
        return new PlcCodeCapabilityRequest { PlcName = request.PlcName.Trim(), ObjectKind = kind, ObjectName = request.ObjectName.Trim(), GroupPath = (request.GroupPath ?? string.Empty).Replace('\\', '/').Trim().Trim('/'), Operation = operation, BlockType = request.BlockType?.Trim(), RequestedLanguage = request.RequestedLanguage?.Trim(), PreferSourceControl = request.PreferSourceControl };
    }

    private static string ComputeFingerprint(PlcCodeCapabilityDiscovery value)
    {
        string json = JsonSerializer.Serialize(new
        {
            value.Request,
            value.Environment,
            value.Target,
            value.Safety,
            Vci = new { value.Vci.Available, value.Vci.WorkspaceName, value.Vci.ExactMappingFound, value.Vci.MappingFileBasePath, value.Vci.MappingFormat, value.Vci.MappingStatus, Formats = value.Vci.SupportedFormats.OrderBy(item => item, StringComparer.Ordinal).ToArray() },
            ImportExport = new { value.ImportExport.DocumentRouteObserved, value.ImportExport.SclSourceRouteObserved, value.ImportExport.SimaticMlRouteObserved },
            value.SelectedRoute,
            Routes = value.Routes.Select(route => new { route.Route, route.Rank, route.Eligible }).ToArray()
        });
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string Scalar(object? value, params string[] names)
    {
        foreach (string name in names) { object? property = TiaReflectionHelper.GetProperty(value, name); if (property != null) return property.ToString() ?? string.Empty; }
        return string.Empty;
    }
    private static bool Bool(object? value, string name) => bool.TryParse(Scalar(value, name), out bool result) && result;
    private static string InferFamily(string value) => value.IndexOf("1500", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("6ES7 5", StringComparison.OrdinalIgnoreCase) >= 0 ? "S7-1500" : value.IndexOf("1200", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("6ES7 2", StringComparison.OrdinalIgnoreCase) >= 0 ? "S7-1200" : value.IndexOf("300", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("6ES7 3", StringComparison.OrdinalIgnoreCase) >= 0 ? "S7-300" : value.IndexOf("400", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("6ES7 4", StringComparison.OrdinalIgnoreCase) >= 0 ? "S7-400" : string.Empty;
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static IEnumerable<SoftwareTreeNode> Flatten(SoftwareTreeNode? node) { if (node == null) yield break; yield return node; foreach (SoftwareTreeNode child in node.Children) foreach (SoftwareTreeNode nested in Flatten(child)) yield return nested; }
    private static string GroupFromTreePath(string path, string container, string objectName)
    {
        string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        int containerIndex = Array.FindIndex(parts, part => part.Equals(container, StringComparison.OrdinalIgnoreCase));
        int objectIndex = Array.FindLastIndex(parts, part => part.Equals(objectName, StringComparison.OrdinalIgnoreCase));
        return containerIndex < 0 || objectIndex <= containerIndex + 1 ? string.Empty : string.Join("/", parts.Skip(containerIndex + 1).Take(objectIndex - containerIndex - 1));
    }
    private static void SetGroupIdentity(PlcCodeTargetEvidence target, string requestedGroup)
    {
        string requested = (requestedGroup ?? string.Empty).Trim().Trim('/');
        if (target.GroupPath.Equals(requested, StringComparison.OrdinalIgnoreCase)) return;
        target.ExactIdentityResolved = false;
        target.IdentityIssue = $"Requested group '{requested}' does not match the observed exact target group '{target.GroupPath}'.";
    }
    private static object? TryGetService(object? target, string typeName)
    {
        if (target == null) return null;
        Type? type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(typeName, false, false)).FirstOrDefault(item => item != null);
        MethodInfo? method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(item => item.Name == "GetService" && item.IsGenericMethodDefinition && item.GetParameters().Length == 0);
        try { return type == null || method == null ? null : method.MakeGenericMethod(type).Invoke(target, null); } catch { return null; }
    }
    private static bool FixedEquals(string a, string b) { byte[] left = Encoding.UTF8.GetBytes(a); byte[] right = Encoding.UTF8.GetBytes(b); if (left.Length != right.Length) return false; int difference = 0; for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index]; return difference == 0; }
    private static string Message(Exception ex) => ex.InnerException?.Message ?? ex.Message;
    private static string CapabilityRoot => Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-capabilities");
    private static string CapabilityPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(ch => !char.IsLetterOrDigit(ch))) throw new InvalidOperationException("Invalid capability id.");
        return Path.Combine(CapabilityRoot, id + ".json");
    }
    private static void CleanupExpired()
    {
        if (!Directory.Exists(CapabilityRoot)) return;
        foreach (string path in Directory.EnumerateFiles(CapabilityRoot, "*.json"))
        {
            try
            {
                PlcCodeCapabilityDiscovery? item = JsonSerializer.Deserialize<PlcCodeCapabilityDiscovery>(File.ReadAllText(path), JsonOptions);
                if (item == null || item.ExpiresAtUtc <= DateTimeOffset.UtcNow) File.Delete(path);
            }
            catch { }
        }
    }
}
