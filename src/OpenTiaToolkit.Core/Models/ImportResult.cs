using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    /// <summary>
    /// Represents the result of a bulk import operation.
    /// </summary>
    public class ImportResult
    {
        /// <summary>Gets or sets the total number of items processed.</summary>
        public int TotalCount { get; set; }

        /// <summary>Gets or sets the number of items successfully imported.</summary>
        public int SuccessCount { get; set; }

        /// <summary>Gets or sets the number of items that failed to import.</summary>
        public int FailedCount { get; set; }

        /// <summary>Gets or sets the list of successfully imported item names.</summary>
        public List<string> ImportedItems { get; set; } = new();

        /// <summary>Gets or sets the list of failed item names with reasons.</summary>
        public List<string> FailedItems { get; set; } = new();

        /// <summary>Gets or sets the list of error messages encountered during import.</summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Represents the command-level result for PLC bulk import operations.
    /// </summary>
    public class PlcImportCommandResult
    {
        public string? PlcName { get; set; }

        public string? ImportDirectory { get; set; }

        public string? FilterRegex { get; set; }

        public string? GroupPath { get; set; }

        public string? ImportOption { get; set; }

        public int TotalCount { get; set; }

        public int SuccessCount { get; set; }

        public int FailedCount { get; set; }

        public List<string> ImportedItems { get; set; } = new();

        public List<string> FailedItems { get; set; } = new();

        public List<string> Errors { get; set; } = new();

        public static PlcImportCommandResult From(
            string plcName,
            string importDirectory,
            string? filterRegex,
            string? groupPath,
            ImportResult result,
            string? importOption = null)
        {
            return new PlcImportCommandResult
            {
                PlcName = plcName,
                ImportDirectory = importDirectory,
                FilterRegex = filterRegex,
                GroupPath = groupPath,
                ImportOption = importOption,
                TotalCount = result.TotalCount,
                SuccessCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                ImportedItems = result.ImportedItems,
                FailedItems = result.FailedItems,
                Errors = result.Errors
            };
        }
    }
}
