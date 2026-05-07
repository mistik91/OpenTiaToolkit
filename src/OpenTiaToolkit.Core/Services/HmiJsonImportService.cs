using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Commands.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Preflights and applies supported Unified HMI JSON documents.
    /// </summary>
    internal sealed class HmiJsonImportService
    {
        private const string EventHandlersPropertyName = "EventHandlers";
        private const string PropertyEventHandlersPropertyName = "PropertyEventHandlers";
        private const string DynamizationsPropertyName = "Dynamizations";
        private const string InterfacePropertyName = "Interface";
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly HmiJsonExportService _exportService;
        private readonly ILogger? _logger;

        public HmiJsonImportService(
            EngineeringSessionContext sessionContext,
            V21UnifiedHmiAdapter adapter,
            HmiJsonExportService exportService,
            ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _logger = logger;
        }

        public async Task<HmiJsonImportResult?> ImportAsync(
            string inputPath,
            HmiJsonFilterOptions? filters,
            bool dryRun,
            HmiJsonImportExecutionOptions? executionOptions)
        {
            var options = executionOptions ?? new HmiJsonImportExecutionOptions();

            if (dryRun)
            {
                var preflightResult = await ImportCoreAsync(inputPath, filters, dryRun: true);
                ApplySafetyPolicy(preflightResult, options);
                return preflightResult;
            }

            HmiJsonImportResult? gatePreflightResult = null;
            if (options.RequireCleanPreflight || options.FailOnWarning)
            {
                gatePreflightResult = await ImportCoreAsync(inputPath, filters, dryRun: true);
                if (gatePreflightResult == null)
                {
                    return CreateSafetyBlockedResult(inputPath, dryRun: false, "Import was blocked because the safety preflight could not be completed.");
                }

                var blockingMessage = GetBlockingSafetyGateMessage(gatePreflightResult, options);
                if (!string.IsNullOrWhiteSpace(blockingMessage))
                {
                    MarkSafetyBlocked(gatePreflightResult, blockingMessage!, backupPath: null);
                    TryWriteReport(gatePreflightResult, inputPath, dryRun: true);
                    return gatePreflightResult;
                }
            }

            string? backupOutputPath = null;
            if (options.BackupBeforeImport)
            {
                var backupTargetPath = HmiJsonDocumentStore.ResolveBackupExportPath(inputPath, options.BackupPath);
                var backupResult = await _exportService.ExportAsync(backupTargetPath, filters: null);
                if (backupResult == null || string.IsNullOrWhiteSpace(backupResult.OutputPath))
                {
                    return gatePreflightResult ?? CreateSafetyBlockedResult(
                        inputPath,
                        dryRun: false,
                        "Import was blocked because the backup export could not be created.");
                }

                backupOutputPath = backupResult.OutputPath;
            }

            var result = await ImportCoreAsync(inputPath, filters, dryRun: false);
            if (result != null)
            {
                result.BackupPath = backupOutputPath;
                ApplySafetyPolicy(result, options, backupOutputPath);
                TryWriteReport(result, inputPath, dryRun: false);
            }

            return result;
        }

        private async Task<HmiJsonImportResult?> ImportCoreAsync(string inputPath, HmiJsonFilterOptions? filters, bool dryRun)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                var resolvedInputPath = HmiJsonDocumentStore.ResolveImportFilePath(inputPath);
                if (!System.IO.File.Exists(resolvedInputPath))
                {
                    _logger?.LogWarning("Unified HMI JSON input file not found: {InputPath}", resolvedInputPath);
                    return null;
                }

                var document = HmiJsonDocumentStore.LoadDocument(resolvedInputPath);
                var effectiveFilters = HmiJsonFilterMatcher.Normalize(filters ?? document.Filters);

                var result = new HmiJsonImportResult
                {
                    GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    DryRun = dryRun,
                    InputPath = resolvedInputPath
                };

                foreach (var screenDocument in document.Screens
                    .Where(screen => HmiJsonFilterMatcher.ShouldIncludeScreen(screen.Path, effectiveFilters)))
                {
                    ProcessScreenDocument(software, screenDocument, effectiveFilters, dryRun, result);
                }

                result.WarningCount = result.Warnings.Count;
                TryWriteReport(result, inputPath, dryRun);
                return result;
            });
        }

        private void ProcessScreenDocument(
            HmiSoftware software,
            HmiJsonScreen screenDocument,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            var screenPath = screenDocument.Path;
            var screenAction = NormalizeAction(screenDocument.Action, defaultAction: "Update");
            var screenExists = _adapter.TryGetScreen(software, screenPath, out var existingScreen, out _ ) && existingScreen != null;
            var screen = existingScreen;

            if (string.Equals(screenAction, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                result.ScreenCount++;
                if (!screenExists)
                {
                    AddWarning(result, $"Screen '{screenPath}' does not exist, so the delete action was skipped.");
                    return;
                }

                result.ScreenDeleted++;
                AddObjectAction(result, "Screen", screenPath, null, null, "Delete", dryRun ? "Screen would be deleted." : "Screen was deleted.");
                if (!dryRun && screen != null && !TryDeleteEngineeringObject(screen, out var error))
                {
                    AddWarning(result, $"Screen '{screenPath}' could not be deleted: {error}");
                }

                return;
            }

            if (!screenExists)
            {
                result.ScreenCreated++;
                AddObjectAction(result, "Screen", screenPath, null, null, "Create", dryRun ? "Screen would be created." : "Screen was created.");
                if (!dryRun)
                {
                    screen = _adapter.EnsureScreen(software, screenPath);
                    screenExists = screen != null;
                }
            }
            else if (string.Equals(screenAction, "Create", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(result, $"Screen '{screenPath}' already exists; create action is being treated as update.");
            }

            result.ScreenCount++;
            if (!dryRun && screen == null)
            {
                AddWarning(result, $"Screen '{screenPath}' could not be resolved.");
                return;
            }

            ProcessScreenProperties(screenPath, screenDocument.Properties, screen, filters, dryRun, result);
            ProcessEventHandlers(
                scope: "Screen",
                screenPath,
                itemName: null,
                screenDocument.EventHandlers,
                screen,
                typeof(HmiScreen),
                filters,
                dryRun,
                result);
            ProcessPropertyEventHandlers(
                scope: "Screen",
                screenPath,
                itemName: null,
                screenDocument.PropertyEventHandlers,
                screen,
                typeof(HmiScreen),
                filters,
                dryRun,
                result);
            ProcessDynamizations(
                scope: "Screen",
                screenPath,
                itemName: null,
                screenDocument.Dynamizations,
                screen,
                typeof(HmiScreen),
                filters,
                dryRun,
                result);

            foreach (var itemDocument in screenDocument.Items
                .Where(item => HmiJsonFilterMatcher.ShouldIncludeItem(item.Name, filters)))
            {
                ProcessItemDocument(screenPath, screen, itemDocument, software, filters, dryRun, result);
            }
        }

        private void ProcessScreenProperties(
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

        private void ProcessItemDocument(
            string screenPath,
            HmiScreen? screen,
            HmiJsonItem itemDocument,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            result.ItemCount++;

            var itemAction = NormalizeAction(itemDocument.Action, defaultAction: "Update");
            var existingItem = screen?.ScreenItems.Find(itemDocument.Name);

            if (string.Equals(itemAction, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                if (existingItem == null)
                {
                    AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' does not exist, so the delete action was skipped.");
                    return;
                }

                result.ItemDeleted++;
                AddObjectAction(result, "Item", screenPath, itemDocument.Name, itemDocument.Type, "Delete", dryRun ? "Item would be deleted." : "Item was deleted.");
                if (!dryRun && !TryDeleteEngineeringObject(existingItem, out var error))
                {
                    AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be deleted: {error}");
                }

                return;
            }

            var requiresRecreate =
                string.Equals(itemAction, "Recreate", StringComparison.OrdinalIgnoreCase) ||
                (existingItem != null && !string.Equals(existingItem.GetType().Name, itemDocument.Type, StringComparison.OrdinalIgnoreCase));

            if (existingItem == null)
            {
                result.ItemCreated++;
                AddObjectAction(result, "Item", screenPath, itemDocument.Name, itemDocument.Type, "Create", dryRun ? "Item would be created." : "Item was created.");
                if (!dryRun)
                {
                    if (screen == null)
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be created because the screen could not be resolved.");
                        return;
                    }

                    existingItem = _adapter.CreateScreenItem(screen, itemDocument.Type, itemDocument.Name);
                    if (existingItem == null)
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be created as type '{itemDocument.Type}'.");
                        return;
                    }
                }
            }
            else if (requiresRecreate)
            {
                result.ItemRecreated++;
                AddObjectAction(result, "Item", screenPath, itemDocument.Name, itemDocument.Type, "Recreate", dryRun ? "Item would be recreated because the type changed." : "Item was recreated because the type changed.");
                if (!dryRun)
                {
                    if (!TryDeleteEngineeringObject(existingItem, out var error))
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be recreated: {error}");
                        return;
                    }

                    if (screen == null)
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be recreated because the screen could not be resolved.");
                        return;
                    }

                    existingItem = _adapter.CreateScreenItem(screen, itemDocument.Type, itemDocument.Name);
                    if (existingItem == null)
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be recreated as type '{itemDocument.Type}'.");
                        return;
                    }
                }
            }
            else if (string.Equals(itemAction, "Create", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' already exists; create action is being treated as update.");
            }

            ProcessItemProperties(screenPath, itemDocument, existingItem, software, filters, dryRun, result);
            var itemTargetType = existingItem?.GetType();
            if (itemTargetType == null && !_adapter.TryResolveItemClrType(itemDocument.Type, out itemTargetType))
            {
                itemTargetType = typeof(HmiScreenItemBase);
            }

            ProcessEventHandlers(
                scope: "Item",
                screenPath,
                itemDocument.Name,
                itemDocument.EventHandlers,
                existingItem,
                itemTargetType,
                filters,
                dryRun,
                result);
            ProcessPropertyEventHandlers(
                scope: "Item",
                screenPath,
                itemDocument.Name,
                itemDocument.PropertyEventHandlers,
                existingItem,
                itemTargetType,
                filters,
                dryRun,
                result);
            ProcessDynamizations(
                scope: "Item",
                screenPath,
                itemDocument.Name,
                itemDocument.Dynamizations,
                existingItem,
                itemTargetType,
                filters,
                dryRun,
                result);
            ProcessInterfaceBindings(
                scope: "Item",
                screenPath,
                itemDocument.Name,
                itemDocument.Interface,
                existingItem,
                itemTargetType,
                software,
                filters,
                dryRun,
                result);
        }

        private void ProcessItemProperties(
            string screenPath,
            HmiJsonItem itemDocument,
            HmiScreenItemBase? item,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
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

                string? currentValue = null;
                var hasCurrentValue = item != null && TryGetItemPropertyValue(item, property.Key, out currentValue);
                if (hasCurrentValue && string.Equals(currentValue, property.Value, StringComparison.Ordinal))
                {
                    result.PropertyUnchangedCount++;
                    continue;
                }

                AddPropertyChange(result, "Item", screenPath, itemDocument.Name, property.Key, hasCurrentValue ? "Update" : "Create", currentValue, property.Value);
                if (dryRun)
                {
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

        private void ProcessEventHandlers(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonEventHandler>? eventHandlers,
            object? owner,
            Type ownerType,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (eventHandlers == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(EventHandlersPropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, EventHandlersPropertyName, "Filtered out");
                return;
            }

            var filteredHandlers = eventHandlers
                .Where(handler => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(EventHandlersPropertyName, handler.EventType, filters))
                .ToList();
            if (eventHandlers.Count > 0 && filteredHandlers.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, EventHandlersPropertyName, "Filtered out");
                return;
            }

            var currentHandlers = new List<HmiJsonEventHandler>();
            if (owner != null)
            {
                if (!_adapter.TryReadEventHandlers(owner, out currentHandlers, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, EventHandlersPropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredHandlers = MergeFilteredEventHandlers(currentHandlers, filteredHandlers, filters);
            var changedKeys = HmiJsonCompositeComparer.GetChangedEventHandlerKeys(currentHandlers, desiredHandlers);
            if (changedKeys.Count == 0)
            {
                result.PropertyUnchangedCount += filteredHandlers.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyEventHandlers(owner, ownerType, desiredHandlers, apply: !dryRun, out var error))
            {
                foreach (var eventType in changedKeys)
                {
                    AddFailedProperty(result, scope, screenPath, itemName, $"{EventHandlersPropertyName}.{eventType}", error ?? "Event handlers could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildEventHandlerMap(currentHandlers);
            var desiredMap = HmiJsonCompositeComparer.BuildEventHandlerMap(desiredHandlers);
            foreach (var eventType in changedKeys)
            {
                currentMap.TryGetValue(eventType, out var currentHandler);
                desiredMap.TryGetValue(eventType, out var desiredHandler);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{EventHandlersPropertyName}.{eventType}",
                    currentHandler == null ? "Create" : desiredHandler == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentHandler),
                    SerializeCompositeValue(desiredHandler));
            }

            result.PropertySetCount += changedKeys.Count;
        }

        private void ProcessPropertyEventHandlers(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonPropertyEventHandler>? propertyEventHandlers,
            object? owner,
            Type ownerType,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (propertyEventHandlers == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(PropertyEventHandlersPropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, PropertyEventHandlersPropertyName, "Filtered out");
                return;
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
            if (propertyEventHandlers.Count > 0 && filteredHandlers.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, PropertyEventHandlersPropertyName, "Filtered out");
                return;
            }

            var currentHandlers = new List<HmiJsonPropertyEventHandler>();
            if (owner != null)
            {
                if (!_adapter.TryReadPropertyEventHandlers(owner, out currentHandlers, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, PropertyEventHandlersPropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredHandlers = MergeFilteredPropertyEventHandlers(currentHandlers, filteredHandlers, filters);
            var changedKeys = HmiJsonCompositeComparer.GetChangedPropertyEventHandlerKeys(currentHandlers, desiredHandlers);
            if (changedKeys.Count == 0)
            {
                result.PropertyUnchangedCount += filteredHandlers.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyPropertyEventHandlers(owner, ownerType, desiredHandlers, apply: !dryRun, out var error))
            {
                foreach (var key in changedKeys)
                {
                    AddFailedProperty(
                        result,
                        scope,
                        screenPath,
                        itemName,
                        $"{PropertyEventHandlersPropertyName}.{HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(key)}",
                        error ?? "Property event handlers could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(currentHandlers);
            var desiredMap = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(desiredHandlers);
            foreach (var key in changedKeys)
            {
                currentMap.TryGetValue(key, out var currentHandler);
                desiredMap.TryGetValue(key, out var desiredHandler);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{PropertyEventHandlersPropertyName}.{HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(key)}",
                    currentHandler == null ? "Create" : desiredHandler == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentHandler),
                    SerializeCompositeValue(desiredHandler));
            }

            result.PropertySetCount += changedKeys.Count;
        }

        private void ProcessDynamizations(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonDynamization>? dynamizations,
            object? owner,
            Type ownerType,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (dynamizations == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(DynamizationsPropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, DynamizationsPropertyName, "Filtered out");
                return;
            }

            var filteredDynamizations = dynamizations
                .Where(dynamization => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(DynamizationsPropertyName, dynamization.Property, filters))
                .ToList();
            if (dynamizations.Count > 0 && filteredDynamizations.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, DynamizationsPropertyName, "Filtered out");
                return;
            }

            var currentDynamizations = new List<HmiJsonDynamization>();
            if (owner != null)
            {
                if (!_adapter.TryReadDynamizations(owner, out currentDynamizations, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, DynamizationsPropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredDynamizations = MergeFilteredDynamizations(currentDynamizations, filteredDynamizations, filters);
            var changedProperties = HmiJsonCompositeComparer.GetChangedDynamizationProperties(currentDynamizations, desiredDynamizations);
            if (changedProperties.Count == 0)
            {
                result.PropertyUnchangedCount += filteredDynamizations.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyDynamizations(owner, ownerType, desiredDynamizations, apply: !dryRun, out var error))
            {
                foreach (var propertyName in changedProperties)
                {
                    AddFailedProperty(result, scope, screenPath, itemName, $"{DynamizationsPropertyName}.{propertyName}", error ?? "Dynamizations could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildDynamizationMap(currentDynamizations);
            var desiredMap = HmiJsonCompositeComparer.BuildDynamizationMap(desiredDynamizations);
            foreach (var propertyName in changedProperties)
            {
                currentMap.TryGetValue(propertyName, out var currentDynamization);
                desiredMap.TryGetValue(propertyName, out var desiredDynamization);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{DynamizationsPropertyName}.{propertyName}",
                    currentDynamization == null ? "Create" : desiredDynamization == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentDynamization),
                    SerializeCompositeValue(desiredDynamization));
            }

            result.PropertySetCount += changedProperties.Count;
        }

        private void ProcessInterfaceBindings(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonInterfaceBinding>? bindings,
            object? owner,
            Type ownerType,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (bindings == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(InterfacePropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, InterfacePropertyName, "Filtered out");
                return;
            }

            var filteredBindings = bindings
                .Where(binding => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(InterfacePropertyName, binding.PropertyName, filters))
                .ToList();
            if (bindings.Count > 0 && filteredBindings.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, InterfacePropertyName, "Filtered out");
                return;
            }

            if (!dryRun && string.Equals(scope, "Item", StringComparison.OrdinalIgnoreCase))
            {
                owner = ReacquireItemOwnerForInterfaceWrite(software, screenPath, itemName, result);
                ownerType = owner?.GetType() ?? ownerType;
            }

            var currentBindings = new List<HmiJsonInterfaceBinding>();
            if (owner != null)
            {
                if (!_adapter.TryReadInterfaceBindings(owner, out currentBindings, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, InterfacePropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredBindings = MergeFilteredInterfaceBindings(currentBindings, filteredBindings, filters);
            var changedProperties = HmiJsonCompositeComparer.GetChangedInterfaceProperties(currentBindings, desiredBindings);
            if (changedProperties.Count == 0)
            {
                result.PropertyUnchangedCount += filteredBindings.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyInterfaceBindings(owner, ownerType, desiredBindings, apply: !dryRun, out var error))
            {
                foreach (var propertyName in changedProperties)
                {
                    AddFailedProperty(result, scope, screenPath, itemName, $"{InterfacePropertyName}.{propertyName}", error ?? "Interface bindings could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildInterfaceBindingMap(currentBindings);
            var desiredMap = HmiJsonCompositeComparer.BuildInterfaceBindingMap(desiredBindings);
            foreach (var propertyName in changedProperties)
            {
                currentMap.TryGetValue(propertyName, out var currentBinding);
                desiredMap.TryGetValue(propertyName, out var desiredBinding);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{InterfacePropertyName}.{propertyName}",
                    currentBinding == null ? "Create" : desiredBinding == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentBinding),
                    SerializeCompositeValue(desiredBinding));
            }

            result.PropertySetCount += changedProperties.Count;
        }

        private object? ReacquireItemOwnerForInterfaceWrite(
            HmiSoftware software,
            string screenPath,
            string? itemName,
            HmiJsonImportResult result)
        {
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return null;
            }

            try
            {
                if (!_adapter.TryGetScreen(software, screenPath, out var screen, out var normalizedPath) || screen == null)
                {
                    AddWarning(result, $"Item '{FormatObjectPath(screenPath, itemName)}': screen could not be reacquired before Interface write.");
                    return null;
                }

                var item = screen.ScreenItems.Find(itemName);
                if (item == null)
                {
                    AddWarning(result, $"Item '{FormatObjectPath(normalizedPath, itemName)}': item could not be reacquired before Interface write.");
                }

                return item;
            }
            catch (Exception ex)
            {
                AddWarning(result, $"Item '{FormatObjectPath(screenPath, itemName)}': item reacquire before Interface write failed: {FormatExceptionMessage(ex)}");
                return null;
            }
        }

        internal static List<HmiJsonEventHandler> MergeFilteredEventHandlers(
            IReadOnlyList<HmiJsonEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredCompositeEntries(EventHandlersPropertyName, filters))
            {
                return HmiJsonCompositeComparer.CloneEventHandlers(filteredHandlers) ?? new List<HmiJsonEventHandler>();
            }

            var merged = HmiJsonCompositeComparer.BuildEventHandlerMap(currentHandlers)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(EventHandlersPropertyName, entry.Key, filters))
                .Select(entry => entry.Value)
                .Concat(filteredHandlers);

            return HmiJsonCompositeComparer.CloneEventHandlers(merged) ?? new List<HmiJsonEventHandler>();
        }

        internal static List<HmiJsonPropertyEventHandler> MergeFilteredPropertyEventHandlers(
            IReadOnlyList<HmiJsonPropertyEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonPropertyEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredCompositeEntries(PropertyEventHandlersPropertyName, filters))
            {
                return HmiJsonCompositeComparer.ClonePropertyEventHandlers(filteredHandlers) ?? new List<HmiJsonPropertyEventHandler>();
            }

            var merged = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(currentHandlers)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(
                    PropertyEventHandlersPropertyName,
                    HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(entry.Key),
                    filters))
                .Select(entry => entry.Value)
                .Concat(filteredHandlers);

            return HmiJsonCompositeComparer.ClonePropertyEventHandlers(merged) ?? new List<HmiJsonPropertyEventHandler>();
        }

        internal static List<HmiJsonDynamization> MergeFilteredDynamizations(
            IReadOnlyList<HmiJsonDynamization> currentDynamizations,
            IReadOnlyList<HmiJsonDynamization> filteredDynamizations,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredCompositeEntries(DynamizationsPropertyName, filters))
            {
                return HmiJsonCompositeComparer.CloneDynamizations(filteredDynamizations) ?? new List<HmiJsonDynamization>();
            }

            var merged = HmiJsonCompositeComparer.BuildDynamizationMap(currentDynamizations)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(DynamizationsPropertyName, entry.Key, filters))
                .Select(entry => entry.Value)
                .Concat(filteredDynamizations);

            return HmiJsonCompositeComparer.CloneDynamizations(merged) ?? new List<HmiJsonDynamization>();
        }

        internal static List<HmiJsonInterfaceBinding> MergeFilteredInterfaceBindings(
            IReadOnlyList<HmiJsonInterfaceBinding> currentBindings,
            IReadOnlyList<HmiJsonInterfaceBinding> filteredBindings,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredCompositeEntries(InterfacePropertyName, filters))
            {
                return HmiJsonCompositeComparer.CloneInterfaceBindings(filteredBindings) ?? new List<HmiJsonInterfaceBinding>();
            }

            var merged = HmiJsonCompositeComparer.BuildInterfaceBindingMap(currentBindings)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(InterfacePropertyName, entry.Key, filters))
                .Select(entry => entry.Value)
                .Concat(filteredBindings);

            return HmiJsonCompositeComparer.CloneInterfaceBindings(merged) ?? new List<HmiJsonInterfaceBinding>();
        }

        internal static void ApplySafetyPolicy(
            HmiJsonImportResult? result,
            HmiJsonImportExecutionOptions options,
            string? backupPath = null)
        {
            if (result == null)
            {
                return;
            }

            var blockingMessage = GetBlockingSafetyGateMessage(result, options);
            if (!string.IsNullOrWhiteSpace(blockingMessage))
            {
                MarkSafetyBlocked(result, blockingMessage!, backupPath);
            }
        }

        internal static string? GetBlockingSafetyGateMessage(HmiJsonImportResult result, HmiJsonImportExecutionOptions options)
        {
            if ((options.RequireCleanPreflight || options.FailOnWarning) &&
                (result.WarningCount > 0 || result.PropertyFailedCount > 0 || result.PropertySkippedCount > 0))
            {
                return $"Clean preflight was required, but the run reported {result.WarningCount} warning(s), {result.PropertyFailedCount} failed property change(s), and {result.PropertySkippedCount} skipped property change(s).";
            }

            return null;
        }

        private static bool ShouldPreserveUnfilteredCompositeEntries(string propertyName, HmiJsonFilterOptions filters)
        {
            return filters.Properties.Count > 0 &&
                   !filters.Properties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        private static HmiJsonImportResult CreateSafetyBlockedResult(string inputPath, bool dryRun, string safetyMessage)
        {
            return new HmiJsonImportResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                DryRun = dryRun,
                InputPath = HmiJsonDocumentStore.ResolveImportFilePath(inputPath),
                SafetyBlocked = true,
                SafetyMessage = safetyMessage
            };
        }

        private static void MarkSafetyBlocked(HmiJsonImportResult result, string safetyMessage, string? backupPath)
        {
            result.SafetyBlocked = true;
            result.SafetyMessage = safetyMessage;
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                result.BackupPath = backupPath;
            }
        }

        private static string NormalizeAction(string? action, string defaultAction)
        {
            return string.IsNullOrWhiteSpace(action) ? defaultAction : action!.Trim();
        }

        private bool TryGetItemPropertyValue(HmiScreenItemBase item, string propertyName, out string? value)
        {
            var property = _adapter.GetItemProperties(item, new[] { propertyName })
                .FirstOrDefault();
            value = property?.Value;
            return property != null;
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
                var property = screen.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null || !property.CanRead)
                {
                    return false;
                }

                var rawValue = property.GetValue(screen);
                if (rawValue == null)
                {
                    return false;
                }

                value = rawValue is string stringValue
                    ? stringValue
                    : Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetScreenProperty(HmiScreen screen, string propertyName, string value, out string? error)
        {
            error = null;

            try
            {
                switch (propertyName)
                {
                    case "Width":
                        screen.Width = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                        return true;
                    case "Height":
                        screen.Height = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                        return true;
                    case "Enabled":
                        screen.Enabled = bool.Parse(value);
                        return true;
                }

                var property = screen.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null || !property.CanWrite)
                {
                    error = $"Property '{propertyName}' is not writable on screens.";
                    return false;
                }

                object convertedValue = value;
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (targetType == typeof(bool))
                {
                    convertedValue = bool.Parse(value);
                }
                else if (targetType == typeof(int))
                {
                    convertedValue = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (targetType == typeof(uint))
                {
                    convertedValue = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (targetType.IsEnum)
                {
                    convertedValue = Enum.Parse(targetType, value, ignoreCase: true);
                }

                property.SetValue(screen, convertedValue);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static bool TryDeleteEngineeringObject(object target, out string? error)
        {
            error = null;

            try
            {
                var method = target.GetType().GetMethod("Delete", BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                {
                    error = $"Type '{target.GetType().Name}' does not expose Delete().";
                    return false;
                }

                method.Invoke(target, null);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static void AddObjectAction(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string? itemType,
            string action,
            string reason)
        {
            result.ObjectActions.Add(new HmiJsonObjectAction
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                ItemType = itemType,
                Action = action,
                Reason = reason
            });
        }

        private static void AddPropertyChange(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string property,
            string action,
            string? currentValue,
            string? desiredValue)
        {
            result.PropertyChanges.Add(new HmiJsonPropertyChange
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                Property = property,
                Action = action,
                CurrentValue = currentValue,
                DesiredValue = desiredValue
            });
        }

        private static void AddSkippedProperty(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string property,
            string reason)
        {
            result.PropertySkippedCount++;
            result.SkippedProperties.Add(new HmiJsonPropertyIssue
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                Property = property,
                Reason = reason
            });
        }

        private static void AddFailedProperty(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string property,
            string reason)
        {
            result.PropertyFailedCount++;
            result.FailedProperties.Add(new HmiJsonPropertyIssue
            {
                Scope = scope,
                ScreenPath = screenPath,
                ItemName = itemName,
                Property = property,
                Reason = reason
            });
        }

        private static void AddWarning(HmiJsonImportResult result, string warning)
        {
            result.Warnings.Add(warning);
        }

        private static string FormatObjectPath(string screenPath, string? itemName)
        {
            return string.IsNullOrWhiteSpace(itemName)
                ? screenPath
                : screenPath + "/" + itemName;
        }

        private static string FormatExceptionMessage(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) &&
                    !messages.Contains(current.Message, StringComparer.Ordinal))
                {
                    messages.Add(current.Message);
                }
            }

            return messages.Count == 0 ? ex.GetType().Name : string.Join(" | Inner: ", messages);
        }

        private static string? SerializeCompositeValue<T>(T? value)
            where T : class
        {
            return value == null ? null : JsonSerializer.Serialize(value);
        }

        private static void TryWriteReport(HmiJsonImportResult result, string inputPath, bool dryRun)
        {
            try
            {
                var reportPath = HmiJsonDocumentStore.ResolveReportFilePath(inputPath, dryRun);
                HmiJsonDocumentStore.SaveImportReport(result, reportPath);
                result.ReportPath = reportPath;
            }
            catch
            {
                // Best-effort only; avoid hiding the main result on report write failures.
            }
        }
    }
}
