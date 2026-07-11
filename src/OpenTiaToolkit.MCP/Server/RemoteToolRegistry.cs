using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.MCP.Server;

public sealed class ToolRegistry : IDisposable
{
    private readonly string _workerPath;
    private readonly string _auditPath;
    private readonly McpRuntimeOptions _options;
    private readonly ILogger<ToolRegistry> _logger;
    private readonly McpFileAccessPolicy _filePolicy;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly object _sync = new object();
    private ToolkitSession _session;
    private Dictionary<string, CommandDescriptor> _commands;
    private OperationSnapshot? _active;

    public ToolRegistry(string workerPath, string auditPath, McpRuntimeOptions options, ILogger<ToolRegistry> logger)
    {
        _workerPath = workerPath;
        _auditPath = auditPath;
        _options = options;
        _logger = logger;
        _filePolicy = new McpFileAccessPolicy(options);
        _session = StartSession();
        _commands = _session.Commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }

    public List<Tool> ListTools()
    {
        var tools = _commands.Values.Select(ToTool).ToList();
        tools.Add(BuiltIn("operation-status", "Reports the active isolated worker operation."));
        tools.Add(BuiltIn("cancel-operation", "Requests cancellation and terminates the worker if necessary."));
        tools.Add(BuiltIn("reset-session", "Replaces the isolated V21 worker and its Siemens session."));
        return tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async ValueTask<CallToolResult> CallToolAsync(
        string? toolName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolName)) throw new McpException("Tool name is required.");
        if (string.Equals(toolName, "operation-status", StringComparison.OrdinalIgnoreCase))
            return Result(StatusOutcome());
        if (string.Equals(toolName, "cancel-operation", StringComparison.OrdinalIgnoreCase))
            return await CancelAsync().ConfigureAwait(false);
        if (string.Equals(toolName, "reset-session", StringComparison.OrdinalIgnoreCase))
            return await ResetAsync(cancellationToken).ConfigureAwait(false);
        if (!_commands.TryGetValue(toolName!, out CommandDescriptor descriptor))
            throw new McpException($"Tool '{toolName}' not found.");

        string? fileError = _filePolicy.Validate(descriptor, arguments);
        if (fileError != null) return Result(Failure(fileError, ErrorCode.PolicyDenied));
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return Result(Failure("Another TIA worker operation is active.", ErrorCode.Conflict));

        var operation = new OperationSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = toolName!,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        lock (_sync) _active = operation;
        try
        {
            var invocation = new CommandInvocation
            {
                CorrelationId = operation.Id,
                CommandName = toolName!,
                Arguments = arguments ?? new Dictionary<string, JsonElement>()
            };
            Task<CommandOutcome> execution = _session.ExecuteAsync(invocation, cancellationToken);
            if (_options.ToolTimeout.HasValue)
            {
                Task completed = await Task.WhenAny(execution, Task.Delay(_options.ToolTimeout.Value)).ConfigureAwait(false);
                if (completed != execution)
                {
                    operation.TimedOut = true;
                    operation.CancelRequested = true;
                    ReplaceWorker(force: true);
                    return Result(Failure(
                        $"Tool '{toolName}' exceeded {_options.ToolTimeout.Value.TotalSeconds:0.###} seconds; its isolated worker was terminated and replaced.",
                        ErrorCode.TimedOut,
                        operation.Id));
                }
            }

            return Result(await execution.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            return Result(Failure($"Tool '{toolName}' was cancelled.", ErrorCode.Cancelled, operation.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote MCP tool {ToolName} failed.", toolName);
            return Result(Failure(ex.InnerException?.Message ?? ex.Message, ErrorCode.WorkerUnavailable, operation.Id));
        }
        finally
        {
            lock (_sync) _active = null;
            _gate.Release();
        }
    }

    private async ValueTask<CallToolResult> CancelAsync()
    {
        OperationSnapshot? active;
        lock (_sync)
        {
            active = _active;
            if (active != null) active.CancelRequested = true;
        }
        if (active == null) return Result(Success("No operation is active."));
        ReplaceWorker(force: true);
        await Task.Yield();
        return Result(Success($"Terminated the isolated worker running '{active.ToolName}'."));
    }

    private async ValueTask<CallToolResult> ResetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReplaceWorker(force: false);
            return Result(Success("The isolated V21 worker and Siemens session were replaced."));
        }
        finally { _gate.Release(); }
    }

    private void ReplaceWorker(bool force)
    {
        ToolkitSession old;
        lock (_sync)
        {
            old = _session;
            _session = StartSession();
            _commands = _session.Commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
        }
        if (force) old.Terminate(); else old.Dispose();
    }

    private ToolkitSession StartSession() => ToolkitSession.StartAsync(
        _workerPath, _auditPath, CancellationToken.None).GetAwaiter().GetResult();

    private CommandOutcome StatusOutcome()
    {
        OperationSnapshot? active;
        lock (_sync) active = _active;
        return new CommandOutcome
        {
            CorrelationId = active?.Id ?? Guid.NewGuid().ToString("N"),
            Success = true,
            Message = active == null ? "No operation is active." : $"'{active.ToolName}' is active.",
            Data = JsonSerializer.SerializeToElement(active),
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static Tool ToTool(CommandDescriptor descriptor)
    {
        var properties = new Dictionary<string, object?>();
        var required = new List<string>();
        foreach (CommandParameterContract parameter in descriptor.Parameters)
        {
            properties[parameter.Name] = new Dictionary<string, object?>
            {
                ["type"] = JsonType(parameter.Kind),
                ["description"] = parameter.Description
            };
            if (parameter.Required) required.Add(parameter.Name);
        }
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };
        if (required.Count > 0) schema["required"] = required;
        return new Tool { Name = descriptor.Name, Description = descriptor.Description, InputSchema = JsonSerializer.SerializeToElement(schema) };
    }

    private static string JsonType(CommandParameterKind kind) => kind switch
    {
        CommandParameterKind.Integer => "integer",
        CommandParameterKind.Boolean => "boolean",
        CommandParameterKind.StringArray or CommandParameterKind.ObjectArray => "array",
        CommandParameterKind.Object => "object",
        _ => "string"
    };

    private static Tool BuiltIn(string name, string description) => new Tool
    {
        Name = name,
        Description = description,
        InputSchema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false })
    };

    private static CallToolResult Result(CommandOutcome outcome)
    {
        JsonElement? data = outcome.Data;
        if (data.HasValue && Encoding.UTF8.GetByteCount(data.Value.GetRawText()) > 512 * 1024)
        {
            string directory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "CommandArtifacts");
            Directory.CreateDirectory(directory);
            string outputPath = Path.Combine(directory, "mcp-result-" + outcome.CorrelationId + ".json");
            File.WriteAllText(outputPath, data.Value.GetRawText(), new UTF8Encoding(false));
            data = JsonSerializer.SerializeToElement(new
            {
                outputPath,
                format = "json",
                dataKind = "worker-command-result",
                bytesWritten = new FileInfo(outputPath).Length,
                inlineOutputSuppressed = true,
                reason = "The typed IPC result exceeded the 512 KiB MCP inline limit."
            });
        }

        JsonNode structured = JsonSerializer.SerializeToNode(new
        {
            success = outcome.Success,
            message = outcome.Message,
            error = outcome.Error,
            data,
            correlationId = outcome.CorrelationId
        }, ContractJson.CreateOptions()) ?? new JsonObject();
        return new CallToolResult
        {
            IsError = !outcome.Success,
            StructuredContent = structured,
            Content = new List<ContentBlock> { new TextContentBlock { Text = structured.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) } }
        };
    }

    private static CommandOutcome Success(string message) => new CommandOutcome
    {
        CorrelationId = Guid.NewGuid().ToString("N"),
        Success = true,
        Message = message,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow
    };

    private static CommandOutcome Failure(string message, ErrorCode code, string? correlationId = null) => new CommandOutcome
    {
        CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
        Success = false,
        Message = message,
        Error = new ErrorDetail { Code = code, Message = message },
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow
    };

    public void Dispose()
    {
        _session.Dispose();
        _gate.Dispose();
    }

    private sealed class OperationSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; set; }
        public bool CancelRequested { get; set; }
        public bool TimedOut { get; set; }
    }
}
