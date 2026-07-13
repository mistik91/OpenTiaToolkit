using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed class PlcBehaviorVectorService
{
    private static readonly Regex IdPattern = new Regex("^[A-Za-z][A-Za-z0-9_.-]{2,63}$", RegexOptions.Compiled);
    internal static readonly string[] SupportedTypes = { "Bool", "Int8", "Int16", "Int32", "Int64", "UInt8", "UInt16", "UInt32", "UInt64", "Float", "Double", "Char", "WChar", "String", "WString" };
    private static readonly string[] SupportedOperators = { "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual" };
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = false };

    public PlcBehaviorVectorValidationResult Validate(string path, IReadOnlyList<string>? allowedTagOwners = null)
    {
        string fullPath = Path.GetFullPath(path ?? string.Empty);
        var errors = new List<string>();
        var warnings = new List<string>();
        PlcBehaviorVector? vector = null;
        byte[] bytes = Array.Empty<byte>();
        if (!File.Exists(fullPath)) errors.Add("Behavior vector was not found: " + fullPath);
        else
        {
            bytes = File.ReadAllBytes(fullPath);
            try { vector = JsonSerializer.Deserialize<PlcBehaviorVector>(bytes, JsonOptions); }
            catch (JsonException ex) { errors.Add("Behavior vector JSON is invalid: " + ex.Message); }
        }

        if (vector != null)
        {
            if (vector.SchemaVersion != "1.0") errors.Add("Unsupported behavior vector schema; expected 1.0.");
            if (!IdPattern.IsMatch(vector.VectorId ?? string.Empty)) errors.Add("vectorId must be 3-64 characters and use letters, digits, dot, underscore, or hyphen.");
            if (vector.Steps == null || vector.Steps.Count == 0) errors.Add("At least one behavior step is required.");
            if (vector.Steps?.Count > 100) errors.Add("A behavior vector may contain at most 100 steps.");
            ValidateWrites(vector.Reset, "reset", errors);
            ValidateWrites(vector.Cleanup, "cleanup", errors);
            var stepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlcBehaviorStep step in vector.Steps ?? Array.Empty<PlcBehaviorStep>())
            {
                if (string.IsNullOrWhiteSpace(step.Name)) errors.Add("Every behavior step requires a name.");
                else if (!stepNames.Add(step.Name)) errors.Add("Behavior step names must be unique: " + step.Name);
                if (step.SettleMilliseconds < 0 || step.SettleMilliseconds > 60000) errors.Add($"Step '{step.Name}' settleMilliseconds must be between 0 and 60000.");
                ValidateWrites(step.Writes, "step " + step.Name, errors);
                if ((step.Assertions?.Count ?? 0) == 0) errors.Add($"Step '{step.Name}' requires at least one assertion.");
                foreach (PlcBehaviorAssertion assertion in step.Assertions ?? Array.Empty<PlcBehaviorAssertion>()) ValidateAssertion(assertion, step.Name, errors);
            }

            string[] written = (vector.Reset ?? Array.Empty<PlcBehaviorWrite>()).Concat((vector.Steps ?? Array.Empty<PlcBehaviorStep>()).SelectMany(step => step.Writes ?? Array.Empty<PlcBehaviorWrite>())).Select(item => item.Tag).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var cleanup = new HashSet<string>((vector.Cleanup ?? Array.Empty<PlcBehaviorWrite>()).Select(item => item.Tag), StringComparer.OrdinalIgnoreCase);
            foreach (string tag in written.Where(tag => !cleanup.Contains(tag))) errors.Add($"Cleanup must restore every stimulated tag; missing '{tag}'.");
            if ((vector.Reset?.Count ?? 0) == 0) warnings.Add("The vector has no reset writes; deterministic initial state must be established by the downloaded probe itself.");

            string[] tags = AllTags(vector).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (allowedTagOwners != null)
            {
                string[] owners = allowedTagOwners.Where(owner => !string.IsNullOrWhiteSpace(owner)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (owners.Length == 0) errors.Add("The probe declares no disposable data block that can own behavior tags.");
                foreach (string tag in tags.Where(tag => !owners.Any(owner => IsOwnedTag(tag, owner))))
                    errors.Add($"Tag '{tag}' is outside the declared disposable data-block owners. Production tags cannot be stimulated by a code probe.");
            }
        }

        return new PlcBehaviorVectorValidationResult
        {
            Vector = vector,
            VectorHash = bytes.Length == 0 ? string.Empty : Sha256(bytes),
            Errors = errors,
            Warnings = warnings,
            ReferencedTags = vector == null ? Array.Empty<string>() : AllTags(vector).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    internal static bool IsOwnedTag(string tag, string owner)
    {
        string normalizedTag = NormalizeTag(tag);
        string normalizedOwner = NormalizeTag(owner);
        return normalizedTag.Equals(normalizedOwner, StringComparison.OrdinalIgnoreCase) || normalizedTag.StartsWith(normalizedOwner + ".", StringComparison.OrdinalIgnoreCase);
    }

    internal static object ConvertValue(string type, JsonElement value)
    {
        switch (CanonicalType(type))
        {
            case "Bool": return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False ? value.GetBoolean() : throw new FormatException("Bool requires a JSON boolean.");
            case "Int8": return checked((sbyte)value.GetInt32());
            case "Int16": return checked((short)value.GetInt32());
            case "Int32": return value.GetInt32();
            case "Int64": return value.GetInt64();
            case "UInt8": return checked((byte)value.GetUInt32());
            case "UInt16": return checked((ushort)value.GetUInt32());
            case "UInt32": return value.GetUInt32();
            case "UInt64": return value.GetUInt64();
            case "Float": return value.GetSingle();
            case "Double": return value.GetDouble();
            case "Char": return checked((sbyte)(value.ValueKind == JsonValueKind.String ? value.GetString()![0] : value.GetInt32()));
            case "WChar": return value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 1 ? value.GetString()![0] : throw new FormatException("WChar requires one JSON character.");
            case "String":
            case "WString": return value.GetString() ?? string.Empty;
            default: throw new FormatException("Unsupported behavior value type: " + type);
        }
    }

    internal static string CanonicalType(string type) => SupportedTypes.FirstOrDefault(item => item.Equals(type, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

    private static void ValidateWrites(IReadOnlyList<PlcBehaviorWrite>? writes, string scope, List<string> errors)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlcBehaviorWrite write in writes ?? Array.Empty<PlcBehaviorWrite>())
        {
            if (string.IsNullOrWhiteSpace(write.Tag)) errors.Add(scope + " contains a write without a tag.");
            else if (!tags.Add(write.Tag)) errors.Add(scope + " writes the same tag more than once: " + write.Tag);
            ValidateValue(write.Type, write.Value, scope + " write " + write.Tag, errors);
        }
    }

    private static void ValidateAssertion(PlcBehaviorAssertion assertion, string step, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(assertion.Tag)) errors.Add($"Step '{step}' contains an assertion without a tag.");
        ValidateValue(assertion.Type, assertion.Expected, $"step '{step}' assertion {assertion.Tag}", errors);
        if (!SupportedOperators.Contains(assertion.Operator ?? string.Empty, StringComparer.OrdinalIgnoreCase)) errors.Add($"Step '{step}' assertion '{assertion.Tag}' uses unsupported operator '{assertion.Operator}'.");
        if (assertion.TimeoutMilliseconds < 1 || assertion.TimeoutMilliseconds > 300000) errors.Add($"Assertion '{assertion.Tag}' timeoutMilliseconds must be 1-300000.");
        if (assertion.PollMilliseconds < 5 || assertion.PollMilliseconds > 10000) errors.Add($"Assertion '{assertion.Tag}' pollMilliseconds must be 5-10000.");
        if (assertion.StableMilliseconds < 0 || assertion.StableMilliseconds > assertion.TimeoutMilliseconds) errors.Add($"Assertion '{assertion.Tag}' stableMilliseconds must be between 0 and its timeout.");
        if (assertion.Tolerance.HasValue && (assertion.Tolerance < 0 || double.IsNaN(assertion.Tolerance.Value) || double.IsInfinity(assertion.Tolerance.Value))) errors.Add($"Assertion '{assertion.Tag}' tolerance must be finite and non-negative.");
    }

    private static void ValidateValue(string type, JsonElement value, string scope, List<string> errors)
    {
        if (string.IsNullOrEmpty(CanonicalType(type))) { errors.Add(scope + " uses unsupported type '" + type + "'."); return; }
        try { ConvertValue(type, value); }
        catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is OverflowException) { errors.Add(scope + " has an invalid value: " + ex.Message); }
    }

    private static IEnumerable<string> AllTags(PlcBehaviorVector vector) =>
        (vector.Reset ?? Array.Empty<PlcBehaviorWrite>()).Select(item => item.Tag)
            .Concat((vector.Steps ?? Array.Empty<PlcBehaviorStep>()).SelectMany(step => (step.Writes ?? Array.Empty<PlcBehaviorWrite>()).Select(item => item.Tag).Concat((step.Assertions ?? Array.Empty<PlcBehaviorAssertion>()).Select(item => item.Tag))))
            .Concat((vector.Cleanup ?? Array.Empty<PlcBehaviorWrite>()).Select(item => item.Tag));

    private static string NormalizeTag(string value) => string.Join(".", (value ?? string.Empty).Split('.').Select(segment => segment.Trim().Trim('"')));
    private static string Sha256(byte[] value) { using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(value).Select(item => item.ToString("x2", CultureInfo.InvariantCulture))); }
}
