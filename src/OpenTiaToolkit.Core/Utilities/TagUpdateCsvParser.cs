using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTiaToolkit.Core.Utilities
{
    /// <summary>
    /// Represents a single row in the tag update CSV file.
    /// </summary>
    public class TagUpdateRow
    {
        /// <summary>Gets or sets the full tag name (e.g., "243MR100" or "23F1").</summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>Gets or sets the logical address (e.g., "%M0.0" or "MW0").</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Gets or sets the data type (e.g., "Bool", "Word", "Int"). Required for new tags.</summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional tag comment/description.</summary>
        public string? Comment { get; set; }

        /// <summary>Gets or sets the line number in the CSV file for error reporting.</summary>
        public int LineNumber { get; set; }
    }

    /// <summary>
    /// Parser for tag update CSV files.
    /// Handles case-insensitive headers, required column validation, and whitespace trimming.
    /// </summary>
    internal static class TagUpdateCsvParser
    {
        // Required columns (case-insensitive)
        private static readonly string[] RequiredColumns = { "TagName", "Address", "DataType", "Comment" };

        /// <summary>
        /// Parses a CSV file containing tag update information.
        /// </summary>
        /// <param name="csvFilePath">Path to the CSV file.</param>
        /// <returns>List of parsed tag update rows.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the CSV file doesn't exist.</exception>
        /// <exception cref="ArgumentException">Thrown when the CSV format is invalid.</exception>
        internal static List<TagUpdateRow> Parse(string csvFilePath)
        {
            if (!File.Exists(csvFilePath))
            {
                throw new FileNotFoundException(
                    $"CSV file not found: {csvFilePath}",
                    csvFilePath
                );
            }

            string[] lines;
            try
            {
                lines = File.ReadLines(csvFilePath).ToArray();
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Failed to read CSV file: {csvFilePath}. Error: {ex.Message}",
                    nameof(csvFilePath),
                    ex
                );
            }

            if (lines.Length == 0)
            {
                throw new ArgumentException(
                    $"CSV file is empty: {csvFilePath}"
                );
            }

            // Auto-detect CSV delimiter (comma or semicolon)
            var delimiter = DetectDelimiter(lines);

            var result = new List<TagUpdateRow>();

            // Parse header row
            var headerColumns = ParseCsvLine(lines[0], delimiter);
            var columnMap = BuildColumnMap(headerColumns);

            // Validate that we have at least the required columns
            if (columnMap.Count < 3)
            {
                throw new ArgumentException(
                    $"CSV file must contain at least TagName, Address, and DataType columns. " +
                    $"Found columns: {string.Join(", ", headerColumns)}"
                );
            }

            // Start parsing from line 1 (skip header)
            for (int i = 1; i < lines.Length; i++)
            {
                var lineNum = i + 1;
                var line = lines[i];

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var columns = ParseCsvLine(line, delimiter);
                if (columns.Length == 0)
                {
                    continue;
                }

                var row = new TagUpdateRow { LineNumber = lineNum };

                // Extract TagName (required)
                if (columnMap.TryGetValue("TagName", out int tagNameIndex) && tagNameIndex < columns.Length)
                {
                    row.TagName = columns[tagNameIndex].Trim();
                }

                // Extract Address (required)
                if (columnMap.TryGetValue("Address", out int addressIndex) && addressIndex < columns.Length)
                {
                    row.Address = columns[addressIndex].Trim();
                }

                // Extract DataType (required)
                if (columnMap.TryGetValue("DataType", out int dataTypeIndex) && dataTypeIndex < columns.Length)
                {
                    row.DataType = columns[dataTypeIndex].Trim();
                }

                // Extract Comment (optional)
                if (columnMap.TryGetValue("Comment", out int commentIndex) && commentIndex < columns.Length)
                {
                    var commentValue = columns[commentIndex].Trim();
                    row.Comment = string.IsNullOrEmpty(commentValue) ? null : commentValue;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(row.TagName))
                {
                    continue; // Skip rows with empty TagName
                }

                result.Add(row);
            }

            if (result.Count == 0)
            {
                throw new ArgumentException(
                    $"CSV file contains no valid tag update rows: {csvFilePath}"
                );
            }

            return result;
        }

        /// <summary>
        /// Parses a CSV line into columns, handling quoted values.
        /// </summary>
        /// <param name="line">The CSV line to parse.</param>
        /// <param name="delimiter">The CSV delimiter character.</param>
        /// <returns>Array of column values.</returns>
        private static string[] ParseCsvLine(string line, char delimiter)
        {
            var columns = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == delimiter && !inQuotes)
                {
                    columns.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            // Add the last column
            columns.Add(current.ToString());

            return columns.ToArray();
        }

        /// <summary>
        /// Builds a map from column name (case-insensitive) to column index.
        /// </summary>
        /// <param name="headerColumns">The header column names.</param>
        /// <returns>Dictionary mapping column names to indices.</returns>
        private static Dictionary<string, int> BuildColumnMap(string[] headerColumns)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headerColumns.Length; i++)
            {
                var columnName = headerColumns[i].Trim();
                if (!string.IsNullOrEmpty(columnName))
                {
                    map[columnName] = i;
                }
            }

            return map;
        }

        /// <summary>
        /// Auto-detects the CSV delimiter by counting comma and semicolon occurrences.
        /// </summary>
        /// <param name="lines">The CSV file lines.</param>
        /// <returns>The detected delimiter (comma or semicolon).</returns>
        private static char DetectDelimiter(string[] lines)
        {
            int commaCount = 0;
            int semicolonCount = 0;

            // Check first 10 non-empty lines
            foreach (var line in lines.Take(10).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                bool inQuotes = false;
                foreach (char c in line)
                {
                    if (c == '"')
                    {
                        inQuotes = !inQuotes;
                    }
                    else if (!inQuotes)
                    {
                        if (c == ',') commaCount++;
                        if (c == ';') semicolonCount++;
                    }
                }
            }

            // Use semicolon if it's more common, otherwise default to comma
            return semicolonCount > commaCount ? ';' : ',';
        }
    }
}
