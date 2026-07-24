using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenItemCreationPipeline
    {
        private readonly V21UnifiedHmiAdapter _adapter;

        internal HmiScreenItemCreationPipeline(V21UnifiedHmiAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        internal HmiScreenItemCreationResult Create(
            HmiScreen screen,
            HmiSoftware software,
            string itemType,
            string itemName,
            IReadOnlyDictionary<string, string?>? properties = null)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (software == null) throw new ArgumentNullException(nameof(software));

            HmiScreenItemBase? item = null;
            try
            {
                item = _adapter.CreateScreenItem(screen, itemType, itemName);
                if (item == null)
                {
                    return HmiScreenItemCreationResult.Failure(
                        $"Item '{itemName}' could not be created as type '{itemType}'.");
                }

                var creationProperties = SelectCreationProperties(
                    _adapter.GetItemCreationPropertyNames(itemType),
                    properties);
                var originalValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in creationProperties)
                {
                    originalValues[property.Key] = ReadPropertyValue(item, property.Key);
                    if (!_adapter.TrySetItemProperty(item, property.Key, property.Value!, software, out var error))
                    {
                        return CleanupFailure(
                            item,
                            $"Creation property '{property.Key}' could not be set on item '{itemName}': {error ?? "Unknown error."}");
                    }
                }

                if (creationProperties.Count > 0)
                {
                    item = screen.ScreenItems.Find(itemName) ?? item;
                    foreach (var property in creationProperties)
                    {
                        var actualValue = ReadPropertyValue(item, property.Key);
                        if (!string.Equals(actualValue, property.Value, StringComparison.Ordinal))
                        {
                            return CleanupFailure(
                                item,
                                $"Creation property '{property.Key}' on item '{itemName}' did not match after write. Expected '{property.Value}', actual '{actualValue ?? "<null>"}'.");
                        }
                    }
                }

                return HmiScreenItemCreationResult.Success(item, originalValues);
            }
            catch (Exception ex)
            {
                var message = V21HmiEngineeringPrimitives.FormatExceptionMessage(ex);
                return item == null
                    ? HmiScreenItemCreationResult.Failure(message)
                    : CleanupFailure(item, message);
            }
        }

        internal static IReadOnlyList<KeyValuePair<string, string?>> SelectCreationProperties(
            IReadOnlyList<string> creationPropertyNames,
            IReadOnlyDictionary<string, string?>? properties)
        {
            if (creationPropertyNames == null || creationPropertyNames.Count == 0 || properties == null)
            {
                return Array.Empty<KeyValuePair<string, string?>>();
            }

            var selected = new List<KeyValuePair<string, string?>>();
            foreach (var propertyName in creationPropertyNames)
            {
                var property = properties.FirstOrDefault(entry =>
                    entry.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(property.Key) || property.Value == null)
                {
                    continue;
                }

                selected.Add(new KeyValuePair<string, string?>(propertyName, property.Value));
            }

            return selected;
        }

        private string? ReadPropertyValue(HmiScreenItemBase item, string propertyName)
        {
            return _adapter
                .GetItemProperties(item, new[] { propertyName })
                .FirstOrDefault(property => property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        private static HmiScreenItemCreationResult CleanupFailure(HmiScreenItemBase item, string error)
        {
            try
            {
                V21HmiEngineeringPrimitives.DeleteEngineeringObject(item);
                return HmiScreenItemCreationResult.Failure(error);
            }
            catch (Exception cleanupException)
            {
                return HmiScreenItemCreationResult.Failure(
                    error + " Cleanup also failed: " + V21HmiEngineeringPrimitives.FormatExceptionMessage(cleanupException));
            }
        }
    }

    internal sealed class HmiScreenItemCreationResult
    {
        private HmiScreenItemCreationResult(
            HmiScreenItemBase? item,
            IReadOnlyDictionary<string, string?> originalCreationPropertyValues,
            string? error)
        {
            Item = item;
            OriginalCreationPropertyValues = originalCreationPropertyValues;
            Error = error;
        }

        public HmiScreenItemBase? Item { get; }

        public IReadOnlyDictionary<string, string?> OriginalCreationPropertyValues { get; }

        public string? Error { get; }

        public bool Succeeded => Item != null && string.IsNullOrWhiteSpace(Error);

        public static HmiScreenItemCreationResult Success(
            HmiScreenItemBase item,
            IReadOnlyDictionary<string, string?> originalCreationPropertyValues)
        {
            return new HmiScreenItemCreationResult(item, originalCreationPropertyValues, error: null);
        }

        public static HmiScreenItemCreationResult Failure(string error)
        {
            return new HmiScreenItemCreationResult(
                item: null,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                error);
        }
    }
}
