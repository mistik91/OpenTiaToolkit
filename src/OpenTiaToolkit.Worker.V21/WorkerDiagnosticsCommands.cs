using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Contracts.Protocol;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Worker.V21;

internal abstract class WorkerDiagnosticCommand : ICommand
{
    protected WorkerDiagnosticCommand(string name, string description)
    {
        Name = name;
        Description = description;
        Schema = new CommandSchema(description);
    }

    public string Name { get; }
    public string Description { get; }
    public CommandSchema Schema { get; }
    public abstract Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger);

    protected static IReadOnlyDictionary<string, string> SiemensHashes()
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string root = Environment.GetEnvironmentVariable("OPEN_TIA_PORTAL_ROOT") ??
            @"C:\Program Files\Siemens\Automation\Portal V21";
        string api = Path.Combine(root, "PublicAPI", "V21", "net48");
        foreach (string name in new[]
        {
            "Siemens.Engineering.Base.dll", "Siemens.Engineering.Step7.dll",
            "Siemens.Engineering.WinCCUnified.dll", "Siemens.Engineering.Startdrive.dll"
        })
        {
            string path = Path.Combine(api, name);
            if (!File.Exists(path)) continue;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                hashes[name] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        return hashes;
    }

    protected static object CompatibilityStatus(IReadOnlyDictionary<string, string> actual)
    {
        string profilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "compatibility", "tia-v21.json");
        if (!File.Exists(profilePath)) return new { matched = false, profilePath, error = "Compatibility profile is missing." };
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(profilePath)))
        {
            var mismatches = new List<string>();
            foreach (JsonElement assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                string name = assembly.GetProperty("name").GetString()!;
                string expected = assembly.GetProperty("sha256").GetString()!;
                if (!actual.TryGetValue(name, out string found) || !string.Equals(expected, found, StringComparison.OrdinalIgnoreCase))
                    mismatches.Add(name);
            }
            return new { matched = mismatches.Count == 0, profilePath, mismatches };
        }
    }
}

internal sealed class VersionCommand : WorkerDiagnosticCommand
{
    public VersionCommand() : base("version", "Reports toolkit, IPC, worker, runtime, and target TIA versions.") { }

    public override Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        Assembly assembly = typeof(Program).Assembly;
        return Task.FromResult(CommandResult.CreateSuccess(new
        {
            toolkitVersion = assembly.GetName().Version?.ToString(),
            informationalVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion,
            protocolVersion = ProtocolConstants.Version,
            worker = "V21",
            runtime = Environment.Version.ToString(),
            targetFramework = ".NET Framework 4.8",
            tiaPortalTarget = "V21"
        }));
    }
}

internal sealed class DoctorCommand : WorkerDiagnosticCommand
{
    public DoctorCommand() : base("doctor", "Checks the isolated worker runtime, Siemens V21 API, paths, and process architecture.") { }

    public override Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        IReadOnlyDictionary<string, string> hashes = SiemensHashes();
        object compatibility = CompatibilityStatus(hashes);
        bool matched = (bool)compatibility.GetType().GetProperty("matched")!.GetValue(compatibility)!;
        bool healthy = Environment.Is64BitProcess && hashes.Count == 4 && matched;
        object data = new
        {
            healthy,
            process64Bit = Environment.Is64BitProcess,
            processId = Process.GetCurrentProcess().Id,
            workerPath = Assembly.GetExecutingAssembly().Location,
            siemensAssemblies = hashes,
            compatibility,
            auditJournal = Environment.GetEnvironmentVariable("OPEN_TIA_AUDIT_JOURNAL"),
            failureInjectionEnabled = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_FAILURE_INJECTION"))
        };
        return Task.FromResult(healthy
            ? CommandResult.CreateSuccess(data, "The isolated V21 worker passed diagnostics.")
            : CommandResult.CreateFailure("The isolated V21 worker is missing required Siemens assemblies or is not 64-bit.", data));
    }
}

internal sealed class CapabilitiesCommand : WorkerDiagnosticCommand
{
    public CapabilitiesCommand() : base("capabilities", "Reports the worker capability profile and exact Siemens assembly hashes.") { }

    public override Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        int coreCommandTypes = typeof(ICommand).Assembly.GetTypes().Count(type =>
            typeof(ICommand).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract);
        IReadOnlyDictionary<string, string> hashes = SiemensHashes();
        return Task.FromResult(CommandResult.CreateSuccess(new
        {
            protocolVersion = ProtocolConstants.Version,
            worker = "V21",
            supportedCommandCount = coreCommandTypes + 3,
            features = new[] { "isolated-worker", "single-request-queue", "typed-outcomes", "mutation-policy", "audit-journal", "pagination", "failure-injection" },
            siemensAssemblyHashes = hashes,
            compatibility = CompatibilityStatus(hashes)
        }));
    }
}
