using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using static OpenTiaToolkit.Core.Services.HmiJsonImportPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal interface IHmiJsonDocumentExecutor
    {
        Task<HmiJsonImportResult?> ImportCoreAsync(string inputPath, HmiJsonFilterOptions? filters, bool dryRun);
    }

    internal interface IHmiJsonBackupExporter
    {
        Task<HmiJsonExportResult?> ExportAsync(string outputPath, HmiJsonFilterOptions? filters);
    }

    internal interface IHmiJsonImportReportWriter
    {
        void TryWrite(HmiJsonImportResult result, string inputPath, bool dryRun);
    }

    internal interface IHmiJsonImportTransactionRunner
    {
        Task<HmiJsonImportResult> RunAsync(
            string inputPath,
            HmiJsonFilterOptions? filters,
            HmiJsonImportExecutionOptions options,
            string? backupOutputPath);
    }

    internal sealed class HmiJsonBackupExporter : IHmiJsonBackupExporter
    {
        private readonly HmiJsonExportService _exportService;

        internal HmiJsonBackupExporter(HmiJsonExportService exportService)
        {
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        }

        public Task<HmiJsonExportResult?> ExportAsync(string outputPath, HmiJsonFilterOptions? filters) =>
            _exportService.ExportAsync(outputPath, filters);
    }

    internal sealed class HmiJsonImportTransactionRunner : IHmiJsonImportTransactionRunner
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IHmiJsonDocumentExecutor _executor;
        private readonly IHmiJsonImportReportWriter _reportWriter;
        private readonly ILogger? _logger;
        private readonly HmiJsonImportTransactionPolicy _policy;

        internal HmiJsonImportTransactionRunner(
            EngineeringSessionContext sessionContext,
            IHmiJsonDocumentExecutor executor,
            IHmiJsonImportReportWriter reportWriter,
            ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
            _logger = logger;
            _policy = new HmiJsonImportTransactionPolicy();
        }

        public async Task<HmiJsonImportResult> RunAsync(
            string inputPath,
            HmiJsonFilterOptions? filters,
            HmiJsonImportExecutionOptions options,
            string? backupOutputPath)
        {
            if (_sessionContext.Portal == null || _sessionContext.Project == null)
            {
                return CreateSafetyBlockedResult(
                    inputPath,
                    dryRun: false,
                    "Import was blocked because no TIA Portal project is selected.");
            }

            const string transactionDescription = "OpenTiaToolkit Unified HMI JSON import";
            try
            {
                using (var access = _sessionContext.Portal!.ExclusiveAccess(transactionDescription))
                using (var transaction = access.Transaction(_sessionContext.Project!, transactionDescription))
                {
                    var imported = await _executor.ImportCoreAsync(inputPath, filters, dryRun: false);
                    var result = _policy.Complete(
                        inputPath,
                        imported,
                        options,
                        backupOutputPath,
                        transaction.CommitOnDispose);
                    _reportWriter.TryWrite(result, inputPath, dryRun: false);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unified HMI JSON import transaction failed");
                var result = _policy.FromException(inputPath, ex, backupOutputPath);
                _reportWriter.TryWrite(result, inputPath, dryRun: false);
                return result;
            }
        }
    }
}
