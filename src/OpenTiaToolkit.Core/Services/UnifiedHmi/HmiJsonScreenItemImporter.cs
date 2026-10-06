using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using static OpenTiaToolkit.Core.Services.HmiJsonImportPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonScreenItemImporter
    {
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly HmiJsonCompositeImportCoordinator _composites;
        private readonly HmiJsonScalarPropertyImporter _scalars;
        private readonly HmiScreenItemCreationPipeline _creationPipeline;

        internal HmiJsonScreenItemImporter(
            V21UnifiedHmiAdapter adapter,
            HmiJsonImportPrimitives primitives,
            HmiJsonCompositeImportCoordinator composites)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            if (primitives == null) throw new ArgumentNullException(nameof(primitives));
            _composites = composites ?? throw new ArgumentNullException(nameof(composites));
            _scalars = new HmiJsonScalarPropertyImporter(adapter, primitives);
            _creationPipeline = new HmiScreenItemCreationPipeline(adapter);
        }

        internal void ProcessScreenDocument(
            HmiSoftware software,
            HmiJsonScreen screenDocument,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            var screenPath = screenDocument.Path;
            var screenAction = NormalizeAction(screenDocument.Action, defaultAction: "Update");
            var screenExists = _adapter.TryGetScreen(software, screenPath, out var existingScreen, out _) && existingScreen != null;
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
            _composites.ProcessEventHandlers(
                scope: "Screen",
                screenPath,
                itemName: null,
                screenDocument.EventHandlers,
                screen,
                typeof(HmiScreen),
                filters,
                dryRun,
                result);
            _composites.ProcessPropertyEventHandlers(
                scope: "Screen",
                screenPath,
                itemName: null,
                screenDocument.PropertyEventHandlers,
                screen,
                typeof(HmiScreen),
                filters,
                dryRun,
                result);
            _composites.ProcessDynamizations(
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
            _scalars.ProcessScreenProperties(screenPath, properties, screen, filters, dryRun, result);
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
            HmiScreenItemBase? temporaryDryRunItem = null;
            IReadOnlyDictionary<string, string?>? originalCreationPropertyValues = null;

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
                if (dryRun)
                {
                    var creationResult = TryCreateItem(
                        screen,
                        software,
                        itemDocument,
                        filters,
                        $"__OpenTiaToolkit_Preflight_{Guid.NewGuid():N}",
                        result,
                        screenPath);
                    temporaryDryRunItem = creationResult?.Item;
                    originalCreationPropertyValues = creationResult?.OriginalCreationPropertyValues;
                    existingItem = temporaryDryRunItem;
                }
                else
                {
                    if (screen == null)
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' could not be created because the screen could not be resolved.");
                        return;
                    }

                    var creationResult = TryCreateItem(
                        screen,
                        software,
                        itemDocument,
                        filters,
                        itemDocument.Name,
                        result,
                        screenPath);
                    existingItem = creationResult?.Item;
                    originalCreationPropertyValues = creationResult?.OriginalCreationPropertyValues;
                    if (existingItem == null)
                    {
                        return;
                    }

                    if (HasInvalidItemProperties(
                        screenPath,
                        itemDocument,
                        existingItem,
                        software,
                        filters,
                        result,
                        originalCreationPropertyValues))
                    {
                        if (!TryDeleteEngineeringObject(existingItem, out var deleteError))
                        {
                            AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' was created during validation but could not be removed after validation failed: {deleteError}");
                        }

                        return;
                    }
                }
            }
            else if (requiresRecreate)
            {
                result.ItemRecreated++;
                AddObjectAction(result, "Item", screenPath, itemDocument.Name, itemDocument.Type, "Recreate", dryRun ? "Item would be recreated because the type changed." : "Item was recreated because the type changed.");
                if (dryRun)
                {
                    var creationResult = TryCreateItem(
                        screen,
                        software,
                        itemDocument,
                        filters,
                        $"__OpenTiaToolkit_Preflight_{Guid.NewGuid():N}",
                        result,
                        screenPath);
                    temporaryDryRunItem = creationResult?.Item;
                    originalCreationPropertyValues = creationResult?.OriginalCreationPropertyValues;
                    existingItem = temporaryDryRunItem;
                }
                else
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

                    var creationResult = TryCreateItem(
                        screen,
                        software,
                        itemDocument,
                        filters,
                        itemDocument.Name,
                        result,
                        screenPath);
                    existingItem = creationResult?.Item;
                    originalCreationPropertyValues = creationResult?.OriginalCreationPropertyValues;
                    if (existingItem == null)
                    {
                        return;
                    }

                    if (HasInvalidItemProperties(
                        screenPath,
                        itemDocument,
                        existingItem,
                        software,
                        filters,
                        result,
                        originalCreationPropertyValues))
                    {
                        AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' was recreated but property validation failed after the original item was deleted.");
                        return;
                    }
                }
            }
            else if (string.Equals(itemAction, "Create", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(result, $"Item '{screenPath}/{itemDocument.Name}' already exists; create action is being treated as update.");
            }

            try
            {
                ProcessItemProperties(
                    screenPath,
                    itemDocument,
                    existingItem,
                    software,
                    filters,
                    dryRun,
                    result,
                    originalCreationPropertyValues);
                var itemTargetType = existingItem?.GetType();
                if (itemTargetType == null && !_adapter.TryResolveItemClrType(itemDocument.Type, out itemTargetType))
                {
                    itemTargetType = typeof(HmiScreenItemBase);
                }

                _composites.ProcessEventHandlers(
                    scope: "Item",
                    screenPath,
                    itemDocument.Name,
                    itemDocument.EventHandlers,
                    existingItem,
                    itemTargetType,
                    filters,
                    dryRun,
                    result);
                _composites.ProcessPropertyEventHandlers(
                    scope: "Item",
                    screenPath,
                    itemDocument.Name,
                    itemDocument.PropertyEventHandlers,
                    existingItem,
                    itemTargetType,
                    filters,
                    dryRun,
                    result);
                _composites.ProcessDynamizations(
                    scope: "Item",
                    screenPath,
                    itemDocument.Name,
                    itemDocument.Dynamizations,
                    existingItem,
                    itemTargetType,
                    filters,
                    dryRun,
                    result);
                _composites.ProcessInterfaceBindings(
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
            finally
            {
                if (temporaryDryRunItem != null && !TryDeleteEngineeringObject(temporaryDryRunItem, out var deleteError))
                {
                    AddWarning(result, $"Temporary dry-run item for '{screenPath}/{itemDocument.Name}' could not be removed: {deleteError}");
                }
            }
        }

        private void ProcessItemProperties(
            string screenPath,
            HmiJsonItem itemDocument,
            HmiScreenItemBase? item,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result,
            IReadOnlyDictionary<string, string?>? originalCreationPropertyValues)
        {
            _scalars.ProcessItemProperties(
                screenPath,
                itemDocument,
                item,
                software,
                filters,
                dryRun,
                result,
                originalCreationPropertyValues);
        }

        private HmiScreenItemCreationResult? TryCreateItem(
            HmiScreen? screen,
            HmiSoftware software,
            HmiJsonItem itemDocument,
            HmiJsonFilterOptions filters,
            string itemName,
            HmiJsonImportResult result,
            string screenPath)
        {
            if (screen == null)
            {
                return null;
            }

            var creationProperties = itemDocument.Properties
                .Where(property =>
                    property.Value != null &&
                    HmiJsonFilterMatcher.ShouldIncludeProperty(property.Key, filters))
                .ToDictionary(
                    property => property.Key,
                    property => property.Value,
                    StringComparer.OrdinalIgnoreCase);
            var creationResult = _creationPipeline.Create(
                screen,
                software,
                itemDocument.Type,
                itemName,
                creationProperties);
            if (!creationResult.Succeeded)
            {
                AddWarning(
                    result,
                    $"Item '{screenPath}/{itemDocument.Name}' could not be initialized as type '{itemDocument.Type}': {creationResult.Error}");
                return null;
            }

            return creationResult;
        }

        private bool HasInvalidItemProperties(
            string screenPath,
            HmiJsonItem itemDocument,
            HmiScreenItemBase item,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            HmiJsonImportResult result,
            IReadOnlyDictionary<string, string?>? initializedCreationProperties)
        {
            var hasInvalidProperties = false;
            foreach (var property in itemDocument.Properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!HmiJsonFilterMatcher.ShouldIncludeProperty(property.Key, filters) || property.Value == null)
                {
                    continue;
                }

                if (initializedCreationProperties != null &&
                    initializedCreationProperties.ContainsKey(property.Key))
                {
                    continue;
                }

                if (!_adapter.TryValidateItemProperty(item, property.Key, property.Value, software, out var validationError))
                {
                    AddFailedProperty(result, "Item", screenPath, itemDocument.Name, property.Key, validationError ?? "Item property could not be validated.");
                    hasInvalidProperties = true;
                }
            }

            return hasInvalidProperties;
        }

    }
}
