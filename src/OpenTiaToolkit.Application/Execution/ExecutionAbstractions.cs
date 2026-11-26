using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Contracts.Audit;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.Application.Execution;

public interface ICommandTransport : IDisposable
{
    Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken,
        Action<WorkerMilestone>? reportMilestone = null);
}

public interface ICommandCatalogProvider
{
    Task<CommandDescriptor[]> GetCommandsAsync(CancellationToken cancellationToken);
}

public interface IMutationPolicy
{
    PolicyDecision Evaluate(CommandDescriptor descriptor, CommandInvocation invocation);
}

public interface IAuditJournal
{
    Task AppendAsync(MutationAuditRecord record, CancellationToken cancellationToken);
}

public sealed class PolicyDecision
{
    private PolicyDecision(bool allowed, bool preview, string message)
    {
        Allowed = allowed;
        Preview = preview;
        Message = message;
    }

    public bool Allowed { get; }
    public bool Preview { get; }
    public string Message { get; }

    public static PolicyDecision Allow(bool preview) => new PolicyDecision(true, preview, string.Empty);
    public static PolicyDecision Deny(string message) => new PolicyDecision(false, false, message);
}
