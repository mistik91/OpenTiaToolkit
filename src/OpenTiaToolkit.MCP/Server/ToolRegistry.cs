using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.MCP.Server
{
    /// <summary>
    /// Registry that manages available MCP tools backed by ICommand implementations.
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ToolAdapter> _tools;
        private readonly ILogger<ToolRegistry> _logger;
        private readonly McpRuntimeOptions _options;
        private readonly ISessionResetService? _sessionResetService;
        private readonly SemaphoreSlim _executionGate = new SemaphoreSlim(1, 1);
        private readonly object _operationLock = new object();
        private ActiveToolOperation? _activeOperation;
        private bool _resetPending;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
        /// </summary>
        /// <param name="commands">The commands to register as tools.</param>
        /// <param name="logger">Logger instance.</param>
        public ToolRegistry(
            IEnumerable<ICommand> commands,
            ILogger<ToolRegistry> logger,
            McpRuntimeOptions? options = null,
            ISessionResetService? sessionResetService = null)
        {
            _logger = logger;
            _options = options ?? new McpRuntimeOptions();
            _sessionResetService = sessionResetService;
            _tools = new Dictionary<string, ToolAdapter>(StringComparer.OrdinalIgnoreCase);
            var fileAccessPolicy = new McpFileAccessPolicy(_options);

            foreach (var command in commands)
            {
                var adapter = new ToolAdapter(command, logger, fileAccessPolicy);
                _tools[command.Name] = adapter;
                _logger.LogInformation("Registered MCP tool: {ToolName} - {Description}", command.Name, command.Description);
            }
        }

        /// <summary>
        /// Gets all registered tool adapters.
        /// </summary>
        public IReadOnlyDictionary<string, ToolAdapter> Tools => _tools;

        /// <summary>
        /// Gets a tool adapter by name.
        /// </summary>
        /// <param name="toolName">The tool name.</param>
        /// <returns>The tool adapter, or null if not found.</returns>
        public ToolAdapter? GetTool(string toolName)
        {
            return _tools.TryGetValue(toolName, out var adapter) ? adapter : null;
        }

        /// <summary>
        /// Lists all available tools.
        /// </summary>
        /// <returns>List of tool information.</returns>
        public List<Tool> ListTools()
        {
            var tools = _tools.Values
                .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .Select(tool => tool.ProtocolTool)
                .ToList();
            tools.Add(CreateBuiltInTool("cancel-operation", "Requests cancellation of the active MCP tool operation.", new Dictionary<string, object?>()));
            tools.Add(CreateBuiltInTool("operation-status", "Reports the active MCP tool operation and latest progress.", new Dictionary<string, object?>()));
            tools.Add(CreateBuiltInTool("reset-session", "Cancels and waits for the active operation, then disposes the TIA session only after it is quiescent.", new Dictionary<string, object?>()));
            return tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Executes a tool by name.
        /// </summary>
        public async ValueTask<CallToolResult> CallToolAsync(
            string? toolName,
            IReadOnlyDictionary<string, JsonElement>? arguments,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new McpException("Missing tool name.");
            }

            if (string.Equals(toolName, "reset-session", StringComparison.OrdinalIgnoreCase))
            {
                var resetResult = await ResetSessionAsync(cancellationToken).ConfigureAwait(false);
                return CreateCallToolResult(ToolExecutionResult.FromCommandResult(resetResult));
            }

            if (TryHandleBuiltInTool(toolName!, out var builtInResult))
            {
                return builtInResult;
            }

            var tool = GetTool(toolName!);
            if (tool == null)
            {
                throw new McpException($"Tool not found: {toolName}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_operationLock)
            {
                if (_resetPending)
                {
                    var resetPendingResult = CommandResult.CreateFailure(
                        "TIA Portal session reset is waiting for the current operation to become quiescent.",
                        CreateOperationStatusData(_activeOperation, busy: true, resetPending: true));
                    return CreateCallToolResult(ToolExecutionResult.FromCommandResult(resetPendingResult));
                }
            }

            if (!await _executionGate.WaitAsync(0, cancellationToken))
            {
                ActiveToolOperation? activeOperation;
                lock (_operationLock)
                {
                    activeOperation = _activeOperation;
                }

                var busyResult = CommandResult.CreateFailure(
                    activeOperation == null
                        ? "TIA Portal is busy executing another MCP operation."
                        : $"TIA Portal is busy executing '{activeOperation.ToolName}' (operation {activeOperation.OperationId}). Retry after it completes.",
                    CreateOperationStatusData(activeOperation, busy: true));
                return CreateCallToolResult(ToolExecutionResult.FromCommandResult(busyResult));
            }

            var operation = new ActiveToolOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ToolName = tool.Name,
                StartedAtUtc = DateTime.UtcNow.ToString("o"),
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            };
            var progress = new Progress<CommandProgressUpdate>(update => ReportProgress(operation, update));
            lock (_operationLock)
            {
                if (_resetPending)
                {
                    operation.Cancellation.Dispose();
                    _executionGate.Release();
                    var resetPendingResult = CommandResult.CreateFailure(
                        "TIA Portal session reset started before this operation could begin.",
                        CreateOperationStatusData(_activeOperation, busy: true, resetPending: true));
                    return CreateCallToolResult(ToolExecutionResult.FromCommandResult(resetPendingResult));
                }

                _activeOperation = operation;
            }

            _logger.LogInformation(
                "Starting MCP tool operation {OperationId}: {ToolName}",
                operation.OperationId,
                operation.ToolName);

            try
            {
                var executionTask = tool.ExecuteAsync(arguments, operation.Cancellation.Token, progress);
                if (_options.ToolTimeout.HasValue)
                {
                    var delayTask = Task.Delay(_options.ToolTimeout.Value);
                    var completedTask = await Task.WhenAny(executionTask, delayTask);
                    if (completedTask != executionTask)
                    {
                        MarkTimedOut(operation);
                        _ = executionTask.ContinueWith(
                            completed =>
                            {
                                if (completed.IsFaulted)
                                {
                                    _logger.LogError(
                                        completed.Exception,
                                        "Timed-out MCP tool operation {OperationId}: {ToolName} later failed.",
                                        operation.OperationId,
                                        operation.ToolName);
                                }

                                ReleaseOperation(operation);
                            },
                            TaskScheduler.Default);

                        var timeoutResult = CommandResult.CreateFailure(
                            $"MCP tool '{operation.ToolName}' exceeded the configured timeout of {_options.ToolTimeout.Value.TotalSeconds:0.###} seconds. The operation was asked to cancel; use reset-session if the TIA Portal attachment remains busy.",
                            CreateOperationStatusData(operation, timedOut: true, timeoutSeconds: _options.ToolTimeout.Value.TotalSeconds));
                        return CreateCallToolResult(ToolExecutionResult.FromCommandResult(timeoutResult));
                    }
                }

                var result = await executionTask;
                return CreateCallToolResult(result);
            }
            finally
            {
                if (!operation.TimedOut)
                {
                    ReleaseOperation(operation);
                }
            }
        }

        private bool TryHandleBuiltInTool(string toolName, out CallToolResult result)
        {
            if (string.Equals(toolName, "operation-status", StringComparison.OrdinalIgnoreCase))
            {
                ActiveToolOperation? activeOperation;
                lock (_operationLock)
                {
                    activeOperation = _activeOperation;
                }

                result = CreateCallToolResult(ToolExecutionResult.FromCommandResult(CommandResult.CreateSuccess(
                    CreateOperationStatusData(activeOperation),
                    activeOperation == null ? "No MCP tool operation is active." : $"MCP tool '{activeOperation.ToolName}' is active.")));
                return true;
            }

            if (string.Equals(toolName, "cancel-operation", StringComparison.OrdinalIgnoreCase))
            {
                ActiveToolOperation? activeOperation;
                lock (_operationLock)
                {
                    activeOperation = _activeOperation;
                    activeOperation?.Cancellation.Cancel();
                    if (activeOperation != null)
                    {
                        activeOperation.CancelRequested = true;
                        activeOperation.CancelRequestedAtUtc = DateTime.UtcNow.ToString("o");
                    }
                }

                result = CreateCallToolResult(ToolExecutionResult.FromCommandResult(CommandResult.CreateSuccess(
                    CreateOperationStatusData(activeOperation, cancelRequested: activeOperation != null),
                    activeOperation == null ? "No MCP tool operation is active." : $"Cancellation requested for MCP tool '{activeOperation.ToolName}'.")));
                return true;
            }

            result = null!;
            return false;
        }

        private async Task<CommandResult> ResetSessionAsync(CancellationToken cancellationToken)
        {
            ActiveToolOperation? activeOperation;

            lock (_operationLock)
            {
                if (_resetPending)
                {
                    return CommandResult.CreateFailure(
                        "A TIA Portal session reset is already pending.",
                        CreateOperationStatusData(_activeOperation, busy: true, resetPending: true));
                }

                _resetPending = true;
                activeOperation = _activeOperation;
                if (activeOperation != null)
                {
                    activeOperation.CancelRequested = true;
                    activeOperation.CancelRequestedAtUtc = DateTime.UtcNow.ToString("o");
                    activeOperation.ResetRequested = true;
                    activeOperation.Cancellation.Cancel();
                }
            }

            try
            {
                if (activeOperation != null)
                {
                    var graceDelay = Task.Delay(_options.SessionResetGracePeriod, cancellationToken);
                    var completed = await Task.WhenAny(activeOperation.Completion.Task, graceDelay).ConfigureAwait(false);
                    if (completed != activeOperation.Completion.Task)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return CommandResult.CreateFailure(
                            $"The active MCP operation did not become quiescent within {_options.SessionResetGracePeriod.TotalSeconds:0.###} seconds. The TIA session was not disposed. Terminate and restart the MCP process to recover safely.",
                            CreateOperationStatusData(activeOperation, busy: true, resetPending: true, resetRefused: true));
                    }

                    await activeOperation.Completion.Task.ConfigureAwait(false);
                }

                await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var sessionReset = _sessionResetService == null
                        ? null
                        : await _sessionResetService.ResetAsync().ConfigureAwait(false);

                    return CommandResult.CreateSuccess(
                        new
                        {
                            reset = true,
                            waitedForOperation = activeOperation != null,
                            canceledOperationId = activeOperation?.OperationId,
                            canceledToolName = activeOperation?.ToolName,
                            session = sessionReset
                        },
                        "MCP session reset completed after the execution gate became quiescent.");
                }
                finally
                {
                    _executionGate.Release();
                }
            }
            finally
            {
                lock (_operationLock)
                {
                    _resetPending = false;
                }
            }
        }

        private static CallToolResult CreateCallToolResult(ToolExecutionResult result)
        {
            return new CallToolResult
            {
                IsError = result.IsError,
                StructuredContent = result.StructuredContent,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock
                    {
                        Text = result.Text
                    }
                }
            };
        }

        private void MarkTimedOut(ActiveToolOperation operation)
        {
            lock (_operationLock)
            {
                operation.TimedOut = true;
                operation.TimedOutAtUtc = DateTime.UtcNow.ToString("o");
                operation.Cancellation.Cancel();
            }

            _logger.LogWarning(
                "MCP tool operation {OperationId}: {ToolName} exceeded the configured timeout.",
                operation.OperationId,
                operation.ToolName);
        }

        private void ReleaseOperation(ActiveToolOperation operation)
        {
            var shouldRelease = false;
            lock (_operationLock)
            {
                if (ReferenceEquals(_activeOperation, operation))
                {
                    _activeOperation = null;
                    shouldRelease = true;
                }
            }

            operation.Cancellation.Dispose();

            _logger.LogInformation(
                "Finished MCP tool operation {OperationId}: {ToolName}",
                operation.OperationId,
                operation.ToolName);

            if (shouldRelease)
            {
                _executionGate.Release();
            }

            operation.Completion.TrySetResult(null);
        }

        private void ReportProgress(ActiveToolOperation operation, CommandProgressUpdate update)
        {
            lock (_operationLock)
            {
                if (!ReferenceEquals(_activeOperation, operation))
                {
                    return;
                }

                operation.LatestProgress = new OperationProgressSnapshot
                {
                    Message = update.Message,
                    Percentage = update.Percentage,
                    Stage = update.Stage,
                    ReportedAtUtc = DateTime.UtcNow.ToString("o")
                };
                operation.ProgressCount++;
            }
        }

        private static object CreateOperationStatusData(
            ActiveToolOperation? operation,
            bool busy = false,
            bool timedOut = false,
            double? timeoutSeconds = null,
            bool cancelRequested = false,
            bool resetPending = false,
            bool resetRefused = false)
        {
            return new
            {
                busy,
                active = operation != null,
                timedOut = timedOut || operation?.TimedOut == true,
                cancelRequested = cancelRequested || operation?.CancelRequested == true,
                resetPending,
                resetRefused,
                activeOperationId = operation?.OperationId,
                activeToolName = operation?.ToolName,
                activeStartedAtUtc = operation?.StartedAtUtc,
                timedOutAtUtc = operation?.TimedOutAtUtc,
                cancelRequestedAtUtc = operation?.CancelRequestedAtUtc,
                timeoutSeconds,
                progress = operation?.LatestProgress,
                progressCount = operation?.ProgressCount ?? 0
            };
        }

        private static Tool CreateBuiltInTool(string name, string description, Dictionary<string, object?> properties)
        {
            var schema = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = "object",
                ["additionalProperties"] = false
            };

            if (properties.Count > 0)
            {
                schema["properties"] = properties;
            }

            return new Tool
            {
                Name = name,
                Description = description,
                InputSchema = JsonSerializer.SerializeToElement(schema)
            };
        }

        private sealed class ActiveToolOperation
        {
            public string OperationId { get; set; } = string.Empty;

            public string ToolName { get; set; } = string.Empty;

            public string StartedAtUtc { get; set; } = string.Empty;

            public bool TimedOut { get; set; }

            public string? TimedOutAtUtc { get; set; }

            public bool CancelRequested { get; set; }

            public string? CancelRequestedAtUtc { get; set; }

            public bool ResetRequested { get; set; }

            public CancellationTokenSource Cancellation { get; set; } = new CancellationTokenSource();

            public TaskCompletionSource<object?> Completion { get; } =
                new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            public OperationProgressSnapshot? LatestProgress { get; set; }

            public int ProgressCount { get; set; }
        }

        private sealed class OperationProgressSnapshot
        {
            public string Message { get; set; } = string.Empty;

            public double? Percentage { get; set; }

            public string? Stage { get; set; }

            public string ReportedAtUtc { get; set; } = string.Empty;
        }
    }
}
