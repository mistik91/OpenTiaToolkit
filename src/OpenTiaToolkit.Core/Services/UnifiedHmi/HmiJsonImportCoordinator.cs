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
    internal sealed class HmiJsonImportCoordinator
    {
        private readonly IHmiJsonBackupExporter _backupExporter;
        private readonly IHmiJsonImportTransactionRunner _transactionRunner;
        private readonly IHmiJsonImportReportWriter _reportWriter;

        public HmiJsonImportCoordinator(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, HmiJsonExportService exportService, ILogger? logger)
            : this(
                sessionContext,
                new HmiJsonBackupExporter(exportService),
                new HmiJsonDocumentExecutor(sessionContext, adapter, exportService, logger),
                reportWriter: new HmiJsonImportReportWriter(),
                transactionRunner: null,
                logger: logger)
        {
        }

        internal HmiJsonImportCoordinator(
            EngineeringSessionContext sessionContext,
            IHmiJsonBackupExporter backupExporter,
            IHmiJsonDocumentExecutor executor,
            IHmiJsonImportReportWriter reportWriter,
            IHmiJsonImportTransactionRunner? transactionRunner,
            ILogger? logger)
        {
            if (sessionContext == null) throw new ArgumentNullException(nameof(sessionContext));
            _backupExporter = backupExporter ?? throw new ArgumentNullException(nameof(backupExporter));
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
            _transactionRunner = transactionRunner ?? new HmiJsonImportTransactionRunner(sessionContext, executor, reportWriter, logger);
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
                return await _transactionRunner.RunPreflightAsync(inputPath, filters, options);
            }

            HmiJsonImportResult? gatePreflightResult = null;
            if (options.RequireCleanPreflight || options.FailOnWarning)
            {
                gatePreflightResult = await _transactionRunner.RunPreflightAsync(inputPath, filters, options);
                if (gatePreflightResult == null)
                {
                    var missingPreflight = CreateSafetyBlockedResult(inputPath, dryRun: false, "Import was blocked because the safety preflight could not be completed.");
                    _reportWriter.TryWrite(missingPreflight, inputPath, dryRun: false);
                    return missingPreflight;
                }

                var blockingMessage = GetBlockingSafetyGateMessage(gatePreflightResult, options);
                if (gatePreflightResult.SafetyBlocked || !string.IsNullOrWhiteSpace(blockingMessage))
                {
                    if (!gatePreflightResult.SafetyBlocked)
                    {
                        MarkSafetyBlocked(gatePreflightResult, blockingMessage!, backupPath: null);
                    }

                    return gatePreflightResult;
                }
            }

            string? backupOutputPath = null;
            if (options.BackupBeforeImport)
            {
                var backupTargetPath = HmiJsonDocumentStore.ResolveBackupExportPath(inputPath, options.BackupPath);
                var backupResult = await _backupExporter.ExportAsync(backupTargetPath, filters);
                if (backupResult == null || string.IsNullOrWhiteSpace(backupResult.OutputPath))
                {
                    var backupFailure = CreateSafetyBlockedResult(
                        inputPath,
                        dryRun: false,
                        "Import was blocked because the backup export could not be created.");
                    _reportWriter.TryWrite(backupFailure, inputPath, dryRun: false);
                    return backupFailure;
                }

                backupOutputPath = backupResult.OutputPath;
            }

            return await _transactionRunner.RunAsync(inputPath, filters, options, backupOutputPath);
        }

    }
}
