using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonPatchFilterPlanner
    {
        private readonly HmiScreenJsonPatchOperationReader _reader = new HmiScreenJsonPatchOperationReader();

        public HmiJsonFilterOptions MergeOperationFilters(HmiJsonFilterOptions? filters, IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            var normalized = HmiJsonFilterMatcher.Normalize(filters);
            var itemNames = normalized.ItemNames.ToList();
            var properties = normalized.Properties.ToList();
            foreach (var operation in operations)
            {
                AddDistinct(itemNames, _reader.ItemName(operation));
                AddDistinct(itemNames, _reader.SourceItemName(operation));
                AddDistinct(itemNames, _reader.TargetItemName(operation));
                foreach (var property in TouchedProperties(operation)) AddDistinct(properties, property);
            }
            return new HmiJsonFilterOptions { Screens = normalized.Screens, ItemNames = itemNames, Properties = properties };
        }

        public List<HmiScreenJsonPatchOperationResult> ValidateOperationFilters(HmiJsonFilterOptions filters, IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            var results = new List<HmiScreenJsonPatchOperationResult>();
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                var op = _reader.OperationName(operation);
                foreach (var itemName in FilterItemNames(operation).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(itemName) || filters.ItemNames.Count == 0 || HmiJsonFilterMatcher.ShouldIncludeItem(itemName, filters)) continue;
                    results.Add(HmiScreenJsonPatchResultFactory.Failure(index, op,
                        $"Item '{itemName}' is outside the current item-names filter. Re-read or apply with item-names including '{itemName}' so the base hash and diff cover the target item.",
                        itemName, suggestions: HmiPatchSuggestionService.BuildItemFilterSuggestions(itemName)));
                }
                var issueItem = HmiScreenJsonPatchOperationReader.FirstNonEmpty(_reader.ItemName(operation), _reader.TargetItemName(operation), _reader.SourceItemName(operation));
                foreach (var property in TouchedProperties(operation).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (filters.Properties.Count == 0 || HmiJsonFilterMatcher.ShouldIncludeProperty(property, filters)) continue;
                    results.Add(HmiScreenJsonPatchResultFactory.Failure(index, op,
                        $"Property '{property}' is outside the current properties filter. Re-read or apply with properties including '{property}' so the base hash and diff cover the requested change.",
                        issueItem, property, HmiPatchSuggestionService.BuildPropertyFilterSuggestions(property)));
                }
            }
            return results;
        }

        internal IEnumerable<string> TouchedProperties(HmiScreenJsonPatchOperation operation)
        {
            switch (HmiScreenJsonPatchOperationCatalog.Normalize(_reader.OperationName(operation)))
            {
                case "setproperty": if (!string.IsNullOrWhiteSpace(_reader.PropertyName(operation))) yield return _reader.PropertyName(operation)!; break;
                case "setproperties":
                    if (operation.Properties != null) foreach (var property in operation.Properties.Keys.Where(value => !string.IsNullOrWhiteSpace(value))) yield return property;
                    break;
                case "moveitem": if (operation.Left.HasValue) yield return "Left"; if (operation.Top.HasValue) yield return "Top"; break;
                case "resizeitem": if (operation.Width.HasValue) yield return "Width"; if (operation.Height.HasValue) yield return "Height"; break;
                case "setlayout":
                    if (operation.Left.HasValue) yield return "Left"; if (operation.Top.HasValue) yield return "Top";
                    if (operation.Width.HasValue) yield return "Width"; if (operation.Height.HasValue) yield return "Height"; break;
                case "createitem":
                case "duplicateitem":
                    if (operation.Properties != null) foreach (var property in operation.Properties.Keys.Where(value => !string.IsNullOrWhiteSpace(value))) yield return property;
                    if (!string.IsNullOrWhiteSpace(_reader.ContainedType(operation))) yield return "ContainedType";
                    if (operation.Left.HasValue) yield return "Left"; if (operation.Top.HasValue) yield return "Top";
                    if (operation.Width.HasValue) yield return "Width"; if (operation.Height.HasValue) yield return "Height"; break;
                case "seteventhandler":
                case "removeeventhandler": yield return "EventHandlers"; break;
                case "setpropertyeventhandler":
                case "removepropertyeventhandler": yield return "PropertyEventHandlers"; break;
                case "setdynamization":
                case "removedynamization": yield return "Dynamizations"; break;
                case "setinterface": yield return "Interface"; break;
            }
        }

        private IEnumerable<string> FilterItemNames(HmiScreenJsonPatchOperation operation)
        {
            switch (HmiScreenJsonPatchOperationCatalog.Normalize(_reader.OperationName(operation)))
            {
                case "duplicateitem":
                    if (!string.IsNullOrWhiteSpace(_reader.SourceItemName(operation))) yield return _reader.SourceItemName(operation)!;
                    if (!string.IsNullOrWhiteSpace(_reader.TargetItemName(operation))) yield return _reader.TargetItemName(operation)!; break;
                case "renameitem":
                    var source = HmiScreenJsonPatchOperationReader.FirstNonEmpty(_reader.ItemName(operation), _reader.SourceItemName(operation));
                    if (!string.IsNullOrWhiteSpace(source)) yield return source!;
                    if (!string.IsNullOrWhiteSpace(_reader.NewName(operation))) yield return _reader.NewName(operation)!; break;
                default: if (!string.IsNullOrWhiteSpace(_reader.ItemName(operation))) yield return _reader.ItemName(operation)!; break;
            }
        }

        private static void AddDistinct(List<string> values, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase))) values.Add(value!.Trim());
        }
    }
}
