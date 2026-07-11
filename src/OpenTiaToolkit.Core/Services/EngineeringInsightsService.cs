using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class EngineeringInsightsService : IEngineeringInsightsService
{
    private readonly EngineeringSessionContext _context;
    private readonly ILogger<EngineeringInsightsService>? _logger;

    public EngineeringInsightsService(EngineeringSessionContext context, ILogger<EngineeringInsightsService>? logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task<IReadOnlyList<EngineeringInsightItem>> ListUnifiedAlarmsAsync() => InGate(() =>
        EnumerateNamedCollections(RequireHmi(), "UnifiedHmi", new[] { "AlarmClasses", "AnalogAlarms", "DiscreteAlarms", "OpcUaAlarmTypes" }));

    public Task<IReadOnlyList<EngineeringInsightItem>> ListUnifiedRuntimeSettingsAsync() => InGate(() =>
    {
        object settings = TiaReflectionHelper.GetProperty(RequireHmi(), "RuntimeSettings")
            ?? throw new InvalidOperationException("The selected Unified HMI does not expose RuntimeSettings.");
        return (IReadOnlyList<EngineeringInsightItem>)new[] { ToInsight(settings, "UnifiedHmi", "RuntimeSettings", "RuntimeSettings") };
    });

    public Task<IReadOnlyList<EngineeringInsightItem>> ListUnifiedTextListsAsync() => InGate(() =>
        EnumerateNamedCollections(RequireHmi(), "UnifiedHmi", new[] { "HmiTextLists", "HmiSystemTextLists", "HmiGraphicLists" }));

    public Task<EngineeringValidationResult> ValidateSafetyAsync() => _context.RunExclusiveAsync(() =>
    {
        object? deviceItem = _context.CurrentPlcDeviceItem;
        if (deviceItem == null)
            return Task.FromResult(Unavailable("Safety", "Open a PLC before validating its safety administration state."));
        object? service = TryGetService(deviceItem, "Siemens.Engineering.Safety.SafetyAdministration");
        if (service == null)
            return Task.FromResult(Unavailable("Safety", "SafetyAdministration is not available; the selected PLC may not be fail-safe."));
        Dictionary<string, string?> properties = ScalarProperties(service);
        var warnings = new List<string>();
        if (properties.TryGetValue("IsSafetyOfflineProgramPasswordSet", out string? passwordSet) && string.Equals(passwordSet, "True", StringComparison.OrdinalIgnoreCase) &&
            properties.TryGetValue("IsLoggedOnToSafetyOfflineProgram", out string? loggedOn) && !string.Equals(loggedOn, "True", StringComparison.OrdinalIgnoreCase))
            warnings.Add("The safety offline program is password-protected and not currently logged on.");
        return Task.FromResult(new EngineeringValidationResult
        {
            Domain = "Safety",
            Available = true,
            Valid = warnings.Count == 0,
            Checks = new[] { "SafetyAdministration service resolved", "Offline safety login state inspected" },
            Warnings = warnings,
            Properties = properties
        });
    });

    public Task<EngineeringValidationResult> InspectMultiuserAsync() => _context.RunExclusiveAsync(() =>
    {
        object? session = _context.Session;
        object? project = _context.Project;
        if (session == null && project == null) return Task.FromResult(Unavailable("Multiuser", "No project or local session is selected."));
        bool isMultiuser = session != null || (project?.GetType().FullName?.IndexOf("Multiuser", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        var properties = new Dictionary<string, string?>
        {
            ["isMultiuser"] = isMultiuser.ToString(),
            ["sessionType"] = session?.GetType().FullName,
            ["projectType"] = project?.GetType().FullName,
            ["ownsSession"] = _context.OwnsCurrentSession.ToString()
        };
        return Task.FromResult(new EngineeringValidationResult
        {
            Domain = "Multiuser",
            Available = true,
            Valid = true,
            Checks = new[] { "Project kind inspected", "Local session ownership inspected" },
            Warnings = isMultiuser ? Array.Empty<string>() : new[] { "The current project is not a multiuser local session." },
            Properties = properties
        });
    });

    public Task<EngineeringValidationResult> InspectProjectSecurityAsync() => _context.RunExclusiveAsync(() =>
    {
        object? project = _context.Project;
        if (project == null) return Task.FromResult(Unavailable("ProjectSecurity", "No project is selected."));
        var properties = ScalarProperties(project, property =>
            property.Name.IndexOf("Author", StringComparison.OrdinalIgnoreCase) >= 0 ||
            property.Name.IndexOf("Modified", StringComparison.OrdinalIgnoreCase) >= 0 ||
            property.Name.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0);
        var probes = new[]
        {
            "Siemens.Engineering.Security.LocalCertificateManager",
            "Siemens.Engineering.Security.PlcPasswordPolicyService",
            "Siemens.Engineering.Security.SyslogServerProvider"
        };
        foreach (string probe in probes) properties["service:" + probe.Split('.').Last()] = (TryGetService(project, probe) != null).ToString();
        return Task.FromResult(new EngineeringValidationResult
        {
            Domain = "ProjectSecurity",
            Available = true,
            Valid = true,
            Checks = new[] { "Project metadata inspected", "Certificate/password/syslog service availability probed" },
            Warnings = Array.Empty<string>(),
            Properties = properties
        });
    });

    public Task<IReadOnlyList<EngineeringInsightItem>> GetWatchTableDetailsAsync() => InGate(() =>
        EnumerateGroup(TiaReflectionHelper.GetProperty(RequirePlc(), "WatchAndForceTableGroup"), "PLC", new[] { "WatchTables", "ForceTables" }, "WatchAndForceTables", includeEntries: true));

    public Task<IReadOnlyList<EngineeringInsightItem>> GetTechnologyObjectDetailsAsync() => InGate(() =>
        EnumerateGroup(TiaReflectionHelper.GetProperty(RequirePlc(), "TechnologicalObjectGroup"), "PLC", new[] { "TechnologicalObjects" }, "TechnologyObjects", includeEntries: true));

    public Task<IReadOnlyList<EngineeringInsightItem>> GetPlcAlarmTextListDetailsAsync() => InGate(() =>
        EnumerateGroup(TiaReflectionHelper.GetProperty(RequirePlc(), "PlcAlarmTextlistGroup"), "PLC", new[] { "PlcAlarmSystemTextlists", "PlcAlarmUserTextlists" }, "AlarmTextLists", includeEntries: true));

    private Task<IReadOnlyList<EngineeringInsightItem>> InGate(Func<IReadOnlyList<EngineeringInsightItem>> action) =>
        _context.RunExclusiveAsync(() => Task.FromResult(action()));

    private object RequireHmi() => _context.HmiSoftware ?? throw new InvalidOperationException("Open a Unified HMI before using this command.");
    private object RequirePlc() => _context.PlcSoftware ?? throw new InvalidOperationException("Open a PLC before using this command.");

    private static IReadOnlyList<EngineeringInsightItem> EnumerateNamedCollections(object root, string domain, IEnumerable<string> collectionNames)
    {
        var result = new List<EngineeringInsightItem>();
        foreach (string collectionName in collectionNames)
            foreach (object item in Enumerate(TiaReflectionHelper.GetProperty(root, collectionName)))
                result.Add(ToInsight(item, domain, collectionName, collectionName + "/" + (TiaReflectionHelper.GetName(item) ?? result.Count.ToString())));
        return result;
    }

    private static IReadOnlyList<EngineeringInsightItem> EnumerateGroup(object? group, string domain, string[] collections, string path, bool includeEntries)
    {
        var result = new List<EngineeringInsightItem>();
        CollectGroup(group, domain, collections, path, includeEntries, result);
        return result;
    }

    private static void CollectGroup(object? group, string domain, string[] collections, string path, bool includeEntries, List<EngineeringInsightItem> result)
    {
        if (group == null) return;
        foreach (string collection in collections)
        {
            foreach (object item in Enumerate(TiaReflectionHelper.GetProperty(group, collection)))
            {
                string name = TiaReflectionHelper.GetName(item) ?? result.Count.ToString();
                string itemPath = path + "/" + name;
                result.Add(ToInsight(item, domain, collection, itemPath));
                if (includeEntries)
                {
                    foreach (string childCollection in new[] { "Entries", "Parameters", "ListRange" })
                        foreach (object child in Enumerate(TiaReflectionHelper.GetProperty(item, childCollection)))
                            result.Add(ToInsight(child, domain, childCollection, itemPath + "/" + childCollection));
                }
            }
        }
        foreach (object nested in Enumerate(TiaReflectionHelper.GetProperty(group, "Groups")))
            CollectGroup(nested, domain, collections, path + "/" + (TiaReflectionHelper.GetName(nested) ?? "Group"), includeEntries, result);
    }

    private static EngineeringInsightItem ToInsight(object item, string domain, string kind, string path) => new EngineeringInsightItem
    {
        Domain = domain,
        Kind = kind,
        Name = TiaReflectionHelper.GetName(item) ?? string.Empty,
        Path = path,
        TypeName = item.GetType().FullName ?? item.GetType().Name,
        Properties = ScalarProperties(item)
    };

    private static Dictionary<string, string?> ScalarProperties(object value, Func<PropertyInfo, bool>? predicate = null)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 || property.Name == "Parent" || (predicate != null && !predicate(property))) continue;
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(Version))) continue;
            try { result[property.Name] = Convert.ToString(property.GetValue(value)); }
            catch (Exception ex) { result[property.Name] = "<unavailable: " + (ex.InnerException?.Message ?? ex.Message) + ">"; }
        }
        return result;
    }

    private static IEnumerable<object> Enumerate(object? value)
    {
        if (value is IEnumerable enumerable && value is not string)
            foreach (object? item in enumerable) if (item != null) yield return item;
    }

    private static object? TryGetService(object target, string serviceTypeName)
    {
        Type? serviceType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(serviceTypeName, false, false)).FirstOrDefault(type => type != null);
        if (serviceType == null) return null;
        MethodInfo? method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.Name == "GetService" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 0);
        if (method == null) return null;
        try { return method.MakeGenericMethod(serviceType).Invoke(target, null); }
        catch (Exception) { return null; }
    }

    private static EngineeringValidationResult Unavailable(string domain, string warning) => new EngineeringValidationResult
    {
        Domain = domain,
        Available = false,
        Valid = false,
        Warnings = new[] { warning }
    };
}
