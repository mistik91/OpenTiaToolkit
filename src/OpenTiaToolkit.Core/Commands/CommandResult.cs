using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Commands
{
    /// <summary>
    /// Represents the result of a command execution.
    /// Provides unified output formatting for both CLI (tables) and MCP (JSON).
    /// </summary>
    public class CommandResult
    {
        /// <summary>
        /// Gets whether the command executed successfully.
        /// </summary>
        public bool Success { get; private set; }

        /// <summary>
        /// Gets the data returned by the command.
        /// Can be a single value, a collection, or a complex object.
        /// </summary>
        public object? Data { get; private set; }

        /// <summary>
        /// Gets a human-readable message about the command execution.
        /// </summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the error message if the command failed.
        /// </summary>
        public string? Error { get; private set; }

        /// <summary>
        /// Creates a successful command result.
        /// </summary>
        /// <param name="data">The data returned by the command (optional).</param>
        /// <param name="message">Human-readable success message.</param>
        /// <returns>A successful CommandResult.</returns>
        public static CommandResult CreateSuccess(object? data = null, string message = "Command completed successfully")
        {
            return new CommandResult
            {
                Success = true,
                Data = data,
                Message = message,
                Error = null
            };
        }

        /// <summary>
        /// Creates a failed command result.
        /// </summary>
        /// <param name="message">Error message describing the failure.</param>
        /// <param name="exception">Optional exception that caused the failure.</param>
        /// <returns>A failed CommandResult.</returns>
        public static CommandResult CreateFailure(string message, Exception? exception = null)
        {
            return new CommandResult
            {
                Success = false,
                Message = message,
                Error = exception?.Message ?? message,
                Data = null
            };
        }

        /// <summary>
        /// Creates a failed command result while preserving structured data for callers.
        /// </summary>
        /// <param name="message">Error message describing the failure.</param>
        /// <param name="data">Structured data collected before the failure was reported.</param>
        /// <param name="exception">Optional exception that caused the failure.</param>
        /// <returns>A failed CommandResult.</returns>
        public static CommandResult CreateFailure(string message, object? data, Exception? exception = null)
        {
            return new CommandResult
            {
                Success = false,
                Message = message,
                Error = exception?.Message ?? message,
                Data = data
            };
        }

        /// <summary>
        /// Converts the result to a formatted table string for CLI output.
        /// </summary>
        /// <returns>A formatted string representation.</returns>
        public string ToTable()
        {
            if (!Success)
            {
                return "Error: " + Message;
            }

            if (Data == null)
            {
                return Message;
            }

            // Simple formatting for now; richer rendering belongs in the CLI layer.
            // For complex objects, serialize to JSON with indentation
            if (Data is string str)
            {
                return str;
            }

            if (Data is IEnumerable<object> collection)
            {
                var items = collection.ToList();
                if (items.Count == 0)
                    return "No results found.";

                // For simple string arrays, join them
                if (items.All(x => x is string))
                    return string.Join(Environment.NewLine, items.Cast<string>());

                // For complex objects, use JSON
                return ToJson();
            }

            // Single object - serialize to JSON
            return ToJson();
        }

        /// <summary>
        /// Converts the result to a JSON string for MCP output.
        /// </summary>
        /// <param name="indented">Whether to indent the JSON (default: true).</param>
        /// <returns>A JSON string representation.</returns>
        public string ToJson(bool indented = true)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var resultObj = new
            {
                success = Success,
                message = Message,
                error = Error,
                data = Data
            };

            return JsonSerializer.Serialize(resultObj, options);
        }
    }
}
