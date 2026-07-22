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

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonImportPrimitives
    {
        internal const string EventHandlersPropertyName = "EventHandlers";
        internal const string PropertyEventHandlersPropertyName = "PropertyEventHandlers";
        internal const string DynamizationsPropertyName = "Dynamizations";
        internal const string InterfacePropertyName = "Interface";
        internal const string TiaOnlineModeBlockedReason = "tia-online-mode";
        private readonly HmiJsonItemPropertyReader _itemProperties;

        internal HmiJsonImportPrimitives(V21UnifiedHmiAdapter adapter)
        {
            _itemProperties = new HmiJsonItemPropertyReader(adapter);
        }

        internal static List<HmiJsonEventHandler> MergeFilteredEventHandlers(
            IReadOnlyList<HmiJsonEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters)
            => HmiJsonCompositeMergeRules.MergeEventHandlers(currentHandlers, filteredHandlers, filters);

        internal static List<HmiJsonPropertyEventHandler> MergeFilteredPropertyEventHandlers(
            IReadOnlyList<HmiJsonPropertyEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonPropertyEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters)
            => HmiJsonCompositeMergeRules.MergePropertyEventHandlers(currentHandlers, filteredHandlers, filters);

        internal static List<HmiJsonDynamization> MergeFilteredDynamizations(
            IReadOnlyList<HmiJsonDynamization> currentDynamizations,
            IReadOnlyList<HmiJsonDynamization> filteredDynamizations,
            HmiJsonFilterOptions filters)
            => HmiJsonCompositeMergeRules.MergeDynamizations(currentDynamizations, filteredDynamizations, filters);

        internal static List<HmiJsonInterfaceBinding> MergeFilteredInterfaceBindings(
            IReadOnlyList<HmiJsonInterfaceBinding> currentBindings,
            IReadOnlyList<HmiJsonInterfaceBinding> filteredBindings,
            HmiJsonFilterOptions filters)
            => HmiJsonCompositeMergeRules.MergeInterfaceBindings(currentBindings, filteredBindings, filters);

        internal static void ApplySafetyPolicy(
            HmiJsonImportResult? result,
            HmiJsonImportExecutionOptions options,
            string? backupPath = null)
            => HmiJsonImportSafetyPolicy.Apply(result, options, backupPath);

        internal static string? GetBlockingSafetyGateMessage(HmiJsonImportResult result, HmiJsonImportExecutionOptions options)
            => HmiJsonImportSafetyPolicy.GetBlockingMessage(result, options);

        internal static bool ShouldPreserveUnfilteredCompositeEntries(string propertyName, HmiJsonFilterOptions filters)
            => HmiJsonCompositeMergeRules.ShouldPreserveUnfilteredEntries(propertyName, filters);

        internal static bool IsOnlineModeTransactionFailure(Exception ex)
            => HmiJsonImportSafetyPolicy.IsOnlineModeFailure(ex);

        internal static string CreateOnlineModeSafetyMessage(string? detail)
            => HmiJsonImportSafetyPolicy.CreateOnlineModeMessage(detail);

        internal static HmiJsonImportResult CreateSafetyBlockedResult(string inputPath, bool dryRun, string safetyMessage, string? blockedReason = null)
            => HmiJsonImportSafetyPolicy.CreateBlockedResult(inputPath, dryRun, safetyMessage, blockedReason);

        internal static void MarkSafetyBlocked(HmiJsonImportResult result, string safetyMessage, string? backupPath, string? blockedReason = null)
            => HmiJsonImportSafetyPolicy.MarkBlocked(result, safetyMessage, backupPath, blockedReason);

        internal static string NormalizeAction(string? action, string defaultAction)
        {
            return string.IsNullOrWhiteSpace(action) ? defaultAction : action!.Trim();
        }

        internal bool TryGetItemPropertyValue(HmiScreenItemBase item, string propertyName, out string? value)
            => _itemProperties.TryGetValue(item, propertyName, out value);

        internal static bool TryGetScreenPropertyValue(HmiScreen screen, string propertyName, out string? value)
            => HmiJsonScreenPropertyAccessor.TryGetValue(screen, propertyName, out value);

        internal static bool TrySetScreenProperty(HmiScreen screen, string propertyName, string value, out string? error)
            => HmiJsonScreenPropertyAccessor.TrySetValue(screen, propertyName, value, out error);

        internal static bool TryValidateScreenProperty(HmiScreen screen, string propertyName, string value, out string? error)
            => HmiJsonScreenPropertyAccessor.TryValidateValue(screen, propertyName, value, out error);

        internal static bool TryDeleteEngineeringObject(object target, out string? error)
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

        internal static void AddObjectAction(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string? itemType,
            string action,
            string reason)
            => HmiJsonImportResultBuilder.AddObjectAction(result, scope, screenPath, itemName, itemType, action, reason);

        internal static void AddPropertyChange(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string property,
            string action,
            string? currentValue,
            string? desiredValue)
            => HmiJsonImportResultBuilder.AddPropertyChange(result, scope, screenPath, itemName, property, action, currentValue, desiredValue);

        internal static void AddSkippedProperty(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string property,
            string reason)
            => HmiJsonImportResultBuilder.AddSkippedProperty(result, scope, screenPath, itemName, property, reason);

        internal static void AddFailedProperty(
            HmiJsonImportResult result,
            string scope,
            string screenPath,
            string? itemName,
            string property,
            string reason)
            => HmiJsonImportResultBuilder.AddFailedProperty(result, scope, screenPath, itemName, property, reason);

        internal static void AddWarning(HmiJsonImportResult result, string warning)
            => HmiJsonImportResultBuilder.AddWarning(result, warning);

        internal static string FormatObjectPath(string screenPath, string? itemName)
            => HmiJsonImportResultBuilder.FormatObjectPath(screenPath, itemName);

        internal static string FormatExceptionMessage(Exception ex)
            => HmiJsonImportResultBuilder.FormatExceptionMessage(ex);

        internal static string? SerializeCompositeValue<T>(T? value)
            where T : class
            => HmiJsonImportResultBuilder.SerializeCompositeValue(value);

    }
}
