using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Utilities
{
    internal static class TagModificationInputParser
    {
        internal static IReadOnlyList<TagModificationOperation> ParseInline(object raw)
        {
            if (raw == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            var json = raw is JsonElement element ? element.GetRawText() : raw.ToString();
            return ParseJson(json ?? string.Empty);
        }

        internal static IReadOnlyList<TagModificationOperation> ParseFile(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException($"Tag modification input file not found: {inputPath}", inputPath);
            }

            var extension = Path.GetExtension(inputPath);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return ParseJson(File.ReadAllText(inputPath));
            }

            if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCsv(inputPath);
            }

            throw new ArgumentException("Tag modification input must be a .json or .csv file.", nameof(inputPath));
        }

        internal static IReadOnlyList<TagModificationOperation> ParseJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var operationsElement = root.ValueKind == JsonValueKind.Object &&
                                    TryGetProperty(root, "operations", out var nestedOperations)
                ? nestedOperations
                : root;

            if (operationsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("JSON input must be an operation array or an object containing an operations array.");
            }

            var operations = new List<TagModificationOperation>();
            foreach (var item in operationsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("Each tag modification operation must be a JSON object.");
                }

                operations.Add(ParseJsonOperation(item));
            }

            EnsureNotEmpty(operations);
            return operations;
        }

        internal static IReadOnlyList<TagModificationOperation> ParseCsv(string inputPath)
        {
            var lines = ReadAllLinesWithEncodingFallback(inputPath);
            if (lines.Length == 0)
            {
                throw new ArgumentException($"CSV file is empty: {inputPath}");
            }

            var delimiter = DetectDelimiter(lines);
            var headers = ParseCsvLine(lines[0], delimiter);
            var columns = headers
                .Select((name, index) => new { Name = name.Trim(), Index = index })
                .Where(item => item.Name.Length > 0)
                .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

            if (!columns.ContainsKey("Action"))
            {
                throw new ArgumentException("CSV input must contain an Action column.");
            }

            var operations = new List<TagModificationOperation>();
            for (var index = 1; index < lines.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    continue;
                }

                var values = ParseCsvLine(lines[index], delimiter);
                var operation = new TagModificationOperation
                {
                    Action = GetCsvValue(values, columns, "Action") ?? string.Empty,
                    SourceLine = index + 1,
                    Match = new TagMatchCriteria
                    {
                        TagTable = GetCsvValue(values, columns, "MatchTagTable"),
                        Name = GetCsvValue(values, columns, "MatchName"),
                        LogicalAddress = NormalizeAddress(GetCsvValue(values, columns, "MatchLogicalAddress"))
                    }
                };

                SetCsvPatchValue(values, columns, "TagTable", value =>
                {
                    operation.Set.HasTagTable = true;
                    operation.Set.TagTable = value;
                });
                SetCsvPatchValue(values, columns, "Name", value =>
                {
                    operation.Set.HasName = true;
                    operation.Set.Name = value;
                });
                SetCsvPatchValue(values, columns, "DataType", value =>
                {
                    operation.Set.HasDataType = true;
                    operation.Set.DataType = value;
                });
                SetCsvPatchValue(values, columns, "LogicalAddress", value =>
                {
                    operation.Set.HasLogicalAddress = true;
                    operation.Set.LogicalAddress = NormalizeAddress(value) ?? string.Empty;
                });
                SetCsvPatchValue(values, columns, "Comment", value =>
                {
                    operation.Set.HasComment = true;
                    operation.Set.Comment = value;
                });

                if (GetCsvBool(values, columns, "ClearComment"))
                {
                    operation.Set.HasComment = true;
                    operation.Set.Comment = string.Empty;
                }

                operations.Add(operation);
            }

            EnsureNotEmpty(operations);
            return operations;
        }

        private static TagModificationOperation ParseJsonOperation(JsonElement item)
        {
            var operation = new TagModificationOperation
            {
                Action = GetString(item, "action") ?? string.Empty
            };

            if (TryGetProperty(item, "match", out var match) && match.ValueKind == JsonValueKind.Object)
            {
                operation.Match.TagTable = GetString(match, "tagTable");
                operation.Match.Name = GetString(match, "name");
                operation.Match.LogicalAddress = NormalizeAddress(GetString(match, "logicalAddress"));
            }

            if (TryGetProperty(item, "set", out var set) && set.ValueKind == JsonValueKind.Object)
            {
                ReadPatchProperty(set, "tagTable", value => operation.Set.TagTable = value, () => operation.Set.HasTagTable = true);
                ReadPatchProperty(set, "name", value => operation.Set.Name = value, () => operation.Set.HasName = true);
                ReadPatchProperty(set, "dataType", value => operation.Set.DataType = value, () => operation.Set.HasDataType = true);
                ReadPatchProperty(
                    set,
                    "logicalAddress",
                    value => operation.Set.LogicalAddress = NormalizeAddress(value) ?? string.Empty,
                    () => operation.Set.HasLogicalAddress = true);
                ReadPatchProperty(set, "comment", value => operation.Set.Comment = value ?? string.Empty, () => operation.Set.HasComment = true);
            }

            return operation;
        }

        private static void ReadPatchProperty(
            JsonElement source,
            string name,
            Action<string?> assign,
            Action markPresent)
        {
            if (!TryGetProperty(source, name, out var value))
            {
                return;
            }

            markPresent();
            assign(value.ValueKind == JsonValueKind.Null ? null : value.ToString());
        }

        private static string? GetString(JsonElement source, string name)
        {
            return TryGetProperty(source, name, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.ToString()
                : null;
        }

        private static bool TryGetProperty(JsonElement source, string name, out JsonElement value)
        {
            foreach (var property in source.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? NormalizeAddress(string? value)
        {
            return value == null ? null : TagUpdateCsvParser.NormalizeLogicalAddress(value);
        }

        private static string? GetCsvValue(
            string[] values,
            IReadOnlyDictionary<string, int> columns,
            string columnName)
        {
            if (!columns.TryGetValue(columnName, out var index) || index >= values.Length)
            {
                return null;
            }

            var value = values[index].Trim();
            return value.Length == 0 ? null : value;
        }

        private static void SetCsvPatchValue(
            string[] values,
            IReadOnlyDictionary<string, int> columns,
            string columnName,
            Action<string> assign)
        {
            var value = GetCsvValue(values, columns, columnName);
            if (value != null)
            {
                assign(value);
            }
        }

        private static bool GetCsvBool(
            string[] values,
            IReadOnlyDictionary<string, int> columns,
            string columnName)
        {
            var value = GetCsvValue(values, columns, columnName);
            return bool.TryParse(value, out var parsed) && parsed;
        }

        private static void EnsureNotEmpty(IReadOnlyCollection<TagModificationOperation> operations)
        {
            if (operations.Count == 0)
            {
                throw new ArgumentException("Tag modification input must contain at least one operation.");
            }
        }

        private static string[] ReadAllLinesWithEncodingFallback(string inputPath)
        {
            try
            {
                using var reader = new StreamReader(
                    inputPath,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                    detectEncodingFromByteOrderMarks: true);
                return SplitLines(reader.ReadToEnd());
            }
            catch (DecoderFallbackException)
            {
                return File.ReadAllLines(inputPath, Encoding.Default);
            }
        }

        private static string[] SplitLines(string content)
        {
            return content
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static string[] ParseCsvLine(string line, char delimiter)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (character == delimiter && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            values.Add(current.ToString());
            return values.ToArray();
        }

        private static char DetectDelimiter(IEnumerable<string> lines)
        {
            var commaCount = 0;
            var semicolonCount = 0;
            foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(10))
            {
                var inQuotes = false;
                foreach (var character in line)
                {
                    if (character == '"')
                    {
                        inQuotes = !inQuotes;
                    }
                    else if (!inQuotes && character == ',')
                    {
                        commaCount++;
                    }
                    else if (!inQuotes && character == ';')
                    {
                        semicolonCount++;
                    }
                }
            }

            return semicolonCount > commaCount ? ';' : ',';
        }
    }
}
