using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Utilities
{
    /// <summary>
    /// Parser for rename map input from various formats.
    /// Handles JSON strings, dictionaries, and JsonElement inputs.
    /// </summary>
    internal static class RenameMapParser
    {
        /// <summary>
        /// Parses a rename map from various input formats.
        /// </summary>
        /// <param name="renameMap">The rename map input (JSON string, Dictionary, or JsonElement).</param>
        /// <returns>A dictionary mapping old names to new names (case-insensitive).</returns>
        /// <exception cref="ArgumentException">Thrown when the input format is invalid.</exception>
        internal static Dictionary<string, string> Parse(object renameMap)
        {
            if (renameMap == null)
            {
                throw new ArgumentException("Rename map cannot be null.", nameof(renameMap));
            }

            // If it's a string, parse as JSON
            if (renameMap is string str)
            {
                return ParseJsonString(str);
            }

            // If it's already a Dictionary, return a copy
            if (renameMap is Dictionary<string, string> dict)
            {
                return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
            }

            // If it's a JsonElement (from MCP protocol), handle it
            if (renameMap is JsonElement jsonElement)
            {
                return ParseJsonElement(jsonElement);
            }

            throw new ArgumentException(
                $"Rename map must be a JSON string, Dictionary<string, string>, or JsonElement. Got type: {renameMap.GetType().Name}",
                nameof(renameMap)
            );
        }

        /// <summary>
        /// Parses a JSON string into a rename map dictionary.
        /// </summary>
        /// <param name="jsonString">The JSON string to parse.</param>
        /// <returns>A dictionary mapping old names to new names.</returns>
        private static Dictionary<string, string> ParseJsonString(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                throw new ArgumentException("Rename map JSON string cannot be empty.", nameof(jsonString));
            }

            // Handle CLI input which may include outer quotes or escaped quotes
            // Examples of possible input formats:
            // - "{\"M100\":\"M200\"}"  -> {"M100":"M200"}
            // - "{\\\"M100\\\":\\\"M200\\\"}" -> {"M100":"M200"}
            // - {"M100":"M200"}  -> {"M100":"M200"}
            // - {M100:M200}  -> {"M100":"M200"} (quotes stripped by REPL parser)

            var working = jsonString;

            // Remove outer single quotes if present (from CLI input)
            working = working.Trim();
            if (working.StartsWith("'") && working.EndsWith("'") && working.Length > 1)
            {
                working = working.Substring(1, working.Length - 2).Trim();
            }

            // Remove outer double quotes if present
            if (working.StartsWith("\"") && working.EndsWith("\"") && working.Length > 1)
            {
                working = working.Substring(1, working.Length - 2).Trim();
            }

            // Handle case where REPL parser stripped all quotes (e.g., {M100:M200})
            // Try to detect and fix this format
            working = FixStrippedQuotesFormat(working);

            // Check if it looks like JSON (starts with {)
            var trimmed = working.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Rename map must be a JSON object starting with '{{'. Got: {jsonString}",
                    nameof(jsonString)
                );
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(working);
                if (jsonDoc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException(
                        "Rename map JSON must be an object (key-value pairs).",
                        nameof(jsonString)
                    );
                }

                return ParseJsonObject(jsonDoc.RootElement);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    $"Invalid JSON format in rename map: {ex.Message}. Input was: {jsonString}",
                    nameof(jsonString),
                    ex
                );
            }
        }

        /// <summary>
        /// Handles input where quotes have been stripped by the REPL parser.
        /// Converts {M100:M200} to {"M100":"M200"}
        /// </summary>
        private static string FixStrippedQuotesFormat(string input)
        {
            // If the input contains colons but no quotes around keys/values, it's likely stripped
            // Check for pattern like {key:value,key2:value2}
            var trimmed = input.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}"))
            {
                return input;
            }

            // Remove outer braces
            var content = trimmed.Substring(1, trimmed.Length - 2).Trim();

            // If content is empty, return as-is
            if (string.IsNullOrEmpty(content))
            {
                return input;
            }

            // Check if we need to fix (look for unquoted keys/values with colons)
            // Pattern to detect: key:value (no quotes around key or value)
            if (content.Contains(":") && !content.Contains("\""))
            {
                // Need to add quotes around keys and values
                var result = new System.Text.StringBuilder();
                result.Append("{");

                var entries = content.Split(',');
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i].Trim();
                    if (string.IsNullOrEmpty(entry))
                        continue;

                    var colonPos = entry.IndexOf(':');
                    if (colonPos > 0)
                    {
                        var key = entry.Substring(0, colonPos).Trim();
                        var value = entry.Substring(colonPos + 1).Trim();

                        result.Append($"\"{key}\":\"{value}\"");
                        if (i < entries.Length - 1)
                            result.Append(",");
                    }
                }

                result.Append("}");
                return result.ToString();
            }

            return input;
        }

        /// <summary>
        /// Parses a JsonElement into a rename map dictionary.
        /// </summary>
        /// <param name="jsonElement">The JsonElement to parse.</param>
        /// <returns>A dictionary mapping old names to new names.</returns>
        private static Dictionary<string, string> ParseJsonElement(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                // It's a string - could be a file path or JSON string
                var strValue = jsonElement.GetString();
                if (string.IsNullOrWhiteSpace(strValue))
                {
                    throw new ArgumentException("Rename map string cannot be empty.");
                }

                // Check if it looks like a file path (contains path separator or .csv extension)
                if (strValue.Contains("/") || strValue.Contains("\\") ||
                    strValue.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "CSV file paths are not currently supported. Please provide a JSON object or JSON string."
                    );
                }

                // Otherwise, treat as JSON string
                return ParseJsonString(strValue);
            }

            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                return ParseJsonObject(jsonElement);
            }

            throw new ArgumentException(
                $"Rename map JsonElement must be an object or string. Got: {jsonElement.ValueKind}"
            );
        }

        /// <summary>
        /// Parses a JSON object element into a rename map dictionary.
        /// </summary>
        /// <param name="jsonObject">The JSON object element.</param>
        /// <returns>A dictionary mapping old names to new names.</returns>
        private static Dictionary<string, string> ParseJsonObject(JsonElement jsonObject)
        {
            var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in jsonObject.EnumerateObject())
            {
                var oldName = property.Name;
                string? newName = null;

                // Handle different value types
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    newName = property.Value.GetString();
                }
                else if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    continue; // Skip null values
                }
                else
                {
                    // Convert other types to string
                    newName = property.Value.ToString();
                }

                // Skip empty entries
                if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                {
                    continue;
                }

                // Check for duplicates (case-insensitive dictionary will handle this, but we want consistent behavior)
                renameMap[oldName] = newName;
            }

            if (renameMap.Count == 0)
            {
                throw new ArgumentException("Rename map contains no valid mappings.");
            }

            return renameMap;
        }
    }
}
