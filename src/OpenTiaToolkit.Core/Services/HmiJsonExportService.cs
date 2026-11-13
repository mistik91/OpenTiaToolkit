using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Exports the supported Unified HMI surface to JSON.
    /// </summary>
    internal sealed class HmiJsonExportService
    {
        private const string EventHandlersPropertyName = "EventHandlers";
        private const string PropertyEventHandlersPropertyName = "PropertyEventHandlers";
        private const string DynamizationsPropertyName = "Dynamizations";
        private const string InterfacePropertyName = "Interface";
        private static readonly string[] DefaultScreenProperties = { "Width", "Height", "Enabled", "BackGraphic" };
        private static readonly string[] DefaultNestedItemProperties = { "Caption.Text" };

        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly ILogger? _logger;

        public HmiJsonExportService(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;
        }

        public async Task<HmiJsonExportResult?> ExportAsync(string outputPath, HmiJsonFilterOptions? filters)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                var effectiveFilters = HmiJsonFilterMatcher.Normalize(filters);
                var document = new HmiJsonDocument
                {
                    DocumentKind = "Export",
                    ExportedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    HmiName = _sessionContext.CurrentHmiName,
                    Filters = effectiveFilters
                };

                var itemCount = 0;
                foreach (var screenPath in _adapter.ListScreenPaths(software))
                {
                    if (!HmiJsonFilterMatcher.ShouldIncludeScreen(screenPath, effectiveFilters))
                    {
                        continue;
                    }

                    if (!_adapter.TryGetScreen(software, screenPath, out var screen, out var normalizedPath) || screen == null)
                    {
                        document.Warnings.Add($"Screen '{screenPath}' could not be resolved during export.");
                        continue;
                    }

                    var exportedScreen = new HmiJsonScreen
                    {
                        Path = normalizedPath,
                        Name = screen.Name,
                        Properties = ExportScreenProperties(screen, effectiveFilters)
                    };
                    ExportCompositeState(screen, exportedScreen, effectiveFilters, document.Warnings, $"Screen '{normalizedPath}'");

                    foreach (var item in screen.ScreenItems.Cast<HmiScreenItemBase>())
                    {
                        if (!HmiJsonFilterMatcher.ShouldIncludeItem(item.Name, effectiveFilters))
                        {
                            continue;
                        }

                        var exportedItem = new HmiJsonItem
                        {
                            Name = item.Name,
                            Type = item.GetType().Name,
                            Properties = ExportItemProperties(item, effectiveFilters)
                        };
                        ExportCompositeState(item, exportedItem, effectiveFilters, document.Warnings, $"Item '{item.Name}' on '{normalizedPath}'");
                        exportedScreen.Items.Add(exportedItem);
                        itemCount++;
                    }

                    document.Screens.Add(exportedScreen);
                }

                var outputFile = HmiJsonDocumentStore.ResolveExportFilePath(outputPath);
                HmiJsonDocumentStore.SaveDocument(document, outputFile);

                return new HmiJsonExportResult
                {
                    OutputPath = outputFile,
                    ScreenCount = document.Screens.Count,
                    ItemCount = itemCount,
                    WarningCount = document.Warnings.Count
                };
            });
        }

        private Dictionary<string, string?> ExportScreenProperties(HmiScreen screen, HmiJsonFilterOptions filters)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var requestedProperties = filters.Properties.Count > 0 ? filters.Properties : DefaultScreenProperties;

            foreach (var propertyName in requestedProperties)
            {
                if (!HmiJsonFilterMatcher.ShouldIncludeProperty(propertyName, filters))
                {
                    continue;
                }

                if (TryGetScreenPropertyValue(screen, propertyName, out var propertyValue) && propertyValue != null)
                {
                    result[propertyName] = propertyValue;
                }
            }

            return result
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, string?> ExportItemProperties(HmiScreenItemBase item, HmiJsonFilterOptions filters)
        {
            var exportedProperties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var requestedScalarProperties = GetRequestedScalarProperties(filters);

            if (filters.Properties.Count > 0)
            {
                foreach (var property in _adapter.GetItemProperties(item, requestedScalarProperties.Length > 0 ? requestedScalarProperties : Array.Empty<string>())
                    .Where(property => property.IsWritable && property.Value != null))
                {
                    exportedProperties[property.Name] = property.Value;
                }

                return exportedProperties
                    .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var property in _adapter.GetItemProperties(item, propertyNames: null)
                .Where(property => property.IsWritable && property.Value != null))
            {
                exportedProperties[property.Name] = property.Value;
            }

            foreach (var nestedProperty in DefaultNestedItemProperties)
            {
                if (exportedProperties.ContainsKey(nestedProperty))
                {
                    continue;
                }

                var nestedValue = _adapter.GetItemProperties(item, new[] { nestedProperty })
                    .FirstOrDefault(property => property.IsWritable && property.Value != null);
                if (nestedValue != null)
                {
                    exportedProperties[nestedValue.Name] = nestedValue.Value;
                }
            }

            return exportedProperties
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        }

        private void ExportCompositeState(
            object source,
            HmiJsonScreen target,
            HmiJsonFilterOptions filters,
            ICollection<string> warnings,
            string context)
        {
            target.EventHandlers = ExportEventHandlers(source, filters, warnings, context);
            target.PropertyEventHandlers = ExportPropertyEventHandlers(source, filters, warnings, context);
            target.Dynamizations = ExportDynamizations(source, filters, warnings, context);
        }

        private void ExportCompositeState(
            object source,
            HmiJsonItem target,
            HmiJsonFilterOptions filters,
            ICollection<string> warnings,
            string context)
        {
            target.EventHandlers = ExportEventHandlers(source, filters, warnings, context);
            target.PropertyEventHandlers = ExportPropertyEventHandlers(source, filters, warnings, context);
            target.Dynamizations = ExportDynamizations(source, filters, warnings, context);
            target.Interface = ExportInterface(source, filters, warnings, context);
        }

        private List<HmiJsonEventHandler>? ExportEventHandlers(
            object source,
            HmiJsonFilterOptions filters,
            ICollection<string> warnings,
            string context)
        {
            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(EventHandlersPropertyName, filters))
            {
                return null;
            }

            if (!_adapter.TryReadEventHandlers(source, out var eventHandlers, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    warnings.Add($"{context}: failed to export {EventHandlersPropertyName} ({error})");
                }

                return null;
            }

            var filteredHandlers = eventHandlers
                .Where(handler => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(EventHandlersPropertyName, handler.EventType, filters))
                .ToList();
            return filteredHandlers.Count > 0 ? filteredHandlers : null;
        }

        private List<HmiJsonPropertyEventHandler>? ExportPropertyEventHandlers(
            object source,
            HmiJsonFilterOptions filters,
            ICollection<string> warnings,
            string context)
        {
            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(PropertyEventHandlersPropertyName, filters))
            {
                return null;
            }

            if (!_adapter.TryReadPropertyEventHandlers(source, out var propertyEventHandlers, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    warnings.Add($"{context}: failed to export {PropertyEventHandlersPropertyName} ({error})");
                }

                return null;
            }

            var filteredHandlers = propertyEventHandlers
                .Where(handler =>
                {
                    var key = HmiJsonCompositeComparer.GetPropertyEventHandlerKey(handler.PropertyName, handler.EventType);
                    return key != null &&
                           HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(
                               PropertyEventHandlersPropertyName,
                               HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(key),
                               filters);
                })
                .ToList();
            return filteredHandlers.Count > 0 ? filteredHandlers : null;
        }

        private List<HmiJsonDynamization>? ExportDynamizations(
            object source,
            HmiJsonFilterOptions filters,
            ICollection<string> warnings,
            string context)
        {
            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(DynamizationsPropertyName, filters))
            {
                return null;
            }

            if (!_adapter.TryReadDynamizations(source, out var dynamizations, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    warnings.Add($"{context}: failed to export {DynamizationsPropertyName} ({error})");
                }

                return null;
            }

            var filteredDynamizations = dynamizations
                .Where(dynamization => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(DynamizationsPropertyName, dynamization.Property, filters))
                .ToList();
            return filteredDynamizations.Count > 0 ? filteredDynamizations : null;
        }

        private List<HmiJsonInterfaceBinding>? ExportInterface(
            object source,
            HmiJsonFilterOptions filters,
            ICollection<string> warnings,
            string context)
        {
            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(InterfacePropertyName, filters))
            {
                return null;
            }

            if (!_adapter.TryReadInterfaceBindings(source, out var bindings, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    warnings.Add($"{context}: failed to export {InterfacePropertyName} ({error})");
                }

                return null;
            }

            var filteredBindings = bindings
                .Where(binding => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(InterfacePropertyName, binding.PropertyName, filters))
                .ToList();
            return filteredBindings.Count > 0 ? filteredBindings : null;
        }

        private static string[] GetRequestedScalarProperties(HmiJsonFilterOptions filters)
        {
            return filters.Properties
                .Where(property =>
                    !property.StartsWith(EventHandlersPropertyName, StringComparison.OrdinalIgnoreCase) &&
                    !property.StartsWith(PropertyEventHandlersPropertyName, StringComparison.OrdinalIgnoreCase) &&
                    !property.StartsWith(DynamizationsPropertyName, StringComparison.OrdinalIgnoreCase) &&
                    !property.StartsWith(InterfacePropertyName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static bool TryGetScreenPropertyValue(HmiScreen screen, string propertyName, out string? value)
        {
            value = null;

            switch (propertyName)
            {
                case "Width":
                    value = screen.Width.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "Height":
                    value = screen.Height.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "Enabled":
                    value = screen.Enabled.ToString();
                    return true;
            }

            try
            {
                var property = screen.GetType().GetProperty(propertyName);
                if (property == null || !property.CanRead)
                {
                    return false;
                }

                value = FormatValue(property.GetValue(screen));
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        private static string? FormatValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string stringValue)
            {
                return stringValue;
            }

            if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }
    }
}
