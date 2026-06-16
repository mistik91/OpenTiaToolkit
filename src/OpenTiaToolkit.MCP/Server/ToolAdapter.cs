using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.MCP.Server
{
    /// <summary>
    /// Adapter that converts ICommand implementations to MCP tool delegates.
    /// </summary>
    public class ToolAdapter
    {
        private readonly ICommand _command;
        private readonly ILogger _logger;
        private readonly Tool _protocolTool;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolAdapter"/> class.
        /// </summary>
        /// <param name="command">The command to adapt.</param>
        /// <param name="logger">Logger instance.</param>
        public ToolAdapter(ICommand command, ILogger logger)
        {
            _command = command;
            _logger = logger;
            _protocolTool = new Tool
            {
                Name = _command.Name,
                Description = _command.Description,
                InputSchema = JsonSerializer.SerializeToElement(GetInputSchema())
            };
        }

        /// <summary>
        /// Gets the tool name (from the command).
        /// </summary>
        public string Name => _command.Name;

        /// <summary>
        /// Gets the tool description (from the command).
        /// </summary>
        public string Description => _command.Description;

        /// <summary>
        /// Gets the protocol tool descriptor.
        /// </summary>
        public Tool ProtocolTool => _protocolTool;

        /// <summary>
        /// Gets the JSON schema for the tool's input arguments.
        /// </summary>
        public object GetInputSchema()
        {
            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var required = new List<string>();

            foreach (var param in _command.Schema.Parameters)
            {
                properties[param.Name] = BuildParameterSchema(param);

                if (param.Required)
                {
                    required.Add(param.Name);
                }
            }

            var schema = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = "object",
                ["additionalProperties"] = false
            };

            if (properties.Count > 0)
            {
                schema["properties"] = properties;
            }

            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        /// <summary>
        /// Executes the command with the provided arguments.
        /// </summary>
        /// <param name="arguments">JSON object containing the arguments.</param>
        /// <returns>Structured MCP tool result.</returns>
        public async Task<ToolExecutionResult> ExecuteAsync(
            IReadOnlyDictionary<string, JsonElement>? arguments,
            CancellationToken cancellationToken = default,
            IProgress<CommandProgressUpdate>? progress = null)
        {
            _logger.LogInformation("MCP: Executing tool {ToolName}", Name);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var args = ParseArguments(arguments);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CommandProgressUpdate($"Starting {Name}.", 0, "starting"));
                var context = new CommandExecutionContext(cancellationToken, progress);
                var result = _command is IContextualCommand contextualCommand
                    ? await contextualCommand.ExecuteAsync(args, _logger, context)
                    : await _command.ExecuteAsync(args, _logger);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CommandProgressUpdate($"Completed {Name}.", 100, "completed"));
                return ToolExecutionResult.FromCommandResult(result, Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP: Error executing tool {ToolName}", Name);

                var errorResult = CommandResult.CreateFailure(
                    $"Error executing {Name}: {ex.Message}",
                    ex
                );

                return ToolExecutionResult.FromCommandResult(errorResult, Name);
            }
        }

        /// <summary>
        /// Parses JSON arguments into CommandArgs while preserving JSON-native types.
        /// </summary>
        private CommandArgs ParseArguments(IReadOnlyDictionary<string, JsonElement>? arguments)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (arguments == null)
            {
                return new CommandArgs(values);
            }

            foreach (var argument in arguments)
            {
                values[argument.Key] = argument.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => argument.Value.Clone()
                };
            }

            return new CommandArgs(values);
        }

        private object BuildParameterSchema(CommandParameter parameter)
        {
            var schema = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["description"] = parameter.Description
            };

            switch (parameter.Type)
            {
                case CommandParameterType.Integer:
                    schema["type"] = "integer";
                    break;

                case CommandParameterType.Boolean:
                    schema["type"] = "boolean";
                    break;

                case CommandParameterType.StringArray:
                    schema["type"] = "array";
                    schema["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string"
                    };
                    break;

                case CommandParameterType.ObjectArray:
                    schema["type"] = "array";
                    schema["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true
                    };
                    break;

                case CommandParameterType.Object:
                    schema["type"] = "object";
                    schema["additionalProperties"] = true;
                    break;

                case CommandParameterType.Path:
                case CommandParameterType.String:
                default:
                    schema["type"] = "string";
                    break;
            }

            if (parameter.DefaultValue != null)
            {
                schema["default"] = parameter.DefaultValue;
            }

            return schema;
        }
    }

    /// <summary>
    /// Structured result returned from an adapted MCP tool call.
    /// </summary>
    public sealed class ToolExecutionResult
    {
        private static readonly JsonSerializerOptions TextSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions NodeSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ToolExecutionResult(bool isError, JsonNode structuredContent, string text)
        {
            IsError = isError;
            StructuredContent = structuredContent;
            Text = text;
        }

        public bool IsError { get; }

        public JsonNode StructuredContent { get; }

        public string Text { get; }

        public static ToolExecutionResult FromCommandResult(CommandResult result)
        {
            return FromCommandResult(result, toolName: null);
        }

        public static ToolExecutionResult FromCommandResult(CommandResult result, string? toolName)
        {
            object? data = result.Data;
            var message = result.Message;
            var structuredContent = CreateStructuredContent(result, message, data);
            var text = SerializeStructuredContent(structuredContent);

            if (ShouldSpillToJsonArtifact(result, data, text))
            {
                var artifactPath = CommandArtifactSupport.CreateDefaultArtifactPath(toolName ?? "mcp-tool-result", ".json");
                var bytesWritten = CommandArtifactSupport.SaveJsonArtifact(data!, artifactPath);
                data = new CommandArtifactResult
                {
                    OutputPath = artifactPath,
                    Format = "json",
                    DataKind = string.IsNullOrWhiteSpace(toolName) ? "command-result" : toolName + "-result",
                    BytesWritten = bytesWritten,
                    ItemCount = TryGetItemCount(result.Data),
                    InlineOutputSuppressed = true,
                    Reason = CommandArtifactSupport.InlineResultExceededReason()
                };
                message = string.IsNullOrWhiteSpace(message)
                    ? "Result data was written to an artifact file."
                    : message + " Result data was written to an artifact file.";
                structuredContent = CreateStructuredContent(result, message, data);
                text = SerializeStructuredContent(structuredContent);
            }

            return new ToolExecutionResult(
                isError: !result.Success,
                structuredContent: structuredContent,
                text: text);
        }

        private static JsonNode CreateStructuredContent(CommandResult result, string message, object? data)
        {
            return JsonSerializer.SerializeToNode(new
            {
                success = result.Success,
                message,
                error = result.Error,
                data
            }, NodeSerializerOptions) ?? new JsonObject();
        }

        private static string SerializeStructuredContent(JsonNode structuredContent)
        {
            return structuredContent.ToJsonString(TextSerializerOptions);
        }

        private static bool ShouldSpillToJsonArtifact(CommandResult result, object? data, string text)
        {
            return data != null &&
                   data is not string &&
                   data is not CommandArtifactResult &&
                   Encoding.UTF8.GetByteCount(text) > CommandArtifactSupport.InlineResultThresholdBytes;
        }

        private static int? TryGetItemCount(object? data)
        {
            return data is ICollection collection ? collection.Count : null;
        }
    }
}
