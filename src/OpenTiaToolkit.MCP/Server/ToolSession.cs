using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.MCP.Server;

internal interface IToolSession : IDisposable
{
    IReadOnlyList<CommandDescriptor> Commands { get; }

    Task<CommandOutcome> ExecuteAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken);

    void Terminate();
}

internal sealed class WorkerToolSession : IToolSession
{
    private readonly ToolkitSession _session;

    private WorkerToolSession(ToolkitSession session)
    {
        _session = session;
    }

    public IReadOnlyList<CommandDescriptor> Commands => _session.Commands;

    public static WorkerToolSession Start(string workerPath, string auditPath)
    {
        return new WorkerToolSession(
            ToolkitSession.StartAsync(workerPath, auditPath, CancellationToken.None)
                .GetAwaiter()
                .GetResult());
    }

    public Task<CommandOutcome> ExecuteAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        return _session.ExecuteAsync(invocation, cancellationToken);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    public void Terminate()
    {
        _session.Terminate();
    }
}
