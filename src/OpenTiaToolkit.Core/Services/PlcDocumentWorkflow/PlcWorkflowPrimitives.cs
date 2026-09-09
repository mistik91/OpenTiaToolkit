using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal static class PlcWorkflowPrimitives
{
    internal static string Hash(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal static string HashFile(string path) => Hash(File.ReadAllBytes(path));

    internal static string HashDocumentPair(string directory, string baseName)
    {
        byte[] document = File.ReadAllBytes(Path.Combine(directory, baseName + ".s7dcl"));
        string resourcePath = Path.Combine(directory, baseName + ".s7res");
        byte[] resource = File.Exists(resourcePath) ? File.ReadAllBytes(resourcePath) : Array.Empty<byte>();
        return Hash(document.Concat(new byte[] { 0 }).Concat(resource).ToArray());
    }

    internal static string RandomToken()
    {
        var bytes = new byte[32];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static bool FixedEquals(string? left, string? right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        if (a.Length != b.Length) return false;
        int difference = 0;
        for (int index = 0; index < a.Length; index++) difference |= a[index] ^ b[index];
        return difference == 0;
    }

    internal static string NormalizeGroupPath(string? path, string? plcName = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = string.Join("/", path!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Trim()));
        normalized = StripPlcPrefix(normalized, plcName);
        foreach (string root in new[] { "Program Blocks", "Blocks" })
        {
            if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return normalized.Substring(root.Length + 1);
        }
        return normalized;
    }

    internal static string NormalizeTypeGroupPath(string? path, string? plcName = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = string.Join("/", path!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Trim()));
        normalized = StripPlcPrefix(normalized, plcName);
        foreach (string root in new[] { "PLC data types", "PLC types", "Types" })
        {
            if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return normalized.Substring(root.Length + 1);
        }
        return normalized;
    }

    private static string StripPlcPrefix(string path, string? plcName)
    {
        if (string.IsNullOrWhiteSpace(plcName)) return path;
        string prefix = plcName!.Trim().Trim('/', '\\') + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length) : path;
    }

    internal static string DisplayGroup(string path) => string.IsNullOrEmpty(path) ? "<root>" : path;
    internal static bool GroupEquals(string? left, string? right) => string.Equals(NormalizeGroupPath(left), NormalizeGroupPath(right), StringComparison.OrdinalIgnoreCase);
    internal static bool IsTypeTarget(PlcBlockIdentity target) => target.BlockType.Equals("TYPE", StringComparison.OrdinalIgnoreCase);
    internal static string Message(Exception exception) => exception.InnerException?.Message ?? exception.Message;
    internal static void Cleanup(string path) { try { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}

internal sealed class PlcRollbackEvidence
{
    internal PlcRollbackEvidence(bool operationSucceeded, bool hashMatches, string? expectedHash, string? actualHash)
    {
        OperationSucceeded = operationSucceeded;
        HashMatches = hashMatches;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }

    internal bool OperationSucceeded { get; }
    internal bool HashMatches { get; }
    internal string? ExpectedHash { get; }
    internal string? ActualHash { get; }
}
