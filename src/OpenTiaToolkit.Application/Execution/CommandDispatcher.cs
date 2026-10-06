using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Contracts.Audit;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Application.Execution;

public sealed class CommandDispatcher
{
    private readonly ICommandTransport _transport;
    private readonly IMutationPolicy _policy;
    private readonly IAuditJournal _journal;
    private readonly IReadOnlyDictionary<string, CommandDescriptor> _commands;

    public CommandDispatcher(
        ICommandTransport transport,
        IMutationPolicy policy,
        IAuditJournal journal,
        IEnumerable<CommandDescriptor> commands)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _commands = commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        if (!_commands.TryGetValue(invocation.CommandName, out CommandDescriptor descriptor))
        {
            return Failure(invocation, started, ErrorCode.NotFound, $"Unknown command '{invocation.CommandName}'.");
        }

        PolicyDecision decision = _policy.Evaluate(descriptor, invocation);
        CommandOutcome outcome;
        if (!decision.Allowed)
        {
            outcome = Failure(invocation, started, ErrorCode.PolicyDenied, decision.Message);
        }
        else
        {
            try
            {
                outcome = await _transport.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                outcome = Failure(invocation, started, ErrorCode.Cancelled, "Command execution was cancelled.");
            }
            catch (Exception ex)
            {
                outcome = Failure(
                    invocation,
                    started,
                    ErrorCode.WorkerUnavailable,
                    ex.InnerException?.Message ?? ex.Message);
            }
        }

        if (descriptor.Risk != CommandRisk.ReadOnly)
        {
            await _journal.AppendAsync(new MutationAuditRecord
            {
                CorrelationId = invocation.CorrelationId,
                TimestampUtc = DateTimeOffset.UtcNow,
                CommandName = descriptor.Name,
                Risk = descriptor.Risk,
                Preview = decision.Preview,
                Allowed = decision.Allowed,
                Success = outcome.Success,
                ErrorCode = outcome.Error?.Code ?? ErrorCode.None,
                Message = outcome.Message,
                RedactedArguments = AuditRedactor.Redact(invocation.Arguments)
            }, CancellationToken.None).ConfigureAwait(false);
        }

        return outcome;
    }

    private static CommandOutcome Failure(CommandInvocation invocation, DateTimeOffset started, ErrorCode code, string message)
    {
        return new CommandOutcome
        {
            CorrelationId = invocation.CorrelationId,
            Success = false,
            Message = message,
            Error = new ErrorDetail { Code = code, Message = message },
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
