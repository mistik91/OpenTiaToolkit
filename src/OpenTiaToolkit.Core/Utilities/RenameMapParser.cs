using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            var working = jsonString.Trim();

            // Check if it's a CSV file path (before any quote stripping)
            if (working.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCsvFile(working);
            }

            // Handle CLI input which may include outer quotes or escaped quotes
            // Examples of possible input formats:
            // - "{\"M100\":\"M200\"}"  -> {"M100":"M200"}
            // - "{\\\"M100\\\":\\\"M200\\\"}" -> {"M100":"M200"}
            // - {"M100":"M200"}  -> {"M100":"M200"}
            // - {M100:M200}  -> {"M100":"M200"} (quotes stripped by REPL parser)

            // Remove outer single quotes if present (from CLI input)
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

                // Check if it's a CSV file (must end with .csv)
                if (strValue.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseCsvFile(strValue);
                }

                // Check if it looks like a file path (contains path separator but not CSV)
                if (strValue.Contains("/") || strValue.Contains("\\"))
                {
                    throw new NotSupportedException(
                        "File paths are only supported for .csv files. Please provide a CSV file or JSON string."
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

        /// <summary>
        /// Parses a CSV file containing rename mappings.
        /// CSV format: OldName,NewName (header optional)
        /// </summary>
        /// <param name="filePath">Path to the CSV file.</param>
        /// <returns>A dictionary mapping old names to new names (case-insensitive).</returns>
        /// <exception cref="FileNotFoundException">Thrown when the CSV file doesn't exist.</exception>
        /// <exception cref="ArgumentException">Thrown when the CSV format is invalid.</exception>
        private static Dictionary<string, string> ParseCsvFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    $"CSV file not found: {filePath}",
                    filePath
                );
            }

            var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines;

            try
            {
                lines = File.ReadLines(filePath).ToArray();
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Failed to read CSV file: {filePath}. Error: {ex.Message}",
                    nameof(filePath),
                    ex
                );
            }

            if (lines.Length == 0)
            {
                throw new ArgumentException(
                    $"CSV file is empty: {filePath}"
                );
            }

            // Determine if first row is a header by checking for header keywords
            bool hasHeader = IsHeaderRow(lines[0]);

            // Start parsing from the appropriate line
            int startIndex = hasHeader ? 1 : 0;

            for (int i = startIndex; i < lines.Length; i++)
            {
                var lineNum = i + 1;
                var line = lines[i];

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Split by comma (simple CSV - no quoted commas support)
                var columns = line.Split(',');

                if (columns.Length != 2)
                {
                    throw new ArgumentException(
                        $"Invalid CSV format at line {lineNum}. Expected 2 columns, found {columns.Length}. Line content: '{line.Trim()}'"
                    );
                }

                var oldName = columns[0].Trim();
                var newName = columns[1].Trim();

                // Skip empty entries
                if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                {
                    continue;
                }

                // Add to dictionary (last occurrence wins for duplicates)
                renameMap[oldName] = newName;
            }

            if (renameMap.Count == 0)
            {
                throw new ArgumentException(
                    $"CSV file contains no valid rename mappings: {filePath}"
                );
            }

            return renameMap;
        }

        /// <summary>
        /// Determines if a row is a header row by checking for common header keywords.
        /// </summary>
        /// <param name="row">The row to check.</param>
        /// <returns>True if the row appears to be a header.</returns>
        private static bool IsHeaderRow(string row)
        {
            if (string.IsNullOrWhiteSpace(row))
            {
                return false;
            }

            var columns = row.Split(',');
            if (columns.Length < 2)
            {
                return false;
            }

            var col1 = columns[0].Trim().ToLowerInvariant();
            var col2 = columns[1].Trim().ToLowerInvariant();

            // Check for common header keywords in first column (old name indicators)
            var oldNameKeywords = new[] { "old", "current", "actual", "existing", "from" };
            // Check for common header keywords in second column (new name indicators)
            var newNameKeywords = new[] { "new", "modified", "renamed", "to", "target" };

            bool firstColIsHeader = oldNameKeywords.Any(keyword => col1.Contains(keyword));
            bool secondColIsHeader = newNameKeywords.Any(keyword => col2.Contains(keyword));

            // Consider it a header if at least one column contains header keywords
            return firstColIsHeader || secondColIsHeader;
        }
    }
}
