using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility and composition facade for Unified HMI JSON import.
    /// </summary>
    internal sealed class HmiJsonImportService
    {
        internal const string TiaOnlineModeBlockedReason = HmiJsonImportPrimitives.TiaOnlineModeBlockedReason;
        private readonly HmiJsonImportCoordinator _coordinator;

        public HmiJsonImportService(
            EngineeringSessionContext sessionContext,
            V21UnifiedHmiAdapter adapter,
            HmiJsonExportService exportService,
            ILogger? logger)
        {
            _coordinator = new HmiJsonImportCoordinator(sessionContext, adapter, exportService, logger);
        }

        public Task<HmiJsonImportResult?> ImportAsync(
            string inputPath,
            HmiJsonFilterOptions? filters,
            bool dryRun,
            HmiJsonImportExecutionOptions? executionOptions) =>
            _coordinator.ImportAsync(inputPath, filters, dryRun, executionOptions);

        internal static List<HmiJsonEventHandler> MergeFilteredEventHandlers(
            IReadOnlyList<HmiJsonEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters) =>
            HmiJsonImportPrimitives.MergeFilteredEventHandlers(currentHandlers, filteredHandlers, filters);

        internal static List<HmiJsonPropertyEventHandler> MergeFilteredPropertyEventHandlers(
            IReadOnlyList<HmiJsonPropertyEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonPropertyEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters) =>
            HmiJsonImportPrimitives.MergeFilteredPropertyEventHandlers(currentHandlers, filteredHandlers, filters);

        internal static List<HmiJsonDynamization> MergeFilteredDynamizations(
            IReadOnlyList<HmiJsonDynamization> currentDynamizations,
            IReadOnlyList<HmiJsonDynamization> filteredDynamizations,
            HmiJsonFilterOptions filters) =>
            HmiJsonImportPrimitives.MergeFilteredDynamizations(currentDynamizations, filteredDynamizations, filters);

        internal static List<HmiJsonInterfaceBinding> MergeFilteredInterfaceBindings(
            IReadOnlyList<HmiJsonInterfaceBinding> currentBindings,
            IReadOnlyList<HmiJsonInterfaceBinding> filteredBindings,
            HmiJsonFilterOptions filters) =>
            HmiJsonImportPrimitives.MergeFilteredInterfaceBindings(currentBindings, filteredBindings, filters);

        internal static void ApplySafetyPolicy(HmiJsonImportResult? result, HmiJsonImportExecutionOptions options, string? backupPath = null) =>
            HmiJsonImportPrimitives.ApplySafetyPolicy(result, options, backupPath);

        internal static string? GetBlockingSafetyGateMessage(HmiJsonImportResult result, HmiJsonImportExecutionOptions options) =>
            HmiJsonImportPrimitives.GetBlockingSafetyGateMessage(result, options);

        internal static bool IsOnlineModeTransactionFailure(Exception ex) =>
            HmiJsonImportPrimitives.IsOnlineModeTransactionFailure(ex);
    }
}
