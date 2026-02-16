using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace OpenTiaToolkit.MCP.Server
{
    /// <summary>
    /// Simple STDIO-based MCP server host.
    /// Implements the Model Context Protocol over stdin/stdout.
    /// </summary>
    public class McpServerHost
    {
        private readonly ToolRegistry _toolRegistry;
        private readonly ILogger<McpServerHost> _logger;
        private readonly TextReader _stdin;
        private readonly TextWriter _stdout;

        /// <summary>
        /// Initializes a new instance of the <see cref="McpServerHost"/> class.
        /// </summary>
        /// <param name="toolRegistry">The tool registry.</param>
        /// <param name="logger">Logger instance.</param>
        public McpServerHost(ToolRegistry toolRegistry, ILogger<McpServerHost> logger)
            : this(toolRegistry, logger, Console.In, Console.Out)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="McpServerHost"/> class with custom I/O.
        /// </summary>
        /// <param name="toolRegistry">The tool registry.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="stdin">Input stream.</param>
        /// <param name="stdout">Output stream.</param>
        public McpServerHost(ToolRegistry toolRegistry, ILogger<McpServerHost> logger, TextReader stdin, TextWriter stdout)
        {
            _toolRegistry = toolRegistry;
            _logger = logger;
            _stdin = stdin;
            _stdout = stdout;
        }

        /// <summary>
        /// Starts the MCP server and begins processing messages.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Exit code.</returns>
        public async Task<int> StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting MCP server...");

            try
            {
                // Process messages from stdin
                while (!cancellationToken.IsCancellationRequested && _stdin.Peek() != -1)
                {
                    try
                    {
                        // Read a line (JSON-RPC message)
                        var line = await _stdin.ReadLineAsync();

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        _logger.LogDebug("Received: {Message}", line);

                        // Process the message and send response
                        var response = await ProcessMessageAsync(line);

                        if (!string.IsNullOrEmpty(response))
                        {
                            await _stdout.WriteLineAsync(response);
                            await _stdout.FlushAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message");

                        // Send error response
                        var errorResponse = CreateErrorResponse("internal_error", ex.Message);
                        await _stdout.WriteLineAsync(errorResponse);
                        await _stdout.FlushAsync();
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Fatal error in MCP server");
                return 1;
            }
        }

        /// <summary>
        /// Processes an incoming MCP message.
        /// </summary>
        /// <param name="message">The JSON-RPC message.</param>
        /// <returns>JSON-RPC response.</returns>
        private async Task<string> ProcessMessageAsync(string message)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;

                var methodName = root.TryGetProperty("method", out var methodProp)
                    ? methodProp.GetString() ?? string.Empty
                    : string.Empty;

                var id = root.TryGetProperty("id", out var idProp)
                    ? idProp.GetInt32()
                    : 0;

                // Handle different methods
                return methodName switch
                {
                    "initialize" => HandleInitialize(id),
                    "tools/list" => HandleListTools(id),
                    "tools/call" => await HandleToolCall(root, id),
                    "shutdown" => HandleShutdown(id),
                    _ => CreateErrorResponse("method_not_found", $"Unknown method: {methodName}", id)
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error parsing JSON message");
                return CreateErrorResponse("parse_error", "Invalid JSON", null);
            }
        }

        /// <summary>
        /// Handles the initialize method.
        /// </summary>
        private string HandleInitialize(int id)
        {
            _logger.LogInformation("Handling initialize request");

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    serverInfo = new
                    {
                        name = "OpenTiaToolkit",
                        version = "1.0.0"
                    },
                    capabilities = new
                    {
                        tools = new { }
                    }
                }
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// Handles the tools/list method.
        /// </summary>
        private string HandleListTools(int id)
        {
            _logger.LogInformation("Handling tools/list request");

            var tools = _toolRegistry.ListTools();

            var toolList = new List<object>();
            foreach (var tool in tools)
            {
                toolList.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = JsonSerializer.Deserialize<object>(tool.InputSchema)
                });
            }

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    tools = toolList
                }
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// Handles the tools/call method.
        /// </summary>
        private async Task<string> HandleToolCall(JsonElement root, int id)
        {
            if (!root.TryGetProperty("params", out var paramsProp))
            {
                return CreateErrorResponse("invalid_params", "Missing params property", id);
            }

            var name = paramsProp.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? string.Empty
                : string.Empty;

            var arguments = paramsProp.TryGetProperty("arguments", out var argsProp)
                ? argsProp.GetRawText() ?? "{}"
                : "{}";

            _logger.LogInformation("Handling tools/call request: {ToolName}", name);

            var tool = _toolRegistry.GetTool(name);

            if (tool == null)
            {
                return CreateErrorResponse("tool_not_found", $"Tool not found: {name}", id);
            }

            try
            {
                var result = await tool.ExecuteAsync(arguments);

                // Parse the result JSON to extract the data
                using var resultDoc = JsonDocument.Parse(result);

                var response = new
                {
                    jsonrpc = "2.0",
                    id = id,
                    result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = result
                            }
                        }
                    }
                };

                return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool: {ToolName}", name);
                return CreateErrorResponse("tool_error", ex.Message, id);
            }
        }

        /// <summary>
        /// Handles the shutdown method.
        /// </summary>
        private string HandleShutdown(int id)
        {
            _logger.LogInformation("Handling shutdown request");

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new { }
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// Creates an error response.
        /// </summary>
        private string CreateErrorResponse(string code, string message, int? id = null)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                error = new
                {
                    code = code,
                    message = message
                }
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
        }
    }
}
