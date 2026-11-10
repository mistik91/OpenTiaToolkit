using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services;

internal sealed class HmiDiscreteAlarmService
{
    private const string SchemaVersion = "1.0";
    private readonly IHmiDiscreteAlarmAccess _access;

    internal HmiDiscreteAlarmService(IHmiDiscreteAlarmAccess access) =>
        _access = access ?? throw new ArgumentNullException(nameof(access));

    public Task<IReadOnlyList<HmiDiscreteAlarmSnapshot>> ListAsync(bool includeValidation = false) => Task.Run(() =>
        (IReadOnlyList<HmiDiscreteAlarmSnapshot>)_access.List(includeValidation).Select(WithHash).ToArray());

    public Task<HmiDiscreteAlarmSnapshot?> GetAsync(string name, bool includeValidation = true) => Task.Run(() =>
    {
        var snapshot = _access.FindExact(name, includeValidation);
        return snapshot == null ? null : WithHash(snapshot);
    });

    public Task<HmiDiscreteAlarmApplyResult> ApplyAsync(string inputPath, bool dryRun, bool failOnWarning) => Task.Run(() =>
    {
        HmiDiscreteAlarmDocument document;
        try { document = ParseDocument(inputPath); }
        catch (Exception ex) { return BlockedApply(dryRun, "invalid-input", ex.Message); }

        var preflightErrors = ValidateDocument(document);
        if (preflightErrors.Count > 0) return BlockedApply(dryRun, "invalid-input", string.Join(" ", preflightErrors));
        if (!_access.IsAvailable) return BlockedApply(dryRun, "hmi-not-open", "No Unified HMI is currently open.");

        try
        {
            if (_access.IsReadOnly) return BlockedApply(dryRun, "discrete-alarms-read-only", "The Unified HMI discrete-alarm composition is read-only.");
            var beforeAll = _access.List(includeValidation: false).Select(WithHash).ToDictionary(x => x.Name, StringComparer.Ordinal);
            var items = BuildInitialPlans(document, beforeAll, failOnWarning);
            if (items.Any(x => x.Errors.Count > 0) || (failOnWarning && items.Any(x => x.Warnings.Count > 0)))
                return BlockedApply(dryRun, "preflight-blocked", "Discrete-alarm preflight was blocked.", items);

            var transaction = new HmiDiscreteAlarmTransactionInfo { Created = true };
            try
            {
                _access.ExecuteTransaction(
                    dryRun ? "OpenTiaToolkit Unified discrete-alarm dry-run" : "OpenTiaToolkit Unified discrete-alarm apply",
                    commit: !dryRun,
                    action: () => ApplyInsideTransaction(document, items, failOnWarning));
                transaction.Committed = !dryRun;
                transaction.RolledBack = dryRun;
            }
            catch (DiscreteAlarmSafetyException ex)
            {
                transaction.RolledBack = true;
                return BlockedApply(dryRun, ex.Code, ex.Message, items, transaction);
            }
            catch (Exception ex)
            {
                transaction.RolledBack = true;
                return ExceptionApply(dryRun, ex, items, transaction);
            }

            if (dryRun)
            {
                var fresh = _access.List(includeValidation: false).Select(WithHash).ToDictionary(x => x.Name, StringComparer.Ordinal);
                if (!SameInventory(beforeAll, fresh))
                    return BlockedApply(true, "dry-run-rollback-verification-failed", "Dry-run rollback verification detected a persistent change.", items, transaction);
            }
            else
            {
                var fresh = _access.List(includeValidation: true).Select(WithHash).ToDictionary(x => x.Name, StringComparer.Ordinal);
                foreach (var item in items)
                    if (fresh.TryGetValue(item.Name, out var persisted)) item.Persisted = persisted;
            }

            return new HmiDiscreteAlarmApplyResult
            {
                DryRun = dryRun,
                Success = true,
                Message = dryRun
                    ? "Discrete-alarm dry-run completed and the transaction was rolled back."
                    : "Discrete alarms were applied atomically. The open TIA project now contains unsaved changes.",
                ProjectHasUnsavedChanges = !dryRun,
                Alarms = items,
                Transaction = transaction
            };
        }
        catch (Exception ex) { return ExceptionApply(dryRun, ex, Array.Empty<HmiDiscreteAlarmPlanItem>(), new HmiDiscreteAlarmTransactionInfo()); }
    });

    public Task<HmiDiscreteAlarmDeleteResult> DeleteAsync(
        string name,
        string expectedHash,
        bool dryRun,
        bool confirm,
        bool allowReferencedDelete) => Task.Run(() =>
    {
        if (!_access.IsAvailable) return BlockedDelete(dryRun, "hmi-not-open", "No Unified HMI is currently open.");
        try
        {
            if (_access.IsReadOnly) return BlockedDelete(dryRun, "discrete-alarms-read-only", "The Unified HMI discrete-alarm composition is read-only.");
            var before = _access.FindExact(name, includeValidation: true);
            if (before == null) return BlockedDelete(dryRun, "alarm-not-found", $"Discrete alarm '{name}' was not found.");
            before = WithHash(before);
            if (string.IsNullOrWhiteSpace(expectedHash) || !FixedEquals(before.ContentHash, expectedHash))
                return BlockedDelete(dryRun, "stale-expected-hash", "expected-hash is required and must match the current canonical alarm hash.", before);
            var validationErrors = before.Validation.SelectMany(x => x.Errors).ToArray();
            if (validationErrors.Length > 0)
                return BlockedDelete(dryRun, "siemens-validation-blocked", "The alarm has Siemens validation errors and deletion was blocked.", before);

            var references = _access.GetCrossReferences(name, out var limitation);
            if (!dryRun && !string.IsNullOrWhiteSpace(limitation))
                return BlockedDelete(false, "cross-reference-unavailable", limitation!, before, references, limitation);
            if (references.Count > 0 && !allowReferencedDelete)
                return BlockedDelete(dryRun, "alarm-is-referenced", "Cross references exist; set allow-referenced-delete=true only after reviewing them.", before, references, limitation);
            if (!dryRun && !confirm)
                return BlockedDelete(false, "confirmation-required", "Deletion requires confirm=true.", before, references, limitation);

            var transaction = new HmiDiscreteAlarmTransactionInfo { Created = true };
            try
            {
                _access.ExecuteTransaction(
                    dryRun ? "OpenTiaToolkit Unified discrete-alarm delete dry-run" : "OpenTiaToolkit Unified discrete-alarm delete",
                    commit: !dryRun,
                    action: () => _access.DeleteExact(name));
                transaction.Committed = !dryRun;
                transaction.RolledBack = dryRun;
            }
            catch (Exception ex)
            {
                transaction.RolledBack = true;
                var mapped = MapException(ex);
                return new HmiDiscreteAlarmDeleteResult { DryRun = dryRun, SafetyBlocked = true, ErrorCode = mapped.Code, Message = mapped.Message, Before = before, CrossReferences = references, CrossReferenceLimitation = limitation, Transaction = transaction };
            }

            var fresh = _access.FindExact(name, includeValidation: false);
            if ((dryRun && (fresh == null || !FixedEquals(Hash(fresh), before.ContentHash))) || (!dryRun && fresh != null))
                return BlockedDelete(dryRun, "delete-verification-failed", "Fresh-read verification did not observe the expected post-transaction state.", before, references, limitation, transaction);

            return new HmiDiscreteAlarmDeleteResult
            {
                DryRun = dryRun,
                Success = true,
                Deleted = !dryRun,
                Message = dryRun ? "Delete dry-run completed and was rolled back." : "The exact discrete alarm was deleted. The open TIA project now contains unsaved changes.",
                Before = before,
                CrossReferences = references,
                CrossReferenceLimitation = limitation,
                Transaction = transaction
            };
        }
        catch (Exception ex)
        {
            var mapped = MapException(ex);
            return BlockedDelete(dryRun, mapped.Code, mapped.Message);
        }
    });

    internal static HmiDiscreteAlarmSnapshot WithHash(HmiDiscreteAlarmSnapshot snapshot)
    {
        snapshot.ContentHash = Hash(snapshot);
        return snapshot;
    }

    internal static string Hash(HmiDiscreteAlarmSnapshot snapshot)
    {
        var canonical = new
        {
            snapshot.Name,
            Id = snapshot.Id.ToString(CultureInfo.InvariantCulture),
            snapshot.AlarmClass,
            Priority = snapshot.Priority.ToString(CultureInfo.InvariantCulture),
            snapshot.Origin,
            snapshot.Area,
            snapshot.AuditClass,
            snapshot.RaisedStateTag,
            RaisedStateTagBitNumber = snapshot.RaisedStateTagBitNumber.ToString(CultureInfo.InvariantCulture),
            TriggerMode = snapshot.TriggerMode,
            snapshot.TriggerBitAddress,
            snapshot.AcknowledgmentStateTag,
            AcknowledgmentStateTagBitNumber = snapshot.AcknowledgmentStateTagBitNumber.ToString(CultureInfo.InvariantCulture),
            snapshot.AcknowledgmentControlTag,
            AcknowledgmentControlTagBitNumber = snapshot.AcknowledgmentControlTagBitNumber.ToString(CultureInfo.InvariantCulture),
            AlarmParameterTags = snapshot.AlarmParameterTags.ToArray(),
            Texts = snapshot.Texts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { Culture = x.Key, Value = x.Value }).ToArray()
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical));
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private void ApplyInsideTransaction(HmiDiscreteAlarmDocument document, IReadOnlyList<HmiDiscreteAlarmPlanItem> plans, bool failOnWarning)
    {
        for (var index = 0; index < document.Alarms.Count; index++)
        {
            var request = document.Alarms[index];
            var plan = plans[index];
            HmiDiscreteAlarmSnapshot after;
            try
            {
                after = plan.Before == null ? _access.CreateAndApply(request) : _access.Apply(request.Name, request);
            }
            catch (Exception ex)
            {
                var mapped = MapException(ex);
                if (mapped.Code == "unified-license-required") throw;
                plan.Status = "Blocked";
                plan.Errors = new[] { mapped.Message };
                throw new DiscreteAlarmSafetyException("setter-failed", $"A setter failed for discrete alarm '{request.Name}'; the batch was rolled back.");
            }
            after = WithHash(after);
            plan.ProposedAfter = after;
            plan.Differences = Diff(plan.Before, after);
            plan.Status = plan.Before == null ? "Created" : plan.Differences.Count == 0 ? "Unchanged" : "Updated";
            plan.Errors = after.Validation.SelectMany(x => x.Errors.Select(error => $"{x.PropertyName}: {error}".TrimStart(':', ' '))).ToArray();
            plan.Warnings = plan.Warnings.Concat(after.Validation.SelectMany(x => x.Warnings.Select(warning => $"{x.PropertyName}: {warning}".TrimStart(':', ' ')))).ToArray();
            if (plan.Errors.Count > 0 || (failOnWarning && plan.Warnings.Count > 0))
            {
                plan.Status = "Blocked";
                throw new DiscreteAlarmSafetyException("siemens-validation-blocked", "Siemens validation blocked the discrete-alarm batch.");
            }
        }
    }

    private IReadOnlyList<HmiDiscreteAlarmPlanItem> BuildInitialPlans(HmiDiscreteAlarmDocument document, IReadOnlyDictionary<string, HmiDiscreteAlarmSnapshot> current, bool failOnWarning)
    {
        var plans = new List<HmiDiscreteAlarmPlanItem>();
        // Current already contains the complete canonical ID inventory. Do
        // not enumerate the Siemens composition again for every request.
        var ids = current.Values.ToLookup(x => x.Id, x => x.Name);
        var classes = new Dictionary<string, bool>(StringComparer.Ordinal);
        var cultures = new Dictionary<string, bool>(StringComparer.Ordinal);
        var tags = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Cached(Dictionary<string, bool> cache, string name, Func<string, bool> read)
        {
            if (!cache.TryGetValue(name, out var exists)) cache[name] = exists = read(name);
            return exists;
        }
        bool TagExists(string name) => Cached(tags, name, _access.TagExists);
        foreach (var request in document.Alarms)
        {
            current.TryGetValue(request.Name, out var before);
            var errors = new List<string>();
            var warnings = new List<string>();
            if (request.ExpectedState.Equals("absent", StringComparison.OrdinalIgnoreCase) && before != null) errors.Add("expectedState=absent but the alarm already exists.");
            if (request.ExpectedState.Equals("present", StringComparison.OrdinalIgnoreCase) && before == null) errors.Add("expectedState=present but the alarm was not found.");
            if (before != null && !string.IsNullOrWhiteSpace(request.ExpectedHash) && !FixedEquals(before.ContentHash, request.ExpectedHash!)) errors.Add("expectedHash does not match the current canonical hash.");
            if (before != null && string.IsNullOrWhiteSpace(request.ExpectedHash)) warnings.Add("Updating an existing alarm without expectedHash is unsafe.");
            if (before != null && request.PresentProperties.Contains("id") && request.Id != before.Id) errors.Add("Changing the ID of an existing alarm is not supported.");
            if (request.PresentProperties.Contains("id") && request.Id.HasValue && ids[request.Id.Value].Any(name => !string.Equals(name, request.Name, StringComparison.Ordinal))) errors.Add($"Alarm ID {request.Id.Value} is already in use.");
            if (request.PresentProperties.Contains("alarmClass") && (string.IsNullOrWhiteSpace(request.AlarmClass) || !Cached(classes, request.AlarmClass!, _access.AlarmClassExists))) errors.Add($"Alarm class '{request.AlarmClass}' does not exist.");
            ValidateTag(request, "raisedStateTag", request.RaisedStateTag, requiredWhenPresent: true, errors, TagExists);
            ValidateTag(request, "acknowledgmentStateTag", request.AcknowledgmentStateTag, requiredWhenPresent: false, errors, TagExists);
            ValidateTag(request, "acknowledgmentControlTag", request.AcknowledgmentControlTag, requiredWhenPresent: false, errors, TagExists);
            if (request.AlarmParameterTags != null)
                foreach (var tag in request.AlarmParameterTags.Where(x => !TagExists(x))) errors.Add($"Alarm parameter tag '{tag}' does not exist.");
            if (request.Texts != null)
                foreach (var culture in request.Texts.Keys.Where(x => !Cached(cultures, x, _access.CultureExists))) errors.Add($"Culture '{culture}' is not configured in the project.");
            if (before == null)
            {
                if (!request.PresentProperties.Contains("id")) errors.Add("id is required when creating an alarm.");
                if (!request.PresentProperties.Contains("alarmClass")) errors.Add("alarmClass is required when creating an alarm.");
                if (!request.PresentProperties.Contains("raisedStateTag")) errors.Add("raisedStateTag is required when creating an alarm.");
                if (!request.PresentProperties.Contains("raisedStateTagBitNumber")) errors.Add("raisedStateTagBitNumber is required when creating an alarm.");
                if (!request.PresentProperties.Contains("triggerMode")) errors.Add("triggerMode is required when creating an alarm.");
            }
            plans.Add(new HmiDiscreteAlarmPlanItem { Name = request.Name, Status = errors.Count > 0 || (failOnWarning && warnings.Count > 0) ? "Blocked" : before == null ? "Created" : "Updated", Before = before, Errors = errors, Warnings = warnings });
        }
        return plans;
    }

    private static void ValidateTag(HmiDiscreteAlarmChangeRequest request, string property, string? value, bool requiredWhenPresent, ICollection<string> errors, Func<string, bool> tagExists)
    {
        if (!request.PresentProperties.Contains(property)) return;
        if (string.IsNullOrWhiteSpace(value)) { if (requiredWhenPresent) errors.Add($"{property} cannot be empty."); return; }
        if (!tagExists(value!)) errors.Add($"Tag '{value}' referenced by {property} does not exist.");
    }

    private static IReadOnlyList<string> ValidateDocument(HmiDiscreteAlarmDocument document)
    {
        var errors = new List<string>();
        if (!string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal)) errors.Add($"Unsupported schemaVersion '{document.SchemaVersion}'. Expected '{SchemaVersion}'.");
        if (document.Alarms.Count == 0) errors.Add("alarms must contain at least one request.");
        foreach (var duplicate in document.Alarms.Where(x => !string.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Name, StringComparer.Ordinal).Where(x => x.Count() > 1)) errors.Add($"Duplicate alarm name '{duplicate.Key}' in the input document.");
        foreach (var request in document.Alarms)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Every alarm requires a non-empty name.");
            if (!new[] { "absent", "present", "any" }.Contains(request.ExpectedState, StringComparer.OrdinalIgnoreCase)) errors.Add($"Invalid expectedState '{request.ExpectedState}' for '{request.Name}'.");
            if (request.AlarmParameterTags?.Count > 10) errors.Add($"Alarm '{request.Name}' has more than the Siemens maximum of 10 alarmParameterTags.");
            if (request.AlarmParameterTags?.Any(string.IsNullOrWhiteSpace) == true) errors.Add($"Alarm '{request.Name}' contains an empty alarmParameterTags entry.");
            if (request.PresentProperties.Contains("triggerMode") && !new[] { "OnRisingEdge", "OnFallingEdge" }.Contains(request.TriggerMode, StringComparer.OrdinalIgnoreCase)) errors.Add($"Invalid triggerMode '{request.TriggerMode}' for '{request.Name}'.");
        }
        foreach (var duplicateId in document.Alarms.Where(x => x.Id.HasValue).GroupBy(x => x.Id!.Value).Where(x => x.Count() > 1)) errors.Add($"Duplicate explicit alarm ID {duplicateId.Key} in the input document.");
        return errors;
    }

    private static HmiDiscreteAlarmDocument ParseDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("input-path is required.");
        if (!File.Exists(path)) throw new FileNotFoundException("Discrete-alarm input document was not found.", path);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("The input root must be a JSON object.");
        var document = new HmiDiscreteAlarmDocument { SchemaVersion = GetRequiredString(root, "schemaVersion") };
        if (!root.TryGetProperty("alarms", out var alarms) || alarms.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("alarms must be a JSON array.");
        var requests = new List<HmiDiscreteAlarmChangeRequest>();
        foreach (var element in alarms.EnumerateArray()) requests.Add(ParseRequest(element));
        document.Alarms = requests;
        return document;
    }

    private static HmiDiscreteAlarmChangeRequest ParseRequest(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Every alarms entry must be an object.");
        var request = new HmiDiscreteAlarmChangeRequest { Name = GetRequiredString(element, "name") };
        foreach (var property in element.EnumerateObject())
        {
            request.PresentProperties.Add(property.Name);
            switch (property.Name)
            {
                case "name": break;
                case "expectedState": request.ExpectedState = ReadString(property.Value, property.Name, allowNull: false)!; break;
                case "expectedHash": request.ExpectedHash = ReadString(property.Value, property.Name, allowNull: true); break;
                case "id": request.Id = ReadUInt32(property.Value, property.Name); break;
                case "alarmClass": request.AlarmClass = ReadString(property.Value, property.Name, true); break;
                case "priority": request.Priority = ReadByte(property.Value, property.Name); break;
                case "origin": request.Origin = ReadString(property.Value, property.Name, true); break;
                case "area": request.Area = ReadString(property.Value, property.Name, true); break;
                case "auditClass": request.AuditClass = ReadString(property.Value, property.Name, true); break;
                case "raisedStateTag": request.RaisedStateTag = ReadString(property.Value, property.Name, true); break;
                case "raisedStateTagBitNumber": request.RaisedStateTagBitNumber = ReadUInt32(property.Value, property.Name); break;
                case "triggerMode": request.TriggerMode = ReadString(property.Value, property.Name, true); break;
                case "acknowledgmentStateTag": request.AcknowledgmentStateTag = ReadString(property.Value, property.Name, true); break;
                case "acknowledgmentStateTagBitNumber": request.AcknowledgmentStateTagBitNumber = ReadInt32(property.Value, property.Name); break;
                case "acknowledgmentControlTag": request.AcknowledgmentControlTag = ReadString(property.Value, property.Name, true); break;
                case "acknowledgmentControlTagBitNumber": request.AcknowledgmentControlTagBitNumber = ReadInt32(property.Value, property.Name); break;
                case "alarmParameterTags": request.AlarmParameterTags = ReadStringArray(property.Value, property.Name); break;
                case "texts": request.Texts = ParseTexts(property.Value); break;
                default: throw new InvalidOperationException($"Unknown alarm property '{property.Name}'.");
            }
        }
        return request;
    }

    private static IReadOnlyDictionary<string, HmiDiscreteAlarmTextPatch>? ParseTexts(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("texts must be an object keyed by culture.");
        var result = new SortedDictionary<string, HmiDiscreteAlarmTextPatch>(StringComparer.Ordinal);
        foreach (var culture in value.EnumerateObject())
        {
            try { _ = CultureInfo.GetCultureInfo(culture.Name); }
            catch (CultureNotFoundException) { throw new InvalidOperationException($"Unknown culture '{culture.Name}'."); }
            if (culture.Value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"texts.{culture.Name} must be an object.");
            var patch = new HmiDiscreteAlarmTextPatch();
            foreach (var property in culture.Value.EnumerateObject())
            {
                var clrName = char.ToUpperInvariant(property.Name[0]) + property.Name.Substring(1);
                var target = typeof(HmiDiscreteAlarmTextPatch).GetProperty(clrName);
                if (target == null) throw new InvalidOperationException($"Unknown localized text property '{property.Name}'.");
                target.SetValue(patch, ReadString(property.Value, property.Name, allowNull: true));
                patch.PresentProperties.Add(clrName);
            }
            result[culture.Name] = patch;
        }
        return result;
    }

    private static IReadOnlyList<HmiDiscreteAlarmPropertyDiff> Diff(HmiDiscreteAlarmSnapshot? before, HmiDiscreteAlarmSnapshot after)
    {
        if (before == null) return new[] { new HmiDiscreteAlarmPropertyDiff { Property = "$alarm", Before = null, After = after } };
        var diffs = new List<HmiDiscreteAlarmPropertyDiff>();
        foreach (var property in typeof(HmiDiscreteAlarmSnapshot).GetProperties().Where(x => x.Name != nameof(HmiDiscreteAlarmSnapshot.Validation) && x.Name != nameof(HmiDiscreteAlarmSnapshot.ContentHash)))
        {
            var left = JsonSerializer.Serialize(property.GetValue(before));
            var right = JsonSerializer.Serialize(property.GetValue(after));
            if (!string.Equals(left, right, StringComparison.Ordinal)) diffs.Add(new HmiDiscreteAlarmPropertyDiff { Property = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1), Before = property.GetValue(before), After = property.GetValue(after) });
        }
        return diffs;
    }

    private static string GetRequiredString(JsonElement owner, string name) => owner.TryGetProperty(name, out var value) ? ReadString(value, name, false)! : throw new InvalidOperationException($"Missing required property '{name}'.");
    private static string? ReadString(JsonElement value, string name, bool allowNull) => value.ValueKind == JsonValueKind.Null && allowNull ? null : value.ValueKind == JsonValueKind.String ? value.GetString() : throw new InvalidOperationException($"{name} must be a string{(allowNull ? " or null" : string.Empty)}.");
    private static uint? ReadUInt32(JsonElement value, string name) => value.ValueKind == JsonValueKind.Null ? null : value.TryGetUInt32(out var result) ? result : throw new InvalidOperationException($"{name} must be an unsigned 32-bit integer or null.");
    private static int? ReadInt32(JsonElement value, string name) => value.ValueKind == JsonValueKind.Null ? null : value.TryGetInt32(out var result) ? result : throw new InvalidOperationException($"{name} must be a signed 32-bit integer or null.");
    private static byte? ReadByte(JsonElement value, string name) => value.ValueKind == JsonValueKind.Null ? null : value.TryGetByte(out var result) ? result : throw new InvalidOperationException($"{name} must be an integer from 0 through 255 or null.");
    private static IReadOnlyList<string>? ReadStringArray(JsonElement value, string name) => value.ValueKind == JsonValueKind.Null ? null : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => ReadString(x, name, false)!).ToArray() : throw new InvalidOperationException($"{name} must be an array of strings or null.");
    private static bool FixedEquals(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameInventory(IReadOnlyDictionary<string, HmiDiscreteAlarmSnapshot> left, IReadOnlyDictionary<string, HmiDiscreteAlarmSnapshot> right) => left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var value) && FixedEquals(x.Value.ContentHash, value.ContentHash));

    private static HmiDiscreteAlarmApplyResult BlockedApply(bool dryRun, string code, string message, IReadOnlyList<HmiDiscreteAlarmPlanItem>? items = null, HmiDiscreteAlarmTransactionInfo? transaction = null) => new() { DryRun = dryRun, SafetyBlocked = true, ErrorCode = code, Message = message, Alarms = items ?? Array.Empty<HmiDiscreteAlarmPlanItem>(), Transaction = transaction ?? new HmiDiscreteAlarmTransactionInfo() };
    private static HmiDiscreteAlarmApplyResult ExceptionApply(bool dryRun, Exception ex, IReadOnlyList<HmiDiscreteAlarmPlanItem> items, HmiDiscreteAlarmTransactionInfo transaction) { var mapped = MapException(ex); return BlockedApply(dryRun, mapped.Code, mapped.Message, items, transaction); }
    private static HmiDiscreteAlarmDeleteResult BlockedDelete(bool dryRun, string code, string message, HmiDiscreteAlarmSnapshot? before = null, IReadOnlyList<HmiDiscreteAlarmCrossReference>? refs = null, string? limitation = null, HmiDiscreteAlarmTransactionInfo? transaction = null) => new() { DryRun = dryRun, SafetyBlocked = true, ErrorCode = code, Message = message, Before = before, CrossReferences = refs ?? Array.Empty<HmiDiscreteAlarmCrossReference>(), CrossReferenceLimitation = limitation, Transaction = transaction ?? new HmiDiscreteAlarmTransactionInfo() };
    private static (string Code, string Message) MapException(Exception ex)
    {
        var root = ex;
        while (root.InnerException != null) root = root.InnerException;
        if (root.GetType().Name.IndexOf("LicenseNotFoundException", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("unified-license-required", "A Siemens WinCC Unified engineering license is required for discrete-alarm access. Install or activate the license and retry.");
        return ("discrete-alarm-operation-failed", root.Message);
    }

    private sealed class DiscreteAlarmSafetyException : Exception
    {
        public DiscreteAlarmSafetyException(string code, string message) : base(message) => Code = code;
        public string Code { get; }
    }
}
