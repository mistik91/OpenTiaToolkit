using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using static OpenTiaToolkit.Core.Services.HmiJsonImportPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonScalarPropertyImporter
    {
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly HmiJsonImportPrimitives _primitives;

        internal HmiJsonScalarPropertyImporter(V21UnifiedHmiAdapter adapter, HmiJsonImportPrimitives primitives)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
        }

        internal void ProcessScreenProperties(
            string screenPath,
            IReadOnlyDictionary<string, string?> properties,
            HmiScreen? screen,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            foreach (var property in properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!HmiJsonFilterMatcher.ShouldIncludeProperty(property.Key, filters))
                {
                    AddSkippedProperty(result, "Screen", screenPath, itemName: null, property.Key, "Filtered out");
                    continue;
                }

                if (property.Value == null)
                {
                    AddSkippedProperty(result, "Screen", screenPath, itemName: null, property.Key, "Null property values are not supported.");
                    continue;
                }

                string? currentValue = null;
                var hasCurrentValue = screen != null && TryGetScreenPropertyValue(screen, property.Key, out currentValue);
                if (hasCurrentValue && string.Equals(currentValue, property.Value, StringComparison.Ordinal))
                {
                    result.PropertyUnchangedCount++;
                    continue;
                }

                AddPropertyChange(result, "Screen", screenPath, itemName: null, property.Key, hasCurrentValue ? "Update" : "Create", currentValue, property.Value);
                if (dryRun)
                {
                    if (screen != null && !TryValidateScreenProperty(screen, property.Key, property.Value, out var validationError))
                    {
                        AddFailedProperty(result, "Screen", screenPath, itemName: null, property.Key, validationError ?? "Screen property could not be validated.");
                        continue;
                    }

                    result.PropertySetCount++;
                    continue;
                }

                string? error = null;
                if (screen == null || !TrySetScreenProperty(screen, property.Key, property.Value, out error))
                {
                    AddFailedProperty(result, "Screen", screenPath, itemName: null, property.Key, error ?? "Screen property could not be set.");
                    continue;
                }

                result.PropertySetCount++;
            }
        }

        internal void ProcessItemProperties(
            string screenPath,
            HmiJsonItem itemDocument,
            HmiScreenItemBase? item,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result,
            IReadOnlyDictionary<string, string?>? originalCreationPropertyValues = null)
        {
            foreach (var property in itemDocument.Properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!HmiJsonFilterMatcher.ShouldIncludeProperty(property.Key, filters))
                {
                    AddSkippedProperty(result, "Item", screenPath, itemDocument.Name, property.Key, "Filtered out");
                    continue;
                }

                if (property.Value == null)
                {
                    AddSkippedProperty(result, "Item", screenPath, itemDocument.Name, property.Key, "Null property values are not supported.");
                    continue;
                }

                if (originalCreationPropertyValues != null &&
                    originalCreationPropertyValues.TryGetValue(property.Key, out var originalValue))
                {
                    if (string.Equals(originalValue, property.Value, StringComparison.Ordinal))
                    {
                        result.PropertyUnchangedCount++;
                    }
                    else
                    {
                        AddPropertyChange(
                            result,
                            "Item",
                            screenPath,
                            itemDocument.Name,
                            property.Key,
                            originalValue == null ? "Create" : "Update",
                            originalValue,
                            property.Value);
                        result.PropertySetCount++;
                    }

                    continue;
                }

                string? currentValue = null;
                var hasCurrentValue = item != null && _primitives.TryGetItemPropertyValue(item, property.Key, out currentValue);
                if (hasCurrentValue && string.Equals(currentValue, property.Value, StringComparison.Ordinal))
                {
                    result.PropertyUnchangedCount++;
                    continue;
                }

                AddPropertyChange(result, "Item", screenPath, itemDocument.Name, property.Key, hasCurrentValue ? "Update" : "Create", currentValue, property.Value);
                if (dryRun)
                {
                    if (item != null && !_adapter.TryValidateItemProperty(item, property.Key, property.Value, software, out var validationError))
                    {
                        AddFailedProperty(result, "Item", screenPath, itemDocument.Name, property.Key, validationError ?? "Item property could not be validated.");
                        continue;
                    }

                    result.PropertySetCount++;
                    continue;
                }

                string? error = null;
                if (item == null || !_adapter.TrySetItemProperty(item, property.Key, property.Value, software, out error))
                {
                    AddFailedProperty(result, "Item", screenPath, itemDocument.Name, property.Key, error ?? "Item property could not be set.");
                    continue;
                }

                result.PropertySetCount++;
            }
        }
    }
}
