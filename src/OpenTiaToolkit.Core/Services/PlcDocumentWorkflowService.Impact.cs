using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed partial class PlcDocumentWorkflowService
{
    private static readonly string[] ImpactStatuses = { "Safe", "ReviewRequired", "Unknown", "Blocked" };

    public PlcCodeImpactAnalysis GetTransactionImpact(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        return transaction.ImpactAnalysis ?? throw new InvalidOperationException("The transaction has not been planned, so no impact analysis exists.");
    }

    public async Task<PlcCodeTransaction> ApproveTransactionImpactAsync(string transactionId, string impactFingerprint, bool acceptUnknown)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "ImpactReviewRequired");
        if (transaction.ImpactAnalysis == null) throw new InvalidOperationException("The transaction has no impact analysis to approve.");
        if (!FixedEquals(transaction.ImpactAnalysis.Fingerprint, impactFingerprint ?? string.Empty))
            throw new InvalidOperationException("The impact fingerprint does not match the current transaction analysis.");
        if (transaction.ImpactAnalysis.Status == "Blocked") throw new InvalidOperationException("Blocked impact findings cannot be approved.");
        if (transaction.ImpactAnalysis.Status == "Unknown" && !acceptUnknown)
            throw new InvalidOperationException("The impact analysis contains unknown evidence. Set accept-unknown=true only after reviewing the unresolved scope.");

        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        await VerifyCapabilityCurrentAsync(transaction).ConfigureAwait(false);
        VerifyTransactionPreconditions(transaction);
        PlcCodeImpactAnalysis current = await AnalyzeTransactionImpactAsync(transaction).ConfigureAwait(false);
        if (!FixedEquals(transaction.ImpactAnalysis.Fingerprint, current.Fingerprint))
        {
            transaction.State = "Stale";
            transaction.Failure = "PLC dependencies, consumers, or semantic impact changed after planning.";
            AddJournal(transaction, null, "Impact", "Stale", transaction.Failure);
            SaveTransaction(transaction);
            throw new InvalidOperationException(transaction.Failure);
        }

        transaction.ImpactAcknowledged = true;
        transaction.ImpactAcknowledgementFingerprint = current.Fingerprint;
        transaction.ImpactAnalysis = current;
        transaction.State = "Planned";
        transaction.ApplyToken = RandomToken();
        transaction.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(TransactionLifetime);
        transaction.PlanHash = ComputeTransactionPlanHash(transaction);
        AddJournal(transaction, null, "Impact", "Acknowledged", $"Impact {current.Status} was explicitly acknowledged with fingerprint {current.Fingerprint}.");
        SaveTransaction(transaction);
        return transaction;
    }

    private async Task<PlcCodeImpactAnalysis> AnalyzeTransactionImpactAsync(PlcCodeTransaction transaction)
    {
        var targets = new List<PlcCodeImpactTarget>();
        var dependencies = new List<PlcCodeImpactDependency>();
        var warnings = new List<string>();
        var producedNames = new HashSet<string>(transaction.Operations.SelectMany(OperationTargets).Select(target => target.Name), StringComparer.OrdinalIgnoreCase);

        foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.Sequence))
        {
            foreach (string dependencyName in operation.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Where(name => !producedNames.Contains(name)))
                dependencies.Add(ResolveImpactDependency(operation.Sequence, dependencyName));

            IReadOnlyList<PlcCodeTransactionTargetSnapshot> snapshots = EffectiveSnapshots(operation);
            for (int index = 0; index < snapshots.Count; index++)
                targets.Add(await AnalyzeImpactTargetAsync(transaction, operation, snapshots[index], index + 1).ConfigureAwait(false));
        }

        if (targets.Any(target => IsDataLayoutTarget(target.Target) && target.ChangeKind != "Create" && target.ChangeKind != "BodyOnly"))
            warnings.Add("DB/type layout analysis cannot prove HMI, recipe, OPC UA, communication, or absolute-address consumers; explicit review is required.");
        if (dependencies.Any(dependency => dependency.Status == "Unknown"))
            warnings.Add("At least one project or library dependency could not be resolved uniquely in the PLC block tree.");

        string status = AggregateImpactStatus(targets.Select(target => target.Status).Concat(dependencies.Select(dependency => dependency.Status)));
        var analysis = new PlcCodeImpactAnalysis
        {
            Status = status,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            Targets = targets,
            Dependencies = dependencies,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
        analysis.Fingerprint = ComputeImpactFingerprint(analysis);
        return analysis;
    }

    private PlcCodeImpactDependency ResolveImpactDependency(int operationSequence, string name)
    {
        IReadOnlyList<ResolvedBlock> matches = FindBlocks(RequireBlockRoot(), name);
        if (matches.Count == 1)
            return new PlcCodeImpactDependency
            {
                OperationSequence = operationSequence,
                Name = name,
                Status = "Safe",
                ResolvedTarget = matches[0].Identity,
                Message = $"Resolved to exact PLC block '{matches[0].Identity}'."
            };
        return new PlcCodeImpactDependency
        {
            OperationSequence = operationSequence,
            Name = name,
            Status = "Unknown",
            Message = matches.Count == 0
                ? "No matching PLC block was observed; this may be a library instruction and requires explicit review plus Siemens compile verification."
                : $"The dependency is ambiguous across {matches.Count} PLC blocks."
        };
    }

    private async Task<PlcCodeImpactTarget> AnalyzeImpactTargetAsync(PlcCodeTransaction transaction, PlcCodeTransactionOperation operation, PlcCodeTransactionTargetSnapshot snapshot, int targetIndex)
    {
        var reasons = new List<string>();
        string? baselineSignature = null;
        string? candidateSignature = null;
        string changeKind;
        string status;

        if (snapshot.Action == "Create")
        {
            changeKind = "Create";
            status = "Safe";
            reasons.Add("The exact target is new, so no existing PLC interface or layout is being replaced.");
        }
        else if (snapshot.Action == "Delete")
        {
            changeKind = "Delete";
            status = "ReviewRequired";
            reasons.Add("Deletion removes a project identity and requires complete consumer review.");
        }
        else
        {
            baselineSignature = ExtractImpactSignature(BaselineArtifactPath(transaction, operation, snapshot, targetIndex), snapshot.Target);
            candidateSignature = ExtractImpactSignature(CandidateArtifactPath(operation), snapshot.Target);
            string? candidateNumber = ExtractCandidateNumber(CandidateArtifactPath(operation), snapshot.Target);
            bool numberChanged = !string.IsNullOrWhiteSpace(candidateNumber) && !string.IsNullOrWhiteSpace(snapshot.Target.Number) &&
                !string.Equals(candidateNumber, snapshot.Target.Number, StringComparison.OrdinalIgnoreCase);
            if (numberChanged)
            {
                changeKind = "IdentityChange";
                status = "ReviewRequired";
                reasons.Add($"The declared block number changes from {snapshot.Target.Number} to {candidateNumber}.");
            }
            else if (baselineSignature == null || candidateSignature == null)
            {
                changeKind = "Unknown";
                status = "Unknown";
                reasons.Add("The baseline and candidate interface/layout signatures could not both be extracted.");
            }
            else if (FixedEquals(baselineSignature, candidateSignature))
            {
                changeKind = "BodyOnly";
                status = "Safe";
                reasons.Add("The normalized external interface and persistent layout signature is unchanged.");
            }
            else
            {
                changeKind = IsDataLayoutTarget(snapshot.Target) ? "LayoutChange" : "InterfaceChange";
                status = "ReviewRequired";
                reasons.Add(IsDataLayoutTarget(snapshot.Target)
                    ? "The normalized DB/type persistent layout signature changed."
                    : "The normalized block interface or FB persistent layout signature changed.");
            }
        }

        var consumers = Array.Empty<PlcCodeImpactConsumer>();
        bool lookupSucceeded = false;
        string lookupMessage = "Consumer lookup was not required for a new or body-only target.";
        string consumerFingerprint = Hash(Encoding.UTF8.GetBytes("not-required"));
        int excludedSelfReferences = 0;
        if (changeKind != "Create" && changeKind != "BodyOnly")
        {
            try
            {
                PlcCrossReferenceResult? result = await _plcService.GetCrossReferencesAsync(snapshot.Target.Name).ConfigureAwait(false);
                if (result == null)
                {
                    lookupMessage = "PLC cross-reference lookup returned no result.";
                    if (status != "Blocked") status = "Unknown";
                }
                else
                {
                    lookupSucceeded = result.Success;
                    lookupMessage = result.Message;
                    PlcCodeImpactConsumer[] observedConsumers = result.Entries.Select(entry => new PlcCodeImpactConsumer
                    {
                        Name = entry.Name,
                        Path = entry.Path,
                        Usage = entry.Usage,
                        ItemType = entry.ItemType,
                        Detail = entry.Detail
                    }).OrderBy(item => item.Path, StringComparer.Ordinal)
                        .ThenBy(item => item.Name, StringComparer.Ordinal)
                        .ThenBy(item => item.Usage, StringComparer.Ordinal)
                        .ThenBy(item => item.ItemType, StringComparer.Ordinal)
                        .ThenBy(item => item.Detail, StringComparer.Ordinal)
                        .ToArray();
                    excludedSelfReferences = observedConsumers.Count(item => IsSelfReference(item, snapshot.Target));
                    consumers = observedConsumers.Where(item => !IsSelfReference(item, snapshot.Target)).ToArray();
                    consumerFingerprint = ComputeConsumerFingerprint(result.Success, excludedSelfReferences, consumers);
                    if (!result.Success && status != "Blocked") status = "Unknown";
                    else if (changeKind == "Delete" && consumers.Length > 0)
                    {
                        status = "Blocked";
                        reasons.Add($"Deletion is blocked because {consumers.Length} PLC cross-reference(s) still consume the target.");
                    }
                    else if (consumers.Length > 0)
                        reasons.Add($"Observed {consumers.Length} PLC consumer cross-reference(s) requiring review.");
                    else
                        reasons.Add("No PLC consumers were returned, but non-PLC consumers are not proven absent.");
                    if (excludedSelfReferences > 0) reasons.Add($"Excluded {excludedSelfReferences} self/interface cross-reference row(s) from the external consumer set.");
                }
            }
            catch (Exception ex)
            {
                lookupMessage = Message(ex);
                consumerFingerprint = Hash(Encoding.UTF8.GetBytes("failure|" + lookupMessage));
                if (status != "Blocked") status = "Unknown";
            }
        }

        return new PlcCodeImpactTarget
        {
            OperationSequence = operation.Sequence,
            Target = snapshot.Target,
            ChangeKind = changeKind,
            Status = status,
            BaselineSignature = baselineSignature,
            CandidateSignature = candidateSignature,
            PlcConsumerLookupSucceeded = lookupSucceeded,
            PlcConsumerLookupMessage = lookupMessage,
            ConsumerFingerprint = consumerFingerprint,
            ExcludedSelfReferenceCount = excludedSelfReferences,
            Consumers = consumers,
            Reasons = reasons.ToArray()
        };
    }

    private static string CandidateArtifactPath(PlcCodeTransactionOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.SourcePath)) return operation.SourcePath!;
        return Path.Combine(operation.ImportDirectory, operation.FileName + ".s7dcl");
    }

    private static string? BaselineArtifactPath(PlcCodeTransaction transaction, PlcCodeTransactionOperation operation, PlcCodeTransactionTargetSnapshot snapshot, int targetIndex)
    {
        if (snapshot.BaselineHash == "missing") return null;
        if (operation.Kind == "PatchSimaticMlBlock" || operation.Kind == "PatchSimaticMlType") return operation.BaselinePath;
        if (operation.Kind == "GenerateSclSource")
            return IsTypeTarget(snapshot.Target)
                ? TransactionSclTypeBaseline(transaction.TransactionId, operation.Sequence, targetIndex)
                : Path.Combine(TransactionSclTargetBaseline(transaction.TransactionId, operation.Sequence, targetIndex), snapshot.Target.Name + ".s7dcl");
        return Path.Combine(TransactionOperationBaseline(transaction.TransactionId, operation.Sequence), snapshot.Target.Name + ".s7dcl");
    }

    private static string? ExtractImpactSignature(string? path, PlcBlockIdentity target)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            IReadOnlyList<string> tokens = extension == ".xml" ? ExtractXmlSignatureTokens(path!, target) : ExtractTextSignatureTokens(File.ReadAllText(path!), target);
            return tokens.Count == 0 ? Hash(Encoding.UTF8.GetBytes("empty|" + target.BlockType.ToUpperInvariant())) : Hash(Encoding.UTF8.GetBytes(string.Join("|", tokens)));
        }
        catch { return null; }
    }

    private static IReadOnlyList<string> ExtractTextSignatureTokens(string text, PlcBlockIdentity target)
    {
        string segment = ExtractTextObjectSegment(text.TrimStart('\uFEFF'), target);
        segment = Regex.Replace(segment, @"(?s)\(\*.*?\*\)|//.*?$", string.Empty, RegexOptions.Multiline);
        var tokens = new List<string>();
        MatchCollection sections = Regex.Matches(segment, @"(?is)\b(?<section>VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|VAR_TEMP|VAR_STAT|VAR|CONST)\b(?<body>.*?)\bEND_VAR\b");
        foreach (Match section in sections)
        {
            string sectionName = NormalizeSectionName(section.Groups["section"].Value);
            if (!IncludeSection(target.BlockType, sectionName)) continue;
            AddTextDeclarationTokens(tokens, sectionName, section.Groups["body"].Value);
        }
        foreach (Match attribute in Regex.Matches(segment, "(?i)\\b(?<name>S7_[A-Za-z0-9_]*(?:Optimized|Retain|Accessible|Visible|SetPoint)[A-Za-z0-9_]*)\\s*:=\\s*\"?(?<value>[^\";\\s}]+)"))
            tokens.Add("ATTRIBUTE:" + NormalizeImpactAttributeName(attribute.Groups["name"].Value) + ":" + NormalizeSignatureValue(attribute.Groups["value"].Value));
        if (tokens.Count == 0 && IsDataLayoutTarget(target))
        {
            Match structure = Regex.Match(segment, @"(?is)\bSTRUCT\b(?<body>.*?)\bEND_STRUCT\b");
            if (structure.Success) AddTextDeclarationTokens(tokens, "LAYOUT", structure.Groups["body"].Value);
        }
        return tokens.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static void AddTextDeclarationTokens(List<string> tokens, string section, string body)
    {
        foreach (string statement in body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = Regex.Replace(statement, @"\{[^{}]*\}", string.Empty).Trim();
            int colon = normalized.IndexOf(':');
            if (colon <= 0) continue;
            string names = normalized.Substring(0, colon).Trim();
            string type = normalized.Substring(colon + 1);
            int initialization = type.IndexOf(":=", StringComparison.Ordinal);
            if (initialization >= 0) type = type.Substring(0, initialization);
            type = NormalizeSignatureValue(type);
            foreach (string name in names.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                tokens.Add(section + ":" + NormalizeSignatureValue(name) + ":" + type);
        }
    }

    private static IReadOnlyList<string> ExtractXmlSignatureTokens(string path, PlcBlockIdentity target)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader);
        XElement? targetElement = document.Descendants().FirstOrDefault(element =>
            (Regex.IsMatch(element.Name.LocalName, @"\.(FC|FB|OB|GlobalDB|InstanceDB)$") || element.Name.LocalName == "SW.Types.PlcStruct") &&
            string.Equals(element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value, target.Name, StringComparison.OrdinalIgnoreCase));
        if (targetElement == null) return Array.Empty<string>();
        var tokens = new List<string>();
        foreach (XElement member in targetElement.Descendants().Where(element => element.Name.LocalName == "Member"))
        {
            string section = member.Ancestors().FirstOrDefault(element => element.Name.LocalName == "Section")?.Attribute("Name")?.Value ?? "LAYOUT";
            section = NormalizeSectionName(section);
            if (!IncludeSection(target.BlockType, section)) continue;
            string name = member.Attribute("Name")?.Value ?? member.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value ?? string.Empty;
            string type = member.Attribute("Datatype")?.Value ?? member.Attribute("DataType")?.Value ?? member.Elements().FirstOrDefault(element => element.Name.LocalName == "Datatype" || element.Name.LocalName == "DataType")?.Value ?? string.Empty;
            string parentPath = string.Join(".", member.Ancestors().Where(element => element.Name.LocalName == "Member").Reverse().Select(element => element.Attribute("Name")?.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
            string qualifiedName = string.IsNullOrEmpty(parentPath) ? name : parentPath + "." + name;
            tokens.Add(section + ":" + NormalizeSignatureValue(qualifiedName) + ":" + NormalizeSignatureValue(type));
        }
        foreach (XElement element in targetElement.DescendantsAndSelf())
        {
            string? namedAttribute = element.Attribute("Name")?.Value;
            if (IsImpactAttributeName(namedAttribute))
            {
                string value = element.Attribute("Value")?.Value ?? element.Value;
                tokens.Add("ATTRIBUTE:" + NormalizeImpactAttributeName(namedAttribute!) + ":" + NormalizeSignatureValue(value));
            }
            foreach (XAttribute attribute in element.Attributes().Where(attribute => IsImpactAttributeName(attribute.Name.LocalName)))
                tokens.Add("ATTRIBUTE:" + NormalizeImpactAttributeName(attribute.Name.LocalName) + ":" + NormalizeSignatureValue(attribute.Value));
        }
        return tokens.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string ExtractTextObjectSegment(string text, PlcBlockIdentity target)
    {
        string keyword = target.BlockType.Equals("FC", StringComparison.OrdinalIgnoreCase) ? "FUNCTION" :
            target.BlockType.Equals("FB", StringComparison.OrdinalIgnoreCase) ? "FUNCTION_BLOCK" :
            target.BlockType.Equals("OB", StringComparison.OrdinalIgnoreCase) ? "ORGANIZATION_BLOCK" :
            target.BlockType.Equals("TYPE", StringComparison.OrdinalIgnoreCase) ? "TYPE" : "DATA_BLOCK";
        string end = keyword == "FUNCTION" ? "END_FUNCTION" : keyword == "FUNCTION_BLOCK" ? "END_FUNCTION_BLOCK" : keyword == "ORGANIZATION_BLOCK" ? "END_ORGANIZATION_BLOCK" : keyword == "TYPE" ? "END_TYPE" : "END_DATA_BLOCK";
        Match match = Regex.Match(text, "(?is)(?:\\{[^{}]*\\}\\s*)?\\b" + keyword + "\\s+(?:\"" + Regex.Escape(target.Name) + "\"|" + Regex.Escape(target.Name) + "\\b)(?<body>.*?)\\b" + end + "\\b");
        return match.Success ? match.Value : text;
    }

    private static string? ExtractCandidateNumber(string path, PlcBlockIdentity target)
    {
        if (!File.Exists(path)) return null;
        string text = File.ReadAllText(path);
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            Match xmlNumber = Regex.Match(text, @"<Number>(?<value>\d+)</Number>", RegexOptions.IgnoreCase);
            return xmlNumber.Success ? xmlNumber.Groups["value"].Value : null;
        }
        Match attribute = Regex.Match(ExtractTextObjectSegment(text, target), "S7_BlockNumber\\s*:=\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
        return attribute.Success ? attribute.Groups["value"].Value : null;
    }

    private static bool IncludeSection(string blockType, string section)
    {
        if (IsDataLayoutType(blockType)) return section != "TEMP" && section != "CONSTANT";
        if (blockType.Equals("FB", StringComparison.OrdinalIgnoreCase)) return section == "INPUT" || section == "OUTPUT" || section == "INOUT" || section == "STATIC";
        return section == "INPUT" || section == "OUTPUT" || section == "INOUT";
    }

    private static string NormalizeSectionName(string value)
    {
        string normalized = value.Replace("VAR_", string.Empty).Replace("VAR", "STATIC").Replace("IN_OUT", "INOUT").Trim().ToUpperInvariant();
        return normalized == "STAT" ? "STATIC" : normalized;
    }

    private static string NormalizeSignatureValue(string value) => Regex.Replace(value ?? string.Empty, "[\\s\"']+", string.Empty).ToUpperInvariant();
    private static bool IsImpactAttributeName(string? value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value!, "Optimized|Retain|Accessible|Visible|SetPoint", RegexOptions.IgnoreCase);
    private static string NormalizeImpactAttributeName(string value) => NormalizeSignatureValue(value).Replace("S7_", string.Empty).Replace("_", string.Empty);
    private static bool IsDataLayoutTarget(PlcBlockIdentity target) => IsDataLayoutType(target.BlockType);
    private static bool IsDataLayoutType(string blockType) => blockType.Equals("TYPE", StringComparison.OrdinalIgnoreCase) || blockType.EndsWith("DB", StringComparison.OrdinalIgnoreCase) || blockType.Equals("DB", StringComparison.OrdinalIgnoreCase);

    private static string AggregateImpactStatus(IEnumerable<string> statuses)
    {
        string[] values = statuses.ToArray();
        for (int index = ImpactStatuses.Length - 1; index >= 0; index--)
            if (values.Contains(ImpactStatuses[index], StringComparer.Ordinal)) return ImpactStatuses[index];
        return "Unknown";
    }

    private static bool IsSelfReference(PlcCodeImpactConsumer consumer, PlcBlockIdentity target)
    {
        string path = (consumer.Path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        string finalSegment = path.Substring(path.LastIndexOf('/') + 1);
        if (!finalSegment.Equals(target.Name, StringComparison.OrdinalIgnoreCase)) return false;
        return string.IsNullOrWhiteSpace(consumer.Detail) || consumer.Detail!.IndexOf("source=" + target.Name, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ComputeConsumerFingerprint(bool succeeded, int excludedSelfReferences, IReadOnlyList<PlcCodeImpactConsumer> consumers)
    {
        string canonical = JsonSerializer.Serialize(new { succeeded, excludedSelfReferences, consumers });
        return Hash(Encoding.UTF8.GetBytes(canonical));
    }

    private static string ComputeImpactFingerprint(PlcCodeImpactAnalysis analysis)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            analysis.SchemaVersion,
            analysis.Status,
            Targets = analysis.Targets.OrderBy(target => target.OperationSequence).ThenBy(target => target.Target.ToString(), StringComparer.Ordinal).Select(target => new
            {
                target.OperationSequence,
                target.Target,
                target.ChangeKind,
                target.Status,
                target.BaselineSignature,
                target.CandidateSignature,
                target.PlcConsumerLookupSucceeded,
                target.ConsumerFingerprint,
                target.ExcludedSelfReferenceCount,
                target.Consumers,
                target.Reasons
            }),
            Dependencies = analysis.Dependencies.OrderBy(dependency => dependency.OperationSequence).ThenBy(dependency => dependency.Name, StringComparer.Ordinal).Select(dependency => new
            {
                dependency.OperationSequence,
                dependency.Name,
                dependency.Status,
                dependency.ResolvedTarget
            }),
            analysis.Warnings
        });
        return Hash(Encoding.UTF8.GetBytes(canonical));
    }
}
