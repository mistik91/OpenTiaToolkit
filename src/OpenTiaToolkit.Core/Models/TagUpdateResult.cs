using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    /// <summary>
    /// Represents the result of a tag update from CSV operation.
    /// </summary>
    public class TagUpdateResult
    {
        /// <summary>Gets or sets the summary statistics.</summary>
        public TagUpdateSummary Summary { get; set; } = new TagUpdateSummary();

        /// <summary>Gets or sets the list of updated tags.</summary>
        public List<TagUpdateEntry> Updated { get; set; } = new List<TagUpdateEntry>();

        /// <summary>Gets or sets the list of created tags.</summary>
        public List<TagCreateEntry> Created { get; set; } = new List<TagCreateEntry>();

        /// <summary>Gets or sets the list of deleted tags.</summary>
        public List<TagDeleteEntry> Deleted { get; set; } = new List<TagDeleteEntry>();

        /// <summary>Gets or sets the list of warnings.</summary>
        public List<TagUpdateWarning> Warnings { get; set; } = new List<TagUpdateWarning>();

        /// <summary>Gets or sets the list of errors.</summary>
        public List<TagUpdateError> Errors { get; set; } = new List<TagUpdateError>();
    }

    /// <summary>
    /// Typed command payload for PLC tag CSV update operations.
    /// </summary>
    public class TagUpdateCommandResult
    {
        /// <summary>Gets or sets the safety login result, when a safety password was supplied.</summary>
        public SafetyLoginResult? SafetyLogin { get; set; }

        /// <summary>Gets or sets the summary statistics.</summary>
        public TagUpdateSummary Summary { get; set; } = new TagUpdateSummary();

        /// <summary>Gets or sets the list of updated tags.</summary>
        public List<TagUpdateEntry> Updated { get; set; } = new List<TagUpdateEntry>();

        /// <summary>Gets or sets the list of created tags.</summary>
        public List<TagCreateEntry> Created { get; set; } = new List<TagCreateEntry>();

        /// <summary>Gets or sets the list of deleted tags.</summary>
        public List<TagDeleteEntry> Deleted { get; set; } = new List<TagDeleteEntry>();

        /// <summary>Gets or sets the list of warnings.</summary>
        public List<TagUpdateWarning> Warnings { get; set; } = new List<TagUpdateWarning>();

        /// <summary>Gets or sets the list of errors.</summary>
        public List<TagUpdateError> Errors { get; set; } = new List<TagUpdateError>();

        /// <summary>
        /// Creates a command payload from the service result without anonymous wrappers.
        /// </summary>
        public static TagUpdateCommandResult From(TagUpdateResult result, SafetyLoginResult? safetyLogin = null)
        {
            return new TagUpdateCommandResult
            {
                SafetyLogin = safetyLogin,
                Summary = result.Summary,
                Updated = result.Updated,
                Created = result.Created,
                Deleted = result.Deleted,
                Warnings = result.Warnings,
                Errors = result.Errors
            };
        }
    }

    /// <summary>
    /// Summary statistics for tag update operations.
    /// </summary>
    public class TagUpdateSummary
    {
        /// <summary>Gets or sets the total number of CSV rows processed.</summary>
        public int TotalCsvRows { get; set; }

        /// <summary>Gets or sets the number of existing tags scanned.</summary>
        public int ExistingTagsScanned { get; set; }

        /// <summary>Gets or sets the number of tags updated.</summary>
        public int TagsUpdated { get; set; }

        /// <summary>Gets or sets the number of tags created.</summary>
        public int TagsCreated { get; set; }

        /// <summary>Gets or sets the number of tags deleted.</summary>
        public int TagsDeleted { get; set; }

        /// <summary>Gets or sets the number of tags skipped.</summary>
        public int TagsSkipped { get; set; }
    }

    /// <summary>
    /// Represents a single tag update entry.
    /// </summary>
    public class TagUpdateEntry
    {
        /// <summary>Gets or sets the tag table name where the tag resides.</summary>
        public string TagTableName { get; set; } = string.Empty;

        /// <summary>Gets or sets how the tag was matched ("FullName" or "BaseName").</summary>
        public string MatchedBy { get; set; } = string.Empty;

        /// <summary>Gets or sets the old tag name.</summary>
        public string OldName { get; set; } = string.Empty;

        /// <summary>Gets or sets the new tag name.</summary>
        public string NewName { get; set; } = string.Empty;

        /// <summary>Gets or sets the old logical address.</summary>
        public string? OldAddress { get; set; }

        /// <summary>Gets or sets the new logical address.</summary>
        public string NewAddress { get; set; } = string.Empty;

        /// <summary>Gets or sets the old comment.</summary>
        public string? OldComment { get; set; }

        /// <summary>Gets or sets the new comment.</summary>
        public string? NewComment { get; set; }
    }

    /// <summary>
    /// Represents a single tag creation entry.
    /// </summary>
    public class TagCreateEntry
    {
        /// <summary>Gets or sets the tag table name where the tag was created.</summary>
        public string TagTableName { get; set; } = string.Empty;

        /// <summary>Gets or sets the tag name.</summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>Gets or sets the data type.</summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>Gets or sets the logical address.</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Gets or sets the comment.</summary>
        public string? Comment { get; set; }
    }

    /// <summary>
    /// Represents a single tag deletion entry.
    /// </summary>
    public class TagDeleteEntry
    {
        /// <summary>Gets or sets the tag table name where the tag was deleted.</summary>
        public string TagTableName { get; set; } = string.Empty;

        /// <summary>Gets or sets the deleted tag name.</summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>Gets or sets the deleted tag logical address.</summary>
        public string? Address { get; set; }

        /// <summary>Gets or sets the reason for deleting the tag.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a warning during tag update operation.
    /// </summary>
    public class TagUpdateWarning
    {
        /// <summary>Gets or sets the item that generated the warning.</summary>
        public string Item { get; set; } = string.Empty;

        /// <summary>Gets or sets the reason for the warning.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Gets or sets the CSV line number (if applicable).</summary>
        public int? LineNumber { get; set; }
    }

    /// <summary>
    /// Represents an error during tag update operation.
    /// </summary>
    public class TagUpdateError
    {
        /// <summary>Gets or sets the item that caused the error.</summary>
        public string Item { get; set; } = string.Empty;

        /// <summary>Gets or sets the error message.</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>Gets or sets detailed error information.</summary>
        public string? Details { get; set; }

        /// <summary>Gets or sets the CSV line number (if applicable).</summary>
        public int? LineNumber { get; set; }
    }
}
