#if NET48
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTiaToolkit.Contracts.Compatibility;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.Application.Execution;

public static class WorkerPackageResolver
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public static string Resolve(string hostExecutablePath, string tiaVersion)
    {
        string normalized = NormalizeVersion(tiaVersion);
        string? versioned = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_" + normalized.ToUpperInvariant() + "_EXE");
        if (!string.IsNullOrWhiteSpace(versioned)) return ValidateExecutable(versioned!, normalized);
        string? legacy = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_EXE");
        if (!string.IsNullOrWhiteSpace(legacy)) return ValidateExecutable(legacy!, normalized);

        string hostDirectory = Path.GetDirectoryName(Path.GetFullPath(hostExecutablePath))!;
        foreach (string candidate in CandidatePaths(hostDirectory, normalized))
            if (File.Exists(candidate)) return ValidateExecutable(candidate, normalized);
        throw new FileNotFoundException($"No worker package supporting TIA {normalized} was found. Set OPEN_TIA_WORKER_{normalized.ToUpperInvariant()}_EXE.");
    }

    public static WorkerPackageManifest? ReadManifestForExecutable(string executablePath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!;
        string path = Path.Combine(directory, "worker-manifest.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<WorkerPackageManifest>(File.ReadAllText(path), JsonOptions)
            : null;
    }

    private static string ValidateExecutable(string path, string normalizedVersion)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Configured worker executable does not exist.", fullPath);
        WorkerPackageManifest? manifest = ReadManifestForExecutable(fullPath);
        if (manifest != null)
        {
            if (!string.Equals(manifest.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
                throw new InvalidDataException($"Worker package protocol '{manifest.ProtocolVersion}' does not match host protocol '{ProtocolConstants.Version}'.");
            if (!manifest.SupportedTiaVersions.Any(item => string.Equals(NormalizeVersion(item), normalizedVersion, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Worker package '{manifest.WorkerId}' does not support TIA {normalizedVersion}.");
        }
        return fullPath;
    }

    private static IEnumerable<string> CandidatePaths(string hostDirectory, string version)
    {
        string suffix = version.TrimStart('V', 'v').Replace(".", string.Empty);
        yield return Path.Combine(hostDirectory, "worker-v" + suffix, $"OpenTiaToolkit.Worker.V{suffix}.exe");
        yield return Path.Combine(hostDirectory, $"OpenTiaToolkit.Worker.V{suffix}.exe");
        string? installRoot = Path.GetDirectoryName(hostDirectory);
        if (installRoot != null)
            yield return Path.Combine(installRoot, "worker-v" + suffix, $"OpenTiaToolkit.Worker.V{suffix}.exe");
        foreach (string start in new[] { hostDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? current = new DirectoryInfo(start);
            while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
            if (current == null) continue;
            string configuration = start.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0 ? "Release" : "Debug";
            yield return Path.Combine(current.FullName, "src", $"OpenTiaToolkit.Worker.V{suffix}", "bin", configuration, "net48", $"OpenTiaToolkit.Worker.V{suffix}.exe");
            yield return Path.Combine(current.FullName, "src", $"OpenTiaToolkit.Worker.V{suffix}", "bin", configuration == "Release" ? "Debug" : "Release", "net48", $"OpenTiaToolkit.Worker.V{suffix}.exe");
        }
    }

    private static string NormalizeVersion(string version)
    {
        string value = (version ?? string.Empty).Trim();
        if (value.StartsWith("TIA", StringComparison.OrdinalIgnoreCase)) value = value.Substring(3).Trim();
        if (!value.StartsWith("V", StringComparison.OrdinalIgnoreCase)) value = "V" + value;
        return value.ToUpperInvariant();
    }
}
#endif
