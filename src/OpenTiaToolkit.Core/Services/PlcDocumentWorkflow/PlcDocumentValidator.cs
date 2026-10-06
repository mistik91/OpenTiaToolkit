using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcDocumentValidator
{
    private static readonly Regex ResourceReferenceRegex = new Regex(@"\bMLC_[A-Za-z0-9_]+\b", RegexOptions.Compiled);
    private static readonly Regex ResourceDefinitionRegex = new Regex(@"(?im)^\s*(?:-\s*)?id\s*:\s*(MLC_[A-Za-z0-9_]+)\s*", RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new Regex(@"(?im)^\s*(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK)\s+(?:""(?<quoted>[^""]+)""|(?<plain>[^\s:]+))", RegexOptions.Compiled);
    private static readonly Regex BlockNumberRegex = new Regex(@"S7_BlockNumber\s*:=\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PreferredLanguageRegex = new Regex(@"S7_PreferredLanguage\s*:=\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CallRegex = new Regex(@"(?<![\w""#])(?:(?:#[A-Za-z_][\w]*|""[^""]+""(?:\.[A-Za-z_][\w]*)*)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{[^{}]*\})?\s*\(", RegexOptions.Compiled);
    private static readonly Regex QuotedBlockCallRegex = new Regex(@"""(?<name>[^""]+)""\s*\(", RegexOptions.Compiled);
    private static readonly Regex UnsupportedDecoratedMoveRegex = new Regex(@"\bMove\s*\{", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WireRegex = new Regex(@"\bwire#(?<name>[A-Za-z0-9_]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> VerifiedInstructions = new HashSet<string>(new[] { "Contact", "I_Contact", "Coil", "I_Coil", "S_Coil", "R_Coil", "Not", "P_Trig", "GT_Contact", "LT_Contact", "EQ_Contact", "NE_Contact", "GE_Contact", "LE_Contact", "P_Contact", "N_Contact", "Move", "Add", "Sub", "Mul", "Div", "Normalize", "Scale", "TON", "TOF", "TP", "TON_Coil", "TOF_Coil", "TP_Coil", "TONR_Coil", "RT_Coil", "ATTR_DB" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> KnownUnsupportedInstructions = new HashSet<string>(new[] { "Gt", "Lt", "Eq", "Ne", "Ge", "Le", "PBox", "NBox" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TypedInstructions = new HashSet<string>(new[] { "GT_Contact", "LT_Contact", "EQ_Contact", "NE_Contact", "GE_Contact", "LE_Contact", "Add", "Sub", "Mul", "Div", "Normalize", "Scale" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DualTypedInstructions = new HashSet<string>(new[] { "Normalize", "Scale" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> StatefulInstructions = new HashSet<string>(new[] { "TON", "TOF", "TP", "CTU", "CTD", "CTUD" }, StringComparer.OrdinalIgnoreCase);

    internal PlcDocumentValidationResult Validate(string importDirectory, string fileName)
    {
        string directory = Path.GetFullPath(importDirectory ?? string.Empty);
        string baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var errors = new List<string>(); var warnings = new List<string>();
        string s7dclPath = Path.Combine(directory, baseName + ".s7dcl"); string s7resPath = Path.Combine(directory, baseName + ".s7res");
        if (!Directory.Exists(directory)) errors.Add($"Import directory was not found: {directory}");
        if (string.IsNullOrWhiteSpace(baseName)) errors.Add("file-name is required.");
        if (!File.Exists(s7dclPath)) errors.Add($"S7DCL file was not found: {s7dclPath}");
        byte[] documentBytes = File.Exists(s7dclPath) ? File.ReadAllBytes(s7dclPath) : Array.Empty<byte>();
        bool hasBom = documentBytes.Length >= 3 && documentBytes[0] == 0xEF && documentBytes[1] == 0xBB && documentBytes[2] == 0xBF;
        string text = DecodeUtf8(documentBytes, errors, "S7DCL");
        if (documentBytes.Length > 0 && !hasBom) warnings.Add("S7DCL is UTF-8 without BOM. UTF-8 BOM is recommended for Siemens imports and required for reliable non-ASCII text.");
        Match blockMatch = BlockRegex.Match(text);
        if (!blockMatch.Success && documentBytes.Length > 0) errors.Add("No supported FUNCTION, FUNCTION_BLOCK, DATA_BLOCK, or ORGANIZATION_BLOCK declaration was found.");
        string? blockName = blockMatch.Success ? FirstNonEmpty(blockMatch.Groups["quoted"].Value, blockMatch.Groups["plain"].Value) : null;
        string? blockType = blockMatch.Success ? NormalizeBlockType(blockMatch.Groups[1].Value) : null;
        if (!string.IsNullOrWhiteSpace(blockName) && !string.Equals(blockName, baseName, StringComparison.Ordinal)) errors.Add($"Document block name '{blockName}' does not match file-name '{baseName}'.");
        ValidateBlockBoundaries(text, blockType, errors);
        if (string.Equals(blockType, "GlobalDB", StringComparison.OrdinalIgnoreCase)) ValidateDataBlockDocument(text, errors, warnings);
        if (Regex.IsMatch(text, @"S7_PreferredLanguage\s*:=\s*""F_", RegexOptions.IgnoreCase) || Regex.IsMatch(text, @"\bS7_F", RegexOptions.IgnoreCase)) errors.Add("Safety-language S7DCL authoring is known-unsupported by this standard-block workflow. Use a separately validated safety workflow.");
        string[] references = ResourceReferenceRegex.Matches(text).Cast<Match>().Select(match => match.Value).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        bool resourceRequired = references.Length > 0; string? resourceHash = null;
        if (resourceRequired && !File.Exists(s7resPath)) errors.Add("The S7DCL contains MLC_* references, but the matching S7RES file is missing.");
        if (File.Exists(s7resPath))
        {
            byte[] resourceBytes = File.ReadAllBytes(s7resPath); string resourceText = DecodeUtf8(resourceBytes, errors, "S7RES"); resourceHash = PlcWorkflowPrimitives.Hash(resourceBytes);
            string[] definitionRows = ResourceDefinitionRegex.Matches(resourceText).Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            foreach (IGrouping<string, string> duplicate in definitionRows.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)) errors.Add($"S7RES defines resource '{duplicate.Key}' more than once.");
            var definitions = new HashSet<string>(definitionRows, StringComparer.Ordinal);
            foreach (string reference in references.Where(reference => !definitions.Contains(reference))) errors.Add($"S7RES does not define referenced resource '{reference}'.");
            foreach (string unused in definitions.Where(definition => !references.Contains(definition, StringComparer.Ordinal))) warnings.Add($"S7RES resource '{unused}' is not referenced by the S7DCL document.");
            if (resourceRequired && !Regex.IsMatch(resourceText, @"(?im)^\s+[A-Za-z]{2}-[A-Za-z]{2}\s*:")) errors.Add("S7RES resources do not contain any culture text rows.");
            if (resourceRequired && !Regex.IsMatch(resourceText, @"(?im)^\s+en-US\s*:")) warnings.Add("S7RES has no en-US text. Siemens LAD imports can reject resources that only contain another culture.");
        }
        IReadOnlyList<PlcDocumentInstructionValidation> instructions = ValidateInstructions(text, blockType, errors, warnings);
        string[] dependencies = instructions.Where(item => item.Classification == "unknown-requires-live-probe" && item.Guidance != null && item.Guidance.IndexOf("Block calls", StringComparison.OrdinalIgnoreCase) >= 0).Select(item => item.Instruction).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        string classification = errors.Any(error => error.IndexOf("known-unsupported", StringComparison.OrdinalIgnoreCase) >= 0) ? "known-unsupported" : instructions.Any(item => item.Classification == "unknown-requires-live-probe") ? "unknown-requires-live-probe" : "verified";
        return new PlcDocumentValidationResult { Valid = errors.Count == 0, Classification = classification, ImportDirectory = directory, FileName = baseName, S7dclPath = s7dclPath, S7resPath = File.Exists(s7resPath) ? s7resPath : null, DocumentHash = PlcWorkflowPrimitives.Hash(documentBytes), ResourceHash = resourceHash, BlockName = blockName, BlockType = blockType, PreferredLanguage = Capture(PreferredLanguageRegex, text), BlockNumber = Capture(BlockNumberRegex, text), HasUtf8Bom = hasBom, ResourceRequired = resourceRequired, ResourceReferences = references, Instructions = instructions, Dependencies = dependencies, Errors = errors, Warnings = warnings };
    }

    private static IReadOnlyList<PlcDocumentInstructionValidation> ValidateInstructions(string text, string? blockType, List<string> errors, List<string> warnings)
    {
        var results = new List<PlcDocumentInstructionValidation>();
        foreach (Match match in CallRegex.Matches(text))
        {
            string name = match.Groups["name"].Value; int line = LineOf(text, match.Index);
            if (KnownUnsupportedInstructions.Contains(name) || (name.Equals("Move", StringComparison.OrdinalIgnoreCase) && UnsupportedDecoratedMoveRegex.IsMatch(match.Value)))
            {
                results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = "known-unsupported", Line = line, Guidance = "Use the import-verified S7DCL form from the LAD grammar reference." }); errors.Add($"Line {line}: known-unsupported S7DCL instruction form '{name}'."); continue;
            }
            bool verifiedSclCounter = name.Equals("CTU", StringComparison.OrdinalIgnoreCase) && IsSclNetworkCall(text, match.Index);
            if (VerifiedInstructions.Contains(name) || verifiedSclCounter)
            {
                bool inferredType = TypedInstructions.Contains(name) && !HasSourceTypeTemplateBeforeCall(text, match.Index, DualTypedInstructions.Contains(name));
                string classification = inferredType ? "unknown-requires-live-probe" : "verified";
                results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = classification, Line = line, Guidance = inferredType ? "Siemens may infer the operand type from connected values, but agent-authored calls should emit an explicit S7_Templates SrcType and require a live compile when it is absent." : null });
                if (inferredType) warnings.Add($"Typed instruction {name} at line {line} has no preceding S7_Templates SrcType in its network. Siemens-exported inferred forms are accepted only with guarded live compile verification.");
                ValidateInstructionArguments(text, match, name, blockType, errors);
            }
            else
            {
                bool projectCall = !StatefulInstructions.Contains(name) && !name.Equals("N_Trig", StringComparison.OrdinalIgnoreCase);
                results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = "unknown-requires-live-probe", Line = line, Guidance = projectCall ? "Block calls are project-specific and require Siemens import plus compile verification." : "Keep the guarded apply and require Siemens import plus zero-error compile verification." });
                warnings.Add($"Line {line}: instruction or block call '{name}' is not in the import-verified registry and requires a live probe.");
                if (StatefulInstructions.Contains(name) || name.Equals("N_Trig", StringComparison.OrdinalIgnoreCase)) ValidateInstructionArguments(text, match, name, blockType, errors);
            }
        }
        foreach (Match match in QuotedBlockCallRegex.Matches(text)) { int line = LineOf(text, match.Index); string name = match.Groups["name"].Value; results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = "unknown-requires-live-probe", Line = line, Guidance = "Block calls are project-specific and require Siemens import plus compile verification." }); warnings.Add($"Line {line}: block call '{name}' requires a live probe against the target project."); }
        foreach (IGrouping<string, Match> wire in WireRegex.Matches(text).Cast<Match>().Where(match => !match.Groups["name"].Value.Equals("powerrail", StringComparison.OrdinalIgnoreCase)).GroupBy(match => match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)) if (wire.Count() < 2) errors.Add($"Wire junction 'wire#{wire.Key}' is referenced only once; every branch junction must have a tap and a join.");
        return results;
    }

    private static void ValidateInstructionArguments(string text, Match call, string name, string? blockType, List<string> errors)
    {
        string arguments = ExtractParenthesized(text, call.Index + call.Length - 1); bool Has(string parameter) => Regex.IsMatch(arguments, @"(?i)\b" + Regex.Escape(parameter) + @"\s*(?::=|=>)");
        if (name.Equals("Move", StringComparison.OrdinalIgnoreCase) && (!Has("in") || !Has("out1"))) errors.Add($"Instruction Move at line {LineOf(text, call.Index)} requires named 'in' and 'out1' parameters.");
        if (new[] { "Add", "Sub", "Mul", "Div" }.Contains(name, StringComparer.OrdinalIgnoreCase) && (!Has("in1") || !Has("in2") || !Has("out"))) errors.Add($"Instruction {name} at line {LineOf(text, call.Index)} requires named 'in1', 'in2', and 'out' parameters.");
        if (new[] { "Normalize", "Scale" }.Contains(name, StringComparer.OrdinalIgnoreCase)) foreach (string parameter in new[] { "min", "value", "max", "out" }) if (!Has(parameter)) errors.Add($"Instruction {name} at line {LineOf(text, call.Index)} requires named '{parameter}'.");
        if (name.Equals("ATTR_DB", StringComparison.OrdinalIgnoreCase)) foreach (string parameter in new[] { "REQ", "DB_NUMBER", "RET_VAL", "DB_LENGTH", "ATTRIB" }) if (!Has(parameter)) errors.Add($"Instruction ATTR_DB at line {LineOf(text, call.Index)} requires named '{parameter}'.");
        if (TypedInstructions.Contains(name) && name.EndsWith("_Contact", StringComparison.OrdinalIgnoreCase) && (!Has("in1") || !Has("in2"))) errors.Add($"Instruction {name} at line {LineOf(text, call.Index)} requires named 'in1' and 'in2' parameters.");
        if (new[] { "TON", "TOF", "TP", "TON_Coil", "TOF_Coil", "TP_Coil", "TONR_Coil" }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            if (!Has("pt")) errors.Add($"Timer instruction {name} at line {LineOf(text, call.Index)} requires named 'pt'."); if (name.EndsWith("_Coil", StringComparison.OrdinalIgnoreCase) && !Has("timer")) errors.Add($"Timer coil {name} at line {LineOf(text, call.Index)} requires named 'timer'.");
            string receiver = call.Value.Substring(0, call.Value.LastIndexOf(name, StringComparison.OrdinalIgnoreCase)); if (string.Equals(blockType, "FC", StringComparison.OrdinalIgnoreCase) && receiver.TrimStart().StartsWith("#", StringComparison.Ordinal)) errors.Add($"Timer instance {receiver.Trim().TrimEnd('.')} at line {LineOf(text, call.Index)} is local to an FC. Use an FB static or a global DB timer instance.");
        }
        if (new[] { "P_Contact", "N_Contact" }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (string parameter in new[] { "operand", "bit" }) if (!Has(parameter)) errors.Add($"Edge contact {name} at line {LineOf(text, call.Index)} requires named '{parameter}'.");
            Match bit = Regex.Match(arguments, @"(?i)\bbit\s*:=\s*#(?<name>[A-Za-z_][A-Za-z0-9_]*)"); if (bit.Success && !IsDeclaredInRetainedEdgeSection(text, bit.Groups["name"].Value)) errors.Add($"Edge contact {name} memory '#{bit.Groups["name"].Value}' at line {LineOf(text, call.Index)} must be Bool in FB static VAR or VAR_IN_OUT, not VAR_TEMP.");
        }
        if (new[] { "CTU", "CTD", "CTUD" }.Contains(name, StringComparer.OrdinalIgnoreCase)) { string[] required = name.Equals("CTU", StringComparison.OrdinalIgnoreCase) ? new[] { "CU", "R", "PV" } : name.Equals("CTD", StringComparison.OrdinalIgnoreCase) ? new[] { "CD", "LD", "PV" } : new[] { "CU", "CD", "R", "LD", "PV" }; foreach (string parameter in required) if (!Has(parameter)) errors.Add($"Counter instruction {name} at line {LineOf(text, call.Index)} requires named '{parameter}'."); }
        Match localReceiver = Regex.Match(call.Value, @"#(?<name>[A-Za-z_][A-Za-z0-9_]*)\."); if (localReceiver.Success && StatefulInstructions.Contains(name) && string.Equals(blockType, "FB", StringComparison.OrdinalIgnoreCase) && !IsDeclaredInStaticSection(text, localReceiver.Groups["name"].Value)) errors.Add($"Stateful instruction instance '#{localReceiver.Groups["name"].Value}' at line {LineOf(text, call.Index)} must be declared in the FB static VAR section, not VAR_TEMP or an interface section.");
        if (name.Equals("P_Trig", StringComparison.OrdinalIgnoreCase) || name.Equals("N_Trig", StringComparison.OrdinalIgnoreCase)) { Match operand = Regex.Match(arguments, @"#(?<name>[A-Za-z_][A-Za-z0-9_]*)"); if (!operand.Success) errors.Add($"{name} at line {LineOf(text, call.Index)} requires a local edge-memory operand."); else if (!IsDeclaredInLocalOrStaticSection(text, operand.Groups["name"].Value, "Bool")) errors.Add($"Edge-memory operand '#{operand.Groups["name"].Value}' at line {LineOf(text, call.Index)} must be declared as Bool in FB static or temporary data."); }
    }

    private static void ValidateBlockBoundaries(string text, string? blockType, List<string> errors)
    {
        if (blockType == "FC" && !Regex.IsMatch(text, @"(?im)^\s*END_FUNCTION\s*$")) errors.Add("FUNCTION is missing END_FUNCTION.");
        if (blockType == "FB" && !Regex.IsMatch(text, @"(?im)^\s*END_FUNCTION_BLOCK\s*$")) errors.Add("FUNCTION_BLOCK is missing END_FUNCTION_BLOCK.");
        if (blockType == "GlobalDB" && !Regex.IsMatch(text, @"(?im)^\s*END_DATA_BLOCK\s*$")) errors.Add("DATA_BLOCK is missing END_DATA_BLOCK.");
        if (blockType == "OB" && !Regex.IsMatch(text, @"(?im)^\s*END_ORGANIZATION_BLOCK\s*$")) errors.Add("ORGANIZATION_BLOCK is missing END_ORGANIZATION_BLOCK.");
        if (Regex.Matches(text, @"(?i)(?<!END_)\bNETWORK\b").Count != Regex.Matches(text, @"(?i)\bEND_NETWORK\b").Count) errors.Add("NETWORK and END_NETWORK counts do not match.");
        if (Regex.Matches(text, @"(?i)(?<!END_)\bRUNG\b").Count != Regex.Matches(text, @"(?i)\bEND_RUNG\b").Count) errors.Add("RUNG and END_RUNG counts do not match.");
    }

    private static void ValidateDataBlockDocument(string text, List<string> errors, List<string> warnings)
    {
        Match variableSection = Regex.Match(text, @"(?is)\bVAR\b(?<body>.*?)\bEND_VAR\b"); if (!variableSection.Success) { errors.Add("Global DATA_BLOCK must contain a VAR/END_VAR declaration section."); return; }
        string body = variableSection.Groups["body"].Value; if (Regex.IsMatch(body, @"(?i)\bS7_Retain\s*:=")) errors.Add("Member-level S7_Retain is known-unsupported: TIA Portal V21 rejected this authored attribute form. Preserve an exported member attribute or use the block-level S7_StandardRetain policy.");
        int structStarts = Regex.Matches(body, @"(?i)\bSTRUCT\b").Count, structEnds = Regex.Matches(body, @"(?i)\bEND_STRUCT\b").Count; if (structStarts != structEnds) errors.Add($"Global DB STRUCT/END_STRUCT counts do not match ({structStarts}/{structEnds}).");
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match declaration in Regex.Matches(body, @"(?im)^\s*(?:\{[^\r\n]*\}\s*)?(?:""(?<quoted>[^""]+)""|(?<plain>[A-Za-z_][A-Za-z0-9_]*))\s*:\s*(?<type>[^;]+);"))
        {
            string name = FirstNonEmpty(declaration.Groups["quoted"].Value, declaration.Groups["plain"].Value)!; string type = declaration.Groups["type"].Value.Trim(); if (declarations.ContainsKey(name)) errors.Add($"Global DB member '{name}' is declared more than once."); else declarations.Add(name, type);
            foreach (Match array in Regex.Matches(type, @"(?i)Array\s*\[\s*(?<lower>-?\d+)\s*\.\.\s*(?<upper>-?\d+)\s*\]")) { long lower = long.Parse(array.Groups["lower"].Value, System.Globalization.CultureInfo.InvariantCulture), upper = long.Parse(array.Groups["upper"].Value, System.Globalization.CultureInfo.InvariantCulture); if (upper < lower) errors.Add($"Global DB member '{name}' has descending array bounds {lower}..{upper}."); else if (upper - lower > 1000000) errors.Add($"Global DB member '{name}' declares an implausibly large array; review the bounds explicitly."); }
            Match lengthMatch = Regex.Match(type, @"(?i)^(?<kind>W?String)\s*\[\s*(?<length>\d+)\s*\]$"); if (lengthMatch.Success) { int length = int.Parse(lengthMatch.Groups["length"].Value, System.Globalization.CultureInfo.InvariantCulture); int maximum = lengthMatch.Groups["kind"].Value.Equals("WString", StringComparison.OrdinalIgnoreCase) ? 16382 : 254; if (length < 1 || length > maximum) errors.Add($"Global DB member '{name}' has invalid {lengthMatch.Groups["kind"].Value} length {length}; expected 1..{maximum}."); }
            if (!Regex.IsMatch(type, @"(?i)^(Bool|Byte|Word|DWord|LWord|SInt|USInt|Int|UInt|DInt|UDInt|LInt|ULInt|Real|LReal|Time|LTime|Date|Time_Of_Day|TOD|String|WString|Char|WChar|Array|Struct)\b")) warnings.Add($"Global DB member '{name}' references project type '{type}'. Confirm that the PLC data type exists before apply.");
        }
        if (declarations.Count == 0) warnings.Add("Global DB declares no members.");
        string initialization = text.Substring(variableSection.Index + variableSection.Length); var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match assignment in Regex.Matches(initialization, @"(?im)^\s*(?:""(?<quoted>[^""]+)""|(?<plain>[A-Za-z_][A-Za-z0-9_]*))\s*:=\s*(?<value>[^;]+);")) { string name = FirstNonEmpty(assignment.Groups["quoted"].Value, assignment.Groups["plain"].Value)!; if (!assigned.Add(name)) errors.Add($"Global DB member '{name}' has more than one start-value assignment."); if (!declarations.TryGetValue(name, out string? type)) errors.Add($"Start-value assignment targets undeclared global DB member '{name}'."); else if (type.Trim().Equals("Bool", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(assignment.Groups["value"].Value.Trim(), @"^(TRUE|FALSE)$", RegexOptions.IgnoreCase)) errors.Add($"Bool member '{name}' requires TRUE or FALSE start value."); }
        if (!Regex.IsMatch(text, @"(?i)S7_Optimized\s*:=")) warnings.Add("Global DB does not declare S7_Optimized explicitly; preserve the exported access mode before applying a modification.");
        if (Regex.IsMatch(text, @"(?i)S7_Optimized\s*:=\s*""FALSE""")) warnings.Add("Non-optimized DB layout is address-sensitive; run cross-reference and HMI impact checks before changing members or order.");
        if (Regex.IsMatch(text, @"(?i)InstanceDB|InstanceOf")) errors.Add("Instance DB documents are not supported by the global DB authoring workflow.");
    }

    private static bool HasSourceTypeTemplateBeforeCall(string text, int callIndex, bool requireDestinationType) { int start = text.LastIndexOf("NETWORK", callIndex, StringComparison.OrdinalIgnoreCase); string prefix = text.Substring(Math.Max(0, start), callIndex - Math.Max(0, start)); int index = prefix.LastIndexOf("S7_Templates", StringComparison.OrdinalIgnoreCase); if (index < 0 || index < prefix.LastIndexOf(')')) return false; string nearest = prefix.Substring(index); return nearest.IndexOf("SrcType", StringComparison.OrdinalIgnoreCase) >= 0 && (!requireDestinationType || nearest.IndexOf("DestType", StringComparison.OrdinalIgnoreCase) >= 0); }
    private static bool IsSclNetworkCall(string text, int callIndex) { int previousEnd = text.LastIndexOf("END_NETWORK", callIndex, StringComparison.OrdinalIgnoreCase); int start = previousEnd < 0 ? 0 : previousEnd + "END_NETWORK".Length; return Regex.IsMatch(text.Substring(start, callIndex - start), @"S7_Language\s*:=\s*""SCL""", RegexOptions.IgnoreCase); }
    private static string DecodeUtf8(byte[] bytes, List<string> errors, string label) { if (bytes.Length == 0) return string.Empty; try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); } catch (DecoderFallbackException ex) { errors.Add($"{label} is not valid UTF-8: {ex.Message}"); return string.Empty; } }
    private static string? Capture(Regex regex, string text) { Match match = regex.Match(text); return match.Success ? match.Groups["value"].Value : null; }
    private static string? FirstNonEmpty(string first, string second) => !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;
    private static string NormalizeBlockType(string value) => value.ToUpperInvariant() == "FUNCTION" ? "FC" : value.ToUpperInvariant() == "FUNCTION_BLOCK" ? "FB" : value.ToUpperInvariant() == "DATA_BLOCK" ? "GlobalDB" : "OB";
    private static int LineOf(string text, int index) => 1 + text.Take(index).Count(character => character == '\n');
    private static string ExtractParenthesized(string text, int openIndex) { int depth = 0; for (int index = openIndex; index < text.Length; index++) { if (text[index] == '(') depth++; else if (text[index] == ')' && --depth == 0) return text.Substring(openIndex + 1, index - openIndex - 1); } return text.Substring(Math.Min(openIndex + 1, text.Length)); }
    private static bool IsDeclaredInStaticSection(string text, string instance) => Regex.Matches(text, @"(?is)\bVAR\b(?<body>.*?)\bEND_VAR\b").Cast<Match>().Any(section => Regex.IsMatch(section.Groups["body"].Value, @"(?im)^\s*" + Regex.Escape(instance) + @"\s*:"));
    private static bool IsDeclaredInLocalOrStaticSection(string text, string name, string type) => Regex.Matches(text, @"(?is)\b(?:VAR|VAR_TEMP)\b(?<body>.*?)\bEND_VAR\b").Cast<Match>().Any(section => Regex.IsMatch(section.Groups["body"].Value, @"(?im)(?:^|;)\s*" + Regex.Escape(name) + @"\s*:\s*" + Regex.Escape(type) + @"\s*;"));
    private static bool IsDeclaredInRetainedEdgeSection(string text, string name) => Regex.Matches(text, @"(?is)\b(?:VAR|VAR_IN_OUT)\b(?<body>.*?)\bEND_VAR\b").Cast<Match>().Any(section => Regex.IsMatch(section.Groups["body"].Value, @"(?im)(?:^|;)\s*" + Regex.Escape(name) + @"\s*:\s*Bool\s*;"));
}
