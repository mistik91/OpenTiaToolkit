using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    /// <summary>
    /// Pure matching, validation, collision, and patch rules for ordered tag plans.
    /// </summary>
    internal sealed class PlcTagModificationPlanner
    {
        public TagSnapshot ApplyPatch(TagSnapshot source, TagValuePatch patch)
        {
            var snapshot = new TagSnapshot
            {
                TagTable = patch.HasTagTable ? patch.TagTable ?? string.Empty : source.TagTable,
                Name = patch.HasName ? patch.Name ?? string.Empty : source.Name,
                DataType = patch.HasDataType ? patch.DataType ?? string.Empty : source.DataType,
                LogicalAddress = patch.HasLogicalAddress
                    ? TagUpdateCsvParser.NormalizeLogicalAddress(patch.LogicalAddress ?? string.Empty)
                    : source.LogicalAddress,
                Comment = patch.HasComment ? patch.Comment ?? string.Empty : source.Comment
            };

            RequireValue(true, snapshot.TagTable, "Resulting tag table cannot be empty");
            RequireValue(true, snapshot.Name, "Resulting tag name cannot be empty");
            RequireValue(true, snapshot.DataType, "Resulting data type cannot be empty");
            return snapshot;
        }

        public PlcTagState FindSingleTag(IEnumerable<PlcTagState> tags, TagMatchCriteria match)
        {
            if (string.IsNullOrWhiteSpace(match.TagTable)
                && string.IsNullOrWhiteSpace(match.Name)
                && string.IsNullOrWhiteSpace(match.LogicalAddress))
            {
                throw new InvalidOperationException(
                    "Tag match must specify at least one of tagTable, name, or logicalAddress");
            }

            var address = match.LogicalAddress == null
                ? null
                : TagUpdateCsvParser.NormalizeLogicalAddress(match.LogicalAddress);
            var matches = tags
                .Where(tag => !tag.IsDeleted)
                .Where(tag => match.TagTable == null
                    || string.Equals(tag.Table.Name, match.TagTable, StringComparison.OrdinalIgnoreCase))
                .Where(tag => match.Name == null
                    || string.Equals(tag.Name, match.Name, StringComparison.OrdinalIgnoreCase))
                .Where(tag => address == null
                    || string.Equals(tag.LogicalAddress, address, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                throw new InvalidOperationException("No existing tag matches the supplied criteria");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Tag match is ambiguous. Matched: {string.Join("; ", matches.Select(tag => $"[{tag.Table.Name}] {tag.Name}"))}");
            }

            return matches[0];
        }

        public PlcTagTableState FindTable(IEnumerable<PlcTagTableState> tables, string name)
        {
            var matches = tables
                .Where(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"Tag table '{name}' was not found");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Tag table name '{name}' is ambiguous");
            }

            return matches[0];
        }

        public void EnsureNoNameCollision(
            IEnumerable<PlcTagState> tags,
            string tableName,
            string tagName,
            PlcTagState? allowed = null)
        {
            if (tags.Any(tag =>
                !tag.IsDeleted
                && !ReferenceEquals(tag, allowed)
                && string.Equals(tag.Table.Name, tableName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Tag '{tagName}' already exists in tag table '{tableName}'");
            }
        }

        public void RequireValue(bool isPresent, string? value, string message)
        {
            if (!isPresent || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
