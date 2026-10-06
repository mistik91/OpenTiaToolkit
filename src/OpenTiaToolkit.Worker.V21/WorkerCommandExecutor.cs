using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Worker.V21;

internal sealed class WorkerCommandExecutor
{
    private readonly IReadOnlyDictionary<string, ICommand> _commands;
    private readonly ILogger<WorkerCommandExecutor> _logger;

    public WorkerCommandExecutor(IEnumerable<ICommand> commands, ILogger<WorkerCommandExecutor> logger)
    {
        _commands = commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public CommandDescriptor[] GetCatalog()
    {
        return _commands.Values
            .Select(CommandContractMapper.ToDescriptor)
            .OrderBy(command => command.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        if (!_commands.TryGetValue(invocation.CommandName, out ICommand command))
        {
            return Failure(invocation, started, ErrorCode.NotFound, $"Unknown command '{invocation.CommandName}'.");
        }

        try
        {
            ApplyFailureInjection("before-execute");
            var values = invocation.Arguments.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
            CommandResult result = await command.ExecuteAsync(new CommandArgs(values), _logger).ConfigureAwait(false);
            var outcome = new CommandOutcome
            {
                CorrelationId = invocation.CorrelationId,
                Success = result.Success,
                Message = result.Message,
                Data = SerializeData(result.Data, invocation),
                Error = result.Success ? null : new ErrorDetail
                {
                    Code = ClassifyError(result.Message),
                    Message = result.Error ?? result.Message
                },
                StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
            ApplyFailureInjection("after-execute");
            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker command {CommandName} failed.", invocation.CommandName);
            return Failure(invocation, started, ErrorCode.InternalError, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static JsonElement? SerializeData(object? data, CommandInvocation invocation)
    {
        if (data == null) return null;
        JsonElement serialized = JsonSerializer.SerializeToElement(data);
        if (!invocation.Arguments.TryGetValue("page-size", out JsonElement pageSizeElement) ||
            !pageSizeElement.TryGetInt32(out int pageSize))
            return serialized;
        if (pageSize < 1 || pageSize > 1000)
            throw new ArgumentOutOfRangeException("page-size", "Page size must be between 1 and 1000.");
        if (serialized.ValueKind != JsonValueKind.Array) return serialized;

        int offset = 0;
        if (invocation.Arguments.TryGetValue("continuation-token", out JsonElement tokenElement) &&
            tokenElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tokenElement.GetString()))
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(tokenElement.GetString()!));
            if (!int.TryParse(decoded, out offset) || offset < 0)
                throw new FormatException("The continuation token is invalid.");
        }

        JsonElement[] all = serialized.EnumerateArray().Select(item => item.Clone()).ToArray();
        JsonElement[] items = all.Skip(offset).Take(pageSize).ToArray();
        int nextOffset = offset + items.Length;
        string? next = nextOffset < all.Length
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(nextOffset.ToString()))
            : null;
        return JsonSerializer.SerializeToElement(new
        {
            items,
            continuationToken = next,
            hasMore = next != null,
            totalCount = all.Length
        });
    }

    private static void ApplyFailureInjection(string stage)
    {
        string? configured = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_FAILURE_INJECTION");
        if (string.IsNullOrWhiteSpace(configured)) return;
        if (string.Equals(configured, "crash:" + stage, StringComparison.OrdinalIgnoreCase))
            Environment.FailFast("Injected V21 worker crash at " + stage + ".");
        if (string.Equals(configured, "throw:" + stage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Injected V21 worker failure at " + stage + ".");
        if (configured.StartsWith("delay:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(configured.Substring("delay:".Length), out int milliseconds) && milliseconds > 0)
            System.Threading.Thread.Sleep(milliseconds);
    }

    private static ErrorCode ClassifyError(string message)
    {
        if (message.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0) return ErrorCode.Cancelled;
        if (message.IndexOf("not connected", StringComparison.OrdinalIgnoreCase) >= 0) return ErrorCode.NotConnected;
        if (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) return ErrorCode.NotFound;
        if (message.IndexOf("partial", StringComparison.OrdinalIgnoreCase) >= 0) return ErrorCode.PartialFailure;
        if (message.IndexOf("validation", StringComparison.OrdinalIgnoreCase) >= 0) return ErrorCode.ValidationFailed;
        if (message.IndexOf("required", StringComparison.OrdinalIgnoreCase) >= 0) return ErrorCode.ValidationFailed;
        return ErrorCode.SiemensRejected;
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
