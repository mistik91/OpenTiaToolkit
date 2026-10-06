using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    /// <summary>
    /// Represents the result of a bulk export operation.
    /// </summary>
    public class ExportResult
    {
        /// <summary>Gets or sets the total number of items processed.</summary>
        public int TotalCount { get; set; }

        /// <summary>Gets or sets the number of items successfully exported.</summary>
        public int SuccessCount { get; set; }

        /// <summary>Gets or sets the number of items that failed to export.</summary>
        public int FailedCount { get; set; }

        /// <summary>Gets or sets the list of successfully exported item names.</summary>
        public List<string> ExportedItems { get; set; } = new();

        /// <summary>Gets or sets the list of failed item names.</summary>
        public List<string> FailedItems { get; set; } = new();

        /// <summary>Gets or sets the list of error messages encountered during export.</summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Represents the command-level result for PLC bulk export operations.
    /// </summary>
    public class PlcExportCommandResult
    {
        public PlcExportSummary Summary { get; set; } = new();

        public List<string> Exported { get; set; } = new();

        public List<string> Failed { get; set; } = new();

        public List<string> Errors { get; set; } = new();

        public static PlcExportCommandResult From(ExportResult result)
        {
            return new PlcExportCommandResult
            {
                Summary = new PlcExportSummary
                {
                    Total = result.TotalCount,
                    Succeeded = result.SuccessCount,
                    Failed = result.FailedCount
                },
                Exported = result.ExportedItems,
                Failed = result.FailedItems,
                Errors = result.Errors
            };
        }
    }

    public class PlcExportSummary
    {
        public int Total { get; set; }

        public int Succeeded { get; set; }

        public int Failed { get; set; }
    }
}
