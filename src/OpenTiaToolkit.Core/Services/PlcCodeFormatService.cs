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
    public const string ValidatorCatalogVersion = "plc-code-formats/1.2";
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
        PlcCodeArtifactValidationResult result;
        if (normalized == "SCL") result = ValidateScl(inputPath);
        else if (normalized == "SIMATICML") result = ValidateSimaticMl(inputPath, baselinePath, allowFlgNetTopology);
        else if (normalized == "S7DCL")
        {
            string fullPath = Path.GetFullPath(inputPath);
            PlcDocumentValidationResult document = _documentWorkflow.Validate(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath));
            result = new PlcCodeArtifactValidationResult
            {
                Valid = document.Valid,
                Format = document.BlockType == "GlobalDB" ? "S7DCL-DATA" : "S7DCL-LAD",
                Classification = document.Classification,
                InputPath = document.S7dclPath,
                InputHash = document.DocumentHash,
                HasUtf8Bom = document.HasUtf8Bom,
                DeclaredObjects = string.IsNullOrWhiteSpace(document.BlockName) ? Array.Empty<PlcCodeDeclaredObject>() : new[] { new PlcCodeDeclaredObject { Name = document.BlockName!, ObjectType = document.BlockType ?? string.Empty, Number = document.BlockNumber, Line = 1 } },
                Findings = document.Instructions.Select(item => new PlcCodeValidationFinding { Construct = item.Instruction, Classification = item.Classification, Line = item.Line, Message = item.Guidance ?? string.Empty }).ToArray(),
                Dependencies = document.Dependencies,
                Errors = document.Errors,
                Warnings = document.Warnings
            };
        }
        else throw new InvalidOperationException($"Unsupported PLC code format '{format}'.");
        result.QualificationSignature = ComputeQualificationSignature(result);
        return result;
    }

    public string ComputeQualificationSignature(PlcCodeArtifactValidationResult validation)
    {
        string normalizedContent;
        if (validation.Format.Equals("SimaticML", StringComparison.OrdinalIgnoreCase)) normalizedContent = ComputeSimaticMlContentHash(validation.InputPath);
        else
        {
            string text = File.Exists(validation.InputPath) ? DecodeUtf8(File.ReadAllBytes(validation.InputPath), new List<string>(), validation.Format) : string.Empty;
            int identity = 0;
            foreach (PlcCodeDeclaredObject declared in validation.DeclaredObjects.OrderBy(item => item.Line).ThenBy(item => item.ObjectType, StringComparer.Ordinal))
            {
                string placeholder = "__PLC_OBJECT_" + (++identity) + "__";
                string pattern = "\"" + Regex.Escape(declared.Name) + "\"|(?<![A-Za-z0-9_])" + Regex.Escape(declared.Name) + "(?![A-Za-z0-9_])";
                text = Regex.Replace(text, pattern, placeholder, RegexOptions.IgnoreCase);
            }
            if (validation.Format.StartsWith("S7DCL", StringComparison.OrdinalIgnoreCase))
                text = Regex.Replace(text, @"(?i)(S7_BlockNumber\s*:=\s*"")[^""]+("")", "$1__PLC_BLOCK_NUMBER__$2");
            normalizedContent = string.Join("\n", text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0));
            if (validation.Format.StartsWith("S7DCL", StringComparison.OrdinalIgnoreCase))
            {
                string resource = Path.ChangeExtension(validation.InputPath, ".s7res");
                normalizedContent += "|resource:" + (File.Exists(resource) ? Hash(File.ReadAllBytes(resource)) : "missing");
            }
        }
        string constructCatalog = string.Join("|", validation.DeclaredObjects.Select(item => item.ObjectType).OrderBy(value => value, StringComparer.Ordinal)
            .Concat(validation.Findings.Select(item => item.Construct + ":" + item.Classification).OrderBy(value => value, StringComparer.Ordinal))
            .Concat(validation.Dependencies.Select(value => "dependency:" + value).OrderBy(value => value, StringComparer.Ordinal)));
        return Hash(Encoding.UTF8.GetBytes(ValidatorCatalogVersion + "|" + validation.Format + "|" + constructCatalog + "|" + normalizedContent));
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

    public PlcCodeChangeContract AnalyzeS7DclChange(string candidateDirectory, string candidateFileName, string baselineDirectory, string baselineFileName, string? allowedChanges)
    {
        PlcDocumentValidationResult candidate = _documentWorkflow.Validate(candidateDirectory, candidateFileName);
        PlcDocumentValidationResult baseline = _documentWorkflow.Validate(baselineDirectory, baselineFileName);
        if (!candidate.Valid) throw new InvalidDataException("Candidate SIMATIC SD documents are invalid: " + string.Join("; ", candidate.Errors));
        if (!baseline.Valid) throw new InvalidDataException("Baseline SIMATIC SD documents are invalid: " + string.Join("; ", baseline.Errors));
        if (!string.Equals(candidate.BlockName, baseline.BlockName, StringComparison.Ordinal) ||
            !string.Equals(candidate.BlockType, baseline.BlockType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.BlockNumber ?? string.Empty, baseline.BlockNumber ?? string.Empty, StringComparison.Ordinal))
            throw new InvalidDataException("Baseline-bound modification must preserve the exact block name, type, and number.");

        string[] allowed = ParseAllowedS7DclSections(allowedChanges);
        string candidateText = ReadUtf8(candidate.S7dclPath);
        string baselineText = ReadUtf8(baseline.S7dclPath);
        IReadOnlyDictionary<string, string> candidateSections = S7DclSections(candidateText, candidate.S7resPath);
        IReadOnlyDictionary<string, string> baselineSections = S7DclSections(baselineText, baseline.S7resPath);
        string[] changed = new[] { "Attributes", "Interface", "Implementation", "Resources" }
            .Where(section => !string.Equals(candidateSections[section], baselineSections[section], StringComparison.Ordinal))
            .ToArray();
        if (changed.Length == 0) throw new InvalidDataException("Baseline-bound modification is a no-op; candidate and baseline have no semantic section changes.");
        string[] unauthorized = changed.Where(section => !allowed.Contains(section, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unauthorized.Length > 0) throw new InvalidDataException("Candidate changes unauthorized S7DCL section(s): " + string.Join(", ", unauthorized) + ".");
        string baselineHash = HashDocumentArtifact(baseline.S7dclPath, baseline.S7resPath);
        string candidateHash = HashDocumentArtifact(candidate.S7dclPath, candidate.S7resPath);
        string fingerprint = Hash(Encoding.UTF8.GetBytes("s7dcl-change/1.0|" + baselineHash + "|" + candidateHash + "|" + string.Join(",", allowed) + "|" + string.Join(",", changed)));
        return new PlcCodeChangeContract
        {
            Mode = "BaselineBound",
            BaselineHash = baselineHash,
            CandidateHash = candidateHash,
            Fingerprint = fingerprint,
            AllowedSections = allowed,
            ChangedSections = changed
        };
    }

    private static string[] ParseAllowedS7DclSections(string? value)
    {
        string[] allowed = (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] supported = { "Attributes", "Interface", "Implementation", "Resources" };
        string[] invalid = allowed.Where(item => !supported.Contains(item, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (invalid.Length > 0) throw new InvalidDataException("Unsupported allowed-changes value(s): " + string.Join(", ", invalid) + ".");
        if (allowed.Length == 0) throw new InvalidDataException("baseline-bound requires at least one allowed-changes section.");
        return supported.Where(item => allowed.Contains(item, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static IReadOnlyDictionary<string, string> S7DclSections(string text, string? resourcePath)
    {
        Match declaration = Regex.Match(text, @"(?im)^\s*(?:FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK)\b[^\r\n]*");
        if (!declaration.Success) throw new InvalidDataException("S7DCL declaration could not be segmented for change analysis.");
        string attributes = NormalizeSection(text.Substring(0, declaration.Index));
        string remainder = text.Substring(declaration.Index + declaration.Length);
        MatchCollection interfaceMatches = Regex.Matches(remainder, @"(?is)\bVAR(?:_INPUT|_OUTPUT|_IN_OUT|_TEMP|_CONSTANT|_EXTERNAL)?\b.*?\bEND_VAR\b");
        string interfaces = NormalizeSection(string.Join("\n", interfaceMatches.Cast<Match>().Select(match => match.Value)));
        string implementation = remainder;
        foreach (Match match in interfaceMatches.Cast<Match>().Reverse()) implementation = implementation.Remove(match.Index, match.Length);
        implementation = Regex.Replace(implementation, @"(?im)^\s*END_(?:FUNCTION|FUNCTION_BLOCK|DATA_BLOCK|ORGANIZATION_BLOCK)\s*$", string.Empty);
        string resources = resourcePath != null && File.Exists(resourcePath) ? NormalizeSection(ReadUtf8(resourcePath)) : string.Empty;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Attributes"] = attributes,
            ["Interface"] = interfaces,
            ["Implementation"] = NormalizeSection(implementation),
            ["Resources"] = resources
        };
    }

    private static string NormalizeSection(string value) => string.Join("\n", value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => line.TrimEnd())).Trim();
    private static string ReadUtf8(string path) => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
    private static string HashDocumentArtifact(string s7dclPath, string? s7resPath)
    {
        byte[] document = File.ReadAllBytes(s7dclPath);
        byte[] resource = s7resPath != null && File.Exists(s7resPath) ? File.ReadAllBytes(s7resPath) : Array.Empty<byte>();
        return Hash(document.Concat(new byte[] { 0 }).Concat(resource).ToArray());
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
