using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed class PlcCodeFormatService
{
    private static readonly Regex SclDeclarationRegex = new Regex(@"(?im)^\s*(FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK|TYPE)\s+(?:""(?<quoted>[^""]+)""|(?<plain>[^\s:]+))", RegexOptions.Compiled);
    private static readonly Regex SclCallRegex = new Regex(@"(?im)(?<![#\w])(?:""(?<quoted>[^""]+)""|(?<plain>[A-Za-z_][A-Za-z0-9_]*))\s*\(", RegexOptions.Compiled);
    private static readonly Regex StatefulTypeRegex = new Regex(@"\b(TON|TOF|TP|TON_TIME|TOF_TIME|TP_TIME|IEC_TIMER|R_TRIG|F_TRIG|CTU|CTD|CTUD|IEC_COUNTER)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly PlcDocumentWorkflowService _documentWorkflow;

    public PlcCodeFormatService(PlcDocumentWorkflowService documentWorkflow) => _documentWorkflow = documentWorkflow;

    public IReadOnlyList<PlcCodeFormatCapability> DescribeFormats() => new[]
    {
        Capability("S7DCL-LAD", new[] { ".s7dcl", ".s7res" }, "verified-subset", "plc-code-transaction", true, true,
            new[] { "FC", "FB", "OB", "mixed LAD/SCL" }, new[] { "exact identity", "canonical export", "compile", "rollback hash" },
            new[] { "F-language unsupported", "unregistered instructions require live probe" }),
        Capability("S7DCL-DATA", new[] { ".s7dcl", ".s7res" }, "verified-subset", "plc-code-transaction", true, true,
            new[] { "GlobalDB" }, new[] { "layout impact", "exact identity", "canonical export", "compile" },
            new[] { "instance DB recreation unsupported", "UDT existence requires project discovery" }),
        Capability("SCL", new[] { ".scl" }, "verified-subset", "guarded-scl-source", true, true,
            new[] { "FC", "FB", "OB", "GlobalDB", "TYPE" }, new[] { "declared object set", "dependency order", "generated identities", "compile", "source/block/type rollback" },
            new[] { "safety source unsupported", "indirect/project calls require live compile" }),
        Capability("SimaticML", new[] { ".xml" }, "patch-only", "guarded-simaticml-import", true, true,
            new[] { "exported PLC block XML", "exported PLC type XML" }, new[] { "namespace", "identity", "ID graph", "canonical export", "compile", "XML rollback" },
            new[] { "transactional apply requires an existing baseline-bound object", "general FlgNet generation unsupported", "baseline export required for mutation" }),
        Capability("VCI", Array.Empty<string>(), "transport", "guarded-vci-sync", true, false,
            new[] { "mapped PLC objects" }, new[] { "mapping", "direction", "workspace file validation", "pre/post status" },
            new[] { "workspace-to-project is not a multi-object atomic transaction", "use narrow exact mappings" })
    };

    public PlcCodeArtifactValidationResult Validate(string format, string inputPath, string? baselinePath = null, bool allowFlgNetTopology = false)
    {
        string normalized = NormalizeFormat(format, inputPath);
        if (normalized == "SCL") return ValidateScl(inputPath);
        if (normalized == "SIMATICML") return ValidateSimaticMl(inputPath, baselinePath, allowFlgNetTopology);
        if (normalized == "S7DCL")
        {
            string fullPath = Path.GetFullPath(inputPath);
            PlcDocumentValidationResult result = _documentWorkflow.Validate(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath));
            return new PlcCodeArtifactValidationResult
            {
                Valid = result.Valid,
                Format = result.BlockType == "GlobalDB" ? "S7DCL-DATA" : "S7DCL-LAD",
                Classification = result.Classification,
                InputPath = result.S7dclPath,
                InputHash = result.DocumentHash,
                HasUtf8Bom = result.HasUtf8Bom,
                DeclaredObjects = string.IsNullOrWhiteSpace(result.BlockName) ? Array.Empty<PlcCodeDeclaredObject>() : new[] { new PlcCodeDeclaredObject { Name = result.BlockName!, ObjectType = result.BlockType ?? string.Empty, Number = result.BlockNumber, Line = 1 } },
                Findings = result.Instructions.Select(item => new PlcCodeValidationFinding { Construct = item.Instruction, Classification = item.Classification, Line = item.Line, Message = item.Guidance ?? string.Empty }).ToArray(),
                Dependencies = result.Dependencies,
                Errors = result.Errors,
                Warnings = result.Warnings
            };
        }
        throw new InvalidOperationException($"Unsupported PLC code format '{format}'.");
    }

    public PlcCodeArtifactValidationResult ValidateScl(string inputPath)
    {
        string path = Path.GetFullPath(inputPath);
        var errors = new List<string>();
        var warnings = new List<string>();
        var findings = new List<PlcCodeValidationFinding>();
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        if (!File.Exists(path)) errors.Add($"SCL source was not found: {path}");
        bool bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
        string text = DecodeUtf8(bytes, errors, "SCL");
        if (bytes.Length > 0 && !bom) warnings.Add("SCL is UTF-8 without BOM. UTF-8 BOM is required for reliable Siemens source import.");
        if (Path.GetExtension(path).Equals(".awl", StringComparison.OrdinalIgnoreCase)) errors.Add("AWL/STL is known-unsupported by the SCL authoring workflow.");
        if (Regex.IsMatch(text, @"\bF_(?:FUNCTION|FUNCTION_BLOCK|ORGANIZATION_BLOCK)\b|S7_F", RegexOptions.IgnoreCase)) errors.Add("Fail-safe SCL authoring is known-unsupported by this standard-code workflow.");

        var objects = new List<PlcCodeDeclaredObject>();
        foreach (Match match in SclDeclarationRegex.Matches(text))
        {
            string keyword = match.Groups[1].Value.ToUpperInvariant();
            string name = !string.IsNullOrWhiteSpace(match.Groups["quoted"].Value) ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
            string type = keyword == "FUNCTION" ? "FC" : keyword == "FUNCTION_BLOCK" ? "FB" : keyword == "ORGANIZATION_BLOCK" ? "OB" : keyword == "DATA_BLOCK" ? "GlobalDB" : "TYPE";
            objects.Add(new PlcCodeDeclaredObject { Name = name, ObjectType = type, Line = LineOf(text, match.Index) });
            string end = keyword == "FUNCTION" ? "END_FUNCTION" : keyword == "FUNCTION_BLOCK" ? "END_FUNCTION_BLOCK" : keyword == "ORGANIZATION_BLOCK" ? "END_ORGANIZATION_BLOCK" : keyword == "DATA_BLOCK" ? "END_DATA_BLOCK" : "END_TYPE";
            if (!Regex.IsMatch(text.Substring(match.Index), @"(?im)^\s*" + end + @"\s*;?\s*$")) errors.Add($"{keyword} '{name}' is missing {end}.");
        }
        if (objects.Count == 0 && bytes.Length > 0) errors.Add("No FUNCTION, FUNCTION_BLOCK, ORGANIZATION_BLOCK, DATA_BLOCK, or TYPE declaration was found.");
        foreach (IGrouping<string, PlcCodeDeclaredObject> duplicate in objects.GroupBy(item => item.ObjectType + ":" + item.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) errors.Add($"SCL declares '{duplicate.Key}' more than once.");

        foreach (Match block in SclDeclarationRegex.Matches(text))
        {
            string keyword = block.Groups[1].Value.ToUpperInvariant();
            int next = SclDeclarationRegex.Match(text, block.Index + block.Length).Index;
            if (next <= block.Index) next = text.Length;
            string body = text.Substring(block.Index, next - block.Index);
            foreach (Match temp in Regex.Matches(body, @"(?is)\bVAR_TEMP\b(?<body>.*?)\bEND_VAR\b"))
                if (StatefulTypeRegex.IsMatch(temp.Groups["body"].Value)) errors.Add($"Stateful IEC instance is declared in VAR_TEMP in {keyword} at line {LineOf(text, block.Index)}. Use FB static data or a global instance.");
            if (keyword == "FUNCTION" && Regex.IsMatch(body, @"(?im)^\s*VAR\s*$")) errors.Add($"FC at line {LineOf(text, block.Index)} declares static VAR data. Use VAR_TEMP or an FB.");
        }

        if (Regex.IsMatch(text, @"\bWHILE\b", RegexOptions.IgnoreCase))
        {
            warnings.Add("WHILE loop termination cannot be proven offline; guarded live compilation is required.");
            findings.Add(Finding("WHILE", "unknown-requires-live-probe", text, Regex.Match(text, @"\bWHILE\b", RegexOptions.IgnoreCase).Index, "Prove a bounded exit condition."));
        }
        foreach (Match @case in Regex.Matches(text, @"(?is)\bCASE\b.*?\bEND_CASE\b"))
            if (!Regex.IsMatch(@case.Value, @"\bELSE\b", RegexOptions.IgnoreCase)) warnings.Add($"CASE at line {LineOf(text, @case.Index)} has no ELSE safe default.");
        if (Regex.IsMatch(text, @"(?<![/])/(?![/])")) warnings.Add("Division is present; verify a zero-denominator guard and type conversions.");

        var declaredNames = new HashSet<string>(objects.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        string[] dependencies = SclCallRegex.Matches(text).Cast<Match>()
            .Select(match => !string.IsNullOrWhiteSpace(match.Groups["quoted"].Value) ? match.Groups["quoted"].Value : match.Groups["plain"].Value)
            .Where(name => !IsSclBuiltin(name) && !declaredNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string dependency in dependencies) findings.Add(new PlcCodeValidationFinding { Construct = dependency, Classification = "unknown-requires-live-probe", Message = "Project or library call must resolve and compile in the bound PLC." });
        string classification = errors.Count > 0 ? "known-unsupported" : dependencies.Length > 0 || findings.Any(item => item.Classification == "unknown-requires-live-probe") ? "unknown-requires-live-probe" : "verified";
        return new PlcCodeArtifactValidationResult { Valid = errors.Count == 0, Format = "SCL", Classification = classification, InputPath = path, InputHash = Hash(bytes), HasUtf8Bom = bom, DeclaredObjects = objects, Findings = findings, Dependencies = dependencies, Errors = errors, Warnings = warnings };
    }

    public PlcCodeArtifactValidationResult ValidateSimaticMl(string inputPath, string? baselinePath, bool allowFlgNetTopology)
    {
        string path = Path.GetFullPath(inputPath);
        var errors = new List<string>();
        var warnings = new List<string>();
        var findings = new List<PlcCodeValidationFinding>();
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        if (!File.Exists(path)) errors.Add($"SimaticML file was not found: {path}");
        XDocument? document = LoadXml(path, errors);
        var objects = new List<PlcCodeDeclaredObject>();
        if (document?.Root != null)
        {
            if (document.Root.Name.LocalName != "Document") errors.Add("SimaticML root element must be Document.");
            if (!document.Root.DescendantsAndSelf().Any(element => !string.IsNullOrWhiteSpace(element.Name.NamespaceName))) errors.Add("SimaticML must preserve at least one exported Siemens schema namespace, even when the Document root is unqualified.");
            string[] ids = document.Descendants().Attributes("ID").Select(attribute => attribute.Value).ToArray();
            foreach (IGrouping<string, string> duplicate in ids.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)) errors.Add($"SimaticML ID '{duplicate.Key}' is duplicated.");
            foreach (XElement compileUnit in document.Descendants().Where(element => element.Name.LocalName.EndsWith("CompileUnit", StringComparison.Ordinal)))
            {
                string[] uids = compileUnit.Descendants().Attributes("UId").Select(attribute => attribute.Value).ToArray();
                foreach (string uid in uids.Where(value => !int.TryParse(value, out _))) errors.Add($"FlgNet UId '{uid}' is not a decimal integer.");
                foreach (IGrouping<string, string> duplicate in uids.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)) errors.Add($"FlgNet UId '{duplicate.Key}' is duplicated in one compile unit.");
            }
            foreach (XElement element in document.Descendants().Where(IsSupportedXmlObject))
            {
                string type = XmlObjectType(element);
                string name = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value ?? string.Empty;
                objects.Add(new PlcCodeDeclaredObject { Name = name, ObjectType = type, Line = 1 });
            }
            if (objects.Count == 0) warnings.Add("No supported PLC block identity was found; this may be a PLC type or unsupported SimaticML object.");
        }

        string classification = "unknown-requires-live-probe";
        if (!string.IsNullOrWhiteSpace(baselinePath) && document != null)
        {
            XDocument? baseline = LoadXml(Path.GetFullPath(baselinePath!), errors);
            if (baseline?.Root != null)
            {
                if (baseline.Root.Name != document.Root!.Name) errors.Add("Candidate changed the SimaticML root namespace or schema identity.");
                string[] baselineObjects = ExtractXmlObjectKeys(baseline);
                string[] candidateObjects = ExtractXmlObjectKeys(document);
                if (!baselineObjects.SequenceEqual(candidateObjects, StringComparer.Ordinal)) errors.Add("Candidate changed the exported PLC object identity set.");
                string[] baselineIds = baseline.Descendants().Attributes("ID").Select(attribute => attribute.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                string[] candidateIds = document.Descendants().Attributes("ID").Select(attribute => attribute.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                if (!baselineIds.SequenceEqual(candidateIds, StringComparer.Ordinal)) errors.Add("Candidate changed SimaticML object IDs. Narrow patches must preserve exported IDs.");
                string baselineTopology = FlgNetTopologyFingerprint(baseline);
                string candidateTopology = FlgNetTopologyFingerprint(document);
                if (!allowFlgNetTopology && !string.Equals(baselineTopology, candidateTopology, StringComparison.Ordinal)) errors.Add("Candidate changes FlgNet topology. Use a disposable-project probe and allow-flgnet-topology only after explicit review.");
                classification = errors.Count == 0 ? "verified" : "known-unsupported";
                findings.Add(new PlcCodeValidationFinding { Construct = "baseline-diff", Classification = classification, Message = "Namespace, object identity, IDs, and FlgNet topology compared with the exported baseline." });
            }
        }
        else warnings.Add("Without an untouched exported baseline, SimaticML is never classified as verified for mutation.");
        return new PlcCodeArtifactValidationResult { Valid = errors.Count == 0, Format = "SimaticML", Classification = errors.Count > 0 ? "known-unsupported" : classification, InputPath = path, InputHash = Hash(bytes), HasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf, DeclaredObjects = objects, Findings = findings, Errors = errors, Warnings = warnings };
    }

    private static PlcCodeFormatCapability Capability(string format, string[] extensions, string status, string route, bool offline, bool transactional, string[] objects, string[] verification, string[] limitations) => new PlcCodeFormatCapability { Format = format, Extensions = extensions, AuthoringStatus = status, MutationRoute = route, OfflineValidation = offline, TransactionalApply = transactional, SupportedObjects = objects, RequiredVerification = verification, KnownLimitations = limitations };

    public string ComputeSimaticMlContentHash(string path)
    {
        var errors = new List<string>();
        XDocument? document = LoadXml(Path.GetFullPath(path), errors);
        if (document?.Root == null || errors.Count > 0) throw new InvalidDataException("SimaticML cannot be canonicalized: " + string.Join("; ", errors));
        XDocument canonical = new XDocument(document);
        canonical.Root!.Elements().Where(element => element.Name.LocalName == "Engineering" || element.Name.LocalName == "DocumentInfo").Remove();
        foreach (XText whitespace in canonical.DescendantNodes().OfType<XText>().Where(node => string.IsNullOrWhiteSpace(node.Value)).ToArray()) whitespace.Remove();
        return Hash(Encoding.UTF8.GetBytes(canonical.ToString(SaveOptions.DisableFormatting)));
    }
    private static string NormalizeFormat(string format, string path) { string value = (format ?? string.Empty).Trim().ToUpperInvariant(); if (value.Length > 0 && value != "AUTO") return value.Replace("-LAD", string.Empty).Replace("-DATA", string.Empty); string extension = Path.GetExtension(path).ToLowerInvariant(); return extension == ".scl" ? "SCL" : extension == ".xml" ? "SIMATICML" : "S7DCL"; }
    private static string DecodeUtf8(byte[] bytes, List<string> errors, string label) { if (bytes.Length == 0) return string.Empty; try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); } catch (DecoderFallbackException ex) { errors.Add($"{label} is not valid UTF-8: {ex.Message}"); return string.Empty; } }
    private static XDocument? LoadXml(string path, List<string> errors) { if (!File.Exists(path)) return null; try { var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }; using (XmlReader reader = XmlReader.Create(path, settings)) return XDocument.Load(reader, LoadOptions.PreserveWhitespace); } catch (Exception ex) { errors.Add("SimaticML XML is invalid or unsafe: " + ex.Message); return null; } }
    private static string[] ExtractXmlObjectKeys(XDocument document) => document.Descendants().Where(IsSupportedXmlObject).Select(element => XmlObjectType(element) + ":" + (element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value ?? string.Empty)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static bool IsSupportedXmlObject(XElement element) => Regex.IsMatch(element.Name.LocalName, @"\.(FC|FB|OB|GlobalDB|InstanceDB)$") || element.Name.LocalName.Equals("SW.Types.PlcStruct", StringComparison.Ordinal);
    private static string XmlObjectType(XElement element) => element.Name.LocalName.Equals("SW.Types.PlcStruct", StringComparison.Ordinal) ? "TYPE" : element.Name.LocalName.Substring(element.Name.LocalName.LastIndexOf('.') + 1);
    private static string FlgNetTopologyFingerprint(XDocument document) => string.Join("|", document.Descendants().Where(element => new[] { "Parts", "Wires", "Part", "Wire", "NameCon", "IdentCon" }.Contains(element.Name.LocalName, StringComparer.Ordinal)).Select(element => element.ToString(SaveOptions.DisableFormatting)));
    private static PlcCodeValidationFinding Finding(string construct, string classification, string text, int index, string message) => new PlcCodeValidationFinding { Construct = construct, Classification = classification, Line = LineOf(text, index), Message = message };
    private static int LineOf(string text, int index) => 1 + text.Take(Math.Max(0, index)).Count(character => character == '\n');
    private static bool IsSclBuiltin(string name) => new[] { "ABS", "MIN", "MAX", "LIMIT", "SEL", "MOVE", "TON", "TOF", "TP", "R_TRIG", "F_TRIG", "CTU", "CTD", "CTUD", "SHL", "SHR", "ROL", "ROR", "ROUND", "TRUNC", "CEIL", "FLOOR" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
}
