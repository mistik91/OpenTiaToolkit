using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed class PlcCodeEvidenceRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _root;
    private readonly object _sync = new object();

    public PlcCodeEvidenceRegistry() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTiaToolkit", "plc-code-evidence", "v1")) { }

    internal PlcCodeEvidenceRegistry(string root) => _root = Path.GetFullPath(root);

    public PlcCodeEvidenceRecord Get(string evidenceId)
    {
        string path = EvidencePath(evidenceId);
        if (!File.Exists(path)) throw new InvalidOperationException($"PLC code evidence '{evidenceId}' was not found.");
        return Read(path);
    }

    public IReadOnlyList<PlcCodeEvidenceRecord> List(string? status = null, string? codeFormat = null)
    {
        if (!Directory.Exists(_root)) return Array.Empty<PlcCodeEvidenceRecord>();
        var records = new List<PlcCodeEvidenceRecord>();
        foreach (string path in Directory.GetFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try { records.Add(Read(path)); }
            catch { }
        }
        return records
            .Where(record => string.IsNullOrWhiteSpace(status) || record.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(record => string.IsNullOrWhiteSpace(codeFormat) || record.CodeFormat.Equals(codeFormat, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.CompletedAtUtc ?? record.StartedAtUtc)
            .ToArray();
    }

    public IReadOnlyList<PlcCodeEvidenceRecord> FindVerified(string compatibilityFingerprint, string qualificationSignature) =>
        List("Verified", null)
            .Where(record => FixedEquals(record.Compatibility.Fingerprint, compatibilityFingerprint) && FixedEquals(record.QualificationSignature, qualificationSignature))
            .ToArray();

    internal void Save(PlcCodeEvidenceRecord record)
    {
        if (!Guid.TryParseExact(record.EvidenceId, "N", out _)) throw new InvalidOperationException("Evidence id is invalid.");
        if (record.RegistryVersion != 1 || record.SchemaVersion != "1.0") throw new InvalidOperationException("Unsupported PLC code evidence schema.");
        lock (_sync)
        {
            Directory.CreateDirectory(_root);
            string path = EvidencePath(record.EvidenceId);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    internal static PlcCodeEvidenceCompatibility BuildCompatibility(IReadOnlyList<PlcCodeCapabilityDiscovery> capabilities, PlcCodeArtifactValidationResult validation)
    {
        if (capabilities.Count == 0) throw new InvalidOperationException("At least one capability is required to build evidence compatibility.");
        PlcCodeCapabilityDiscovery first = capabilities[0];
        if (string.IsNullOrWhiteSpace(first.Environment.TiaVersion) || string.IsNullOrWhiteSpace(first.Environment.TiaBuild))
            throw new InvalidOperationException("Exact TIA version and build/update evidence are required before code evidence can be queried or promoted.");
        if (string.IsNullOrWhiteSpace(first.Environment.PlcFamily) && string.IsNullOrWhiteSpace(first.Environment.PlcTypeIdentifier) && string.IsNullOrWhiteSpace(first.Environment.PlcOrderNumber))
            throw new InvalidOperationException("Exact PLC family, type identifier, or order-number evidence is required before code evidence can be queried or promoted.");
        string route = first.SelectedRoute;
        if (capabilities.Any(capability => !capability.SelectedRoute.Equals(route, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("All probe capabilities must select the same mutation route.");
        if (capabilities.Any(capability => !SameEnvironment(first.Environment, capability.Environment)))
            throw new InvalidOperationException("All probe capabilities must describe the same TIA and PLC environment.");
        bool projectBound = validation.Dependencies.Count > 0;
        var result = new PlcCodeEvidenceCompatibility
        {
            Scope = projectBound ? "Project" : "Environment",
            TiaVersion = first.Environment.TiaVersion,
            TiaBuild = first.Environment.TiaBuild,
            PlcFamily = first.Environment.PlcFamily,
            PlcTypeIdentifier = first.Environment.PlcTypeIdentifier,
            PlcOrderNumber = first.Environment.PlcOrderNumber,
            Route = route,
            CodeFormat = validation.Format,
            SafetyContext = capabilities.Any(capability => capability.Safety.IsFailSafeTarget) ? "FailSafe" : "Standard",
            ProjectFingerprint = projectBound ? Hash(Encoding.UTF8.GetBytes(first.Environment.ProjectPath + "|" + first.Environment.ProjectName)) : string.Empty,
            ObjectShapes = capabilities.Select(capability => string.Join("|", capability.Request.ObjectKind, capability.Request.BlockType ?? string.Empty, capability.Request.RequestedLanguage ?? capability.Target.ProgrammingLanguage ?? string.Empty)).OrderBy(value => value, StringComparer.Ordinal).ToArray()
        };
        result.Fingerprint = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            result.SchemaVersion,
            result.Scope,
            result.TiaVersion,
            result.TiaBuild,
            result.PlcFamily,
            result.PlcTypeIdentifier,
            result.PlcOrderNumber,
            result.Route,
            result.CodeFormat,
            result.SafetyContext,
            result.ProjectFingerprint,
            result.ObjectShapes
        })));
        return result;
    }

    private PlcCodeEvidenceRecord Read(string path) => JsonSerializer.Deserialize<PlcCodeEvidenceRecord>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"PLC code evidence manifest is invalid: {path}");
    private string EvidencePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Evidence id is invalid.");
        return Path.Combine(_root, id + ".json");
    }
    private static bool SameEnvironment(PlcCodeEnvironmentEvidence left, PlcCodeEnvironmentEvidence right) =>
        string.Equals(left.TiaVersion, right.TiaVersion, StringComparison.Ordinal) &&
        string.Equals(left.TiaBuild, right.TiaBuild, StringComparison.Ordinal) &&
        string.Equals(left.PlcName, right.PlcName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.PlcTypeIdentifier, right.PlcTypeIdentifier, StringComparison.Ordinal) &&
        string.Equals(left.PlcOrderNumber, right.PlcOrderNumber, StringComparison.Ordinal);
    private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
    private static bool FixedEquals(string left, string right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left ?? string.Empty); byte[] b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        if (a.Length != b.Length) return false;
        int difference = 0; for (int index = 0; index < a.Length; index++) difference |= a[index] ^ b[index];
        return difference == 0;
    }
}
