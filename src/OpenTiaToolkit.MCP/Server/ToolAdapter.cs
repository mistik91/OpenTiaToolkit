using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolAdapter"/> class.
        /// </summary>
        /// <param name="command">The command to adapt.</param>
        /// <param name="logger">Logger instance.</param>
        public ToolAdapter(ICommand command, ILogger logger)
        {
            _command = command;
            _logger = logger;
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
        /// Gets the JSON schema for the tool's input arguments.
        /// </summary>
        public string GetInputSchema()
        {
            var properties = new List<string>();
            var required = new List<string>();

            foreach (var param in _command.Schema.Parameters)
            {
                var jsonType = param.Type switch
                {
                    CommandParameterType.String => "string",
                    CommandParameterType.Integer => "number",
                    CommandParameterType.Boolean => "boolean",
                    CommandParameterType.StringArray => "array",
                    CommandParameterType.Path => "string",
                    _ => "string"
                };

                properties.Add($"\"{param.Name}\": {{\"type\": \"{jsonType}\", \"description\": \"{param.Description}\"}}");

                if (param.Required)
                {
                    required.Add($"\"{param.Name}\"");
                }
            }

            var requiredStr = required.Count > 0 ? $"\"required\": [{string.Join(", ", required)}]," : "";

            return $"{{\"type\": \"object\", {requiredStr} \"properties\": {{\n  {string.Join(",\n  ", properties)}\n}}}}";
        }

        /// <summary>
        /// Executes the command with the provided arguments.
        /// </summary>
        /// <param name="arguments">JSON string containing the arguments.</param>
        /// <returns>JSON string containing the result.</returns>
        public async Task<string> ExecuteAsync(string arguments)
        {
            _logger.LogInformation("MCP: Executing tool {ToolName} with arguments: {Arguments}", Name, arguments);

            try
            {
                // Parse arguments from JSON (simplified for Phase 1)
                var args = ParseArguments(arguments);

                // Execute the command
                var result = await _command.ExecuteAsync(args, _logger);

                // Return JSON result
                return result.ToJson(indented: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP: Error executing tool {ToolName}", Name);

                // Return error as JSON
                var errorResult = CommandResult.CreateFailure(
                    $"Error executing {Name}: {ex.Message}",
                    ex
                );

                return errorResult.ToJson(indented: true);
            }
        }

        /// <summary>
        /// Parses JSON arguments into CommandArgs (simplified implementation).
        /// </summary>
        private CommandArgs ParseArguments(string json)
        {
            var values = new Dictionary<string, object?>();

            // For Phase 1, handle simple key=value parsing from JSON-like string
            // A full JSON parser would be better but this works for simple cases
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
            {
                return new CommandArgs(values);
            }

            try
            {
                // Simple parsing: look for "key":"value" or "key":value patterns
                // This is a simplified approach - Phase 2 should use System.Text.Json
                var pattern = "\"(?<key>[^\"]+)\"\\s*:\\s*\"?(?<value>[^\"]+)\"?";
                var matches = System.Text.RegularExpressions.Regex.Matches(json, pattern);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var key = match.Groups["key"].Value;
                    var value = match.Groups["value"].Value;

                    if (!string.IsNullOrEmpty(key))
                    {
                        values[key] = value;
                    }
                }
            }
            catch (Exception)
            {
                // If parsing fails, return empty args
            }

            return new CommandArgs(values);
        }
    }
}
