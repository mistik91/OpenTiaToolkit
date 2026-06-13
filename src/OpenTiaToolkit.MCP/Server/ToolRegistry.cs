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
        private readonly SemaphoreSlim _executionGate = new SemaphoreSlim(1, 1);
        private readonly object _operationLock = new object();
        private ActiveToolOperation? _activeOperation;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
        /// </summary>
        /// <param name="commands">The commands to register as tools.</param>
        /// <param name="logger">Logger instance.</param>
        public ToolRegistry(IEnumerable<ICommand> commands, ILogger<ToolRegistry> logger, McpRuntimeOptions? options = null)
        {
            _logger = logger;
            _options = options ?? new McpRuntimeOptions();
            _tools = new Dictionary<string, ToolAdapter>(StringComparer.OrdinalIgnoreCase);

            foreach (var command in commands)
            {
                var adapter = new ToolAdapter(command, logger);
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
            return _tools.Values
                .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .Select(tool => tool.ProtocolTool)
                .ToList();
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

            var tool = GetTool(toolName!);
            if (tool == null)
            {
                throw new McpException($"Tool not found: {toolName}");
            }

            cancellationToken.ThrowIfCancellationRequested();

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
                    new
                    {
                        busy = true,
                        activeOperationId = activeOperation?.OperationId,
                        activeToolName = activeOperation?.ToolName,
                        activeStartedAtUtc = activeOperation?.StartedAtUtc
                    });
                return CreateCallToolResult(ToolExecutionResult.FromCommandResult(busyResult));
            }

            var operation = new ActiveToolOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ToolName = tool.Name,
                StartedAtUtc = DateTime.UtcNow.ToString("o")
            };
            lock (_operationLock)
            {
                _activeOperation = operation;
            }

            _logger.LogInformation(
                "Starting MCP tool operation {OperationId}: {ToolName}",
                operation.OperationId,
                operation.ToolName);

            try
            {
                // Do not cancel Openness calls through caller cancellation:
                // they must finish before another tool can safely use the same session.
                var executionTask = tool.ExecuteAsync(arguments, CancellationToken.None);
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
                            $"MCP tool '{operation.ToolName}' exceeded the configured timeout of {_options.ToolTimeout.Value.TotalSeconds:0.###} seconds. The underlying TIA Portal operation is still running; later calls remain busy until it finishes.",
                            new
                            {
                                timedOut = true,
                                activeOperationId = operation.OperationId,
                                activeToolName = operation.ToolName,
                                activeStartedAtUtc = operation.StartedAtUtc,
                                timedOutAtUtc = operation.TimedOutAtUtc,
                                timeoutSeconds = _options.ToolTimeout.Value.TotalSeconds
                            });
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
            }

            _logger.LogWarning(
                "MCP tool operation {OperationId}: {ToolName} exceeded the configured timeout.",
                operation.OperationId,
                operation.ToolName);
        }

        private void ReleaseOperation(ActiveToolOperation operation)
        {
            lock (_operationLock)
            {
                if (ReferenceEquals(_activeOperation, operation))
                {
                    _activeOperation = null;
                }
            }

            _logger.LogInformation(
                "Finished MCP tool operation {OperationId}: {ToolName}",
                operation.OperationId,
                operation.ToolName);
            _executionGate.Release();
        }

        private sealed class ActiveToolOperation
        {
            public string OperationId { get; set; } = string.Empty;

            public string ToolName { get; set; } = string.Empty;

            public string StartedAtUtc { get; set; } = string.Empty;

            public bool TimedOut { get; set; }

            public string? TimedOutAtUtc { get; set; }
        }
    }
}
