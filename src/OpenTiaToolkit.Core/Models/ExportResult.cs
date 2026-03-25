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
}
