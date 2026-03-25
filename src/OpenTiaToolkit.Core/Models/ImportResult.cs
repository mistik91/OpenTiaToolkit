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
}
