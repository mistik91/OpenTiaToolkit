#if NET48
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Audit;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Application.Execution;

public interface IToolkitClient
{
    CommandDescriptor[] Commands { get; }
    Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken);
}

public sealed class ToolkitSession : IToolkitClient, IDisposable
{
    private readonly WorkerProcessTransport _transport;
    private readonly CommandDispatcher _dispatcher;

    private ToolkitSession(
        WorkerProcessTransport transport,
        CommandDescriptor[] commands,
        CommandDispatcher dispatcher)
    {
        _transport = transport;
        Commands = commands;
        _dispatcher = dispatcher;
    }

    public CommandDescriptor[] Commands { get; }

    public static async Task<ToolkitSession> StartAsync(
        string workerExecutablePath,
        string auditJournalPath,
        CancellationToken cancellationToken)
    {
        WorkerProcessTransport transport = await WorkerProcessTransport.StartAsync(
            workerExecutablePath,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
        try
        {
            CommandDescriptor[] commands = await transport.GetCommandsAsync(cancellationToken).ConfigureAwait(false);
            var dispatcher = new CommandDispatcher(
                transport,
                new MutationPolicy(),
                new JsonLinesAuditJournal(auditJournalPath),
                commands);
            return new ToolkitSession(transport, commands, dispatcher);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    public Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken)
    {
        return _dispatcher.ExecuteAsync(invocation, cancellationToken);
    }

    public CommandDescriptor? FindCommand(string name)
    {
        return Commands.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _transport.Dispose();
    }

    public void Terminate()
    {
        _transport.Terminate();
    }

    public static string ResolveWorkerPath(string hostExecutablePath)
    {
        string? configured = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_EXE");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        string hostDirectory = Path.GetDirectoryName(Path.GetFullPath(hostExecutablePath))!;
        var candidates = new System.Collections.Generic.List<string>
        {
            Path.Combine(hostDirectory, "worker-v21", "OpenTiaToolkit.Worker.V21.exe"),
            Path.Combine(hostDirectory, "OpenTiaToolkit.Worker.V21.exe"),
            Path.GetFullPath(Path.Combine(hostDirectory, "..", "..", "..", "..", "OpenTiaToolkit.Worker.V21", "bin", "Debug", "net48", "OpenTiaToolkit.Worker.V21.exe")),
            Path.GetFullPath(Path.Combine(hostDirectory, "..", "..", "..", "..", "OpenTiaToolkit.Worker.V21", "bin", "Release", "net48", "OpenTiaToolkit.Worker.V21.exe"))
        };

        foreach (string start in new[] { hostDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? current = new DirectoryInfo(start);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln")))
                {
                    string preferredConfiguration = start.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Release"
                        : "Debug";
                    string fallbackConfiguration = preferredConfiguration == "Release" ? "Debug" : "Release";
                    candidates.Add(Path.Combine(current.FullName, "src", "OpenTiaToolkit.Worker.V21", "bin", preferredConfiguration, "net48", "OpenTiaToolkit.Worker.V21.exe"));
                    candidates.Add(Path.Combine(current.FullName, "src", "OpenTiaToolkit.Worker.V21", "bin", fallbackConfiguration, "net48", "OpenTiaToolkit.Worker.V21.exe"));
                    break;
                }

                current = current.Parent;
            }
        }

        string? found = candidates.FirstOrDefault(File.Exists);
        return found ?? throw new FileNotFoundException(
            "The V21 worker was not found. Set OPEN_TIA_WORKER_EXE or install the worker-v21 payload beside the host.");
    }

    public static string ResolveDefaultAuditPath()
    {
        string? configured = Environment.GetEnvironmentVariable("OPEN_TIA_AUDIT_JOURNAL");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenTiaToolkit",
            "audit",
            "mutations.jsonl");
    }
}
#endif
