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
    internal sealed class HmiJsonDocumentExecutor : IHmiJsonDocumentExecutor
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger? _logger;
        private readonly HmiJsonScreenItemImporter _screenItems;

        internal HmiJsonDocumentExecutor(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, HmiJsonExportService exportService, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            if (exportService == null) throw new ArgumentNullException(nameof(exportService));
            _logger = logger;
            var primitives = new HmiJsonImportPrimitives(adapter);
            var composites = new HmiJsonCompositeImportCoordinator(adapter);
            _screenItems = new HmiJsonScreenItemImporter(adapter, primitives, composites);
        }

        public async Task<HmiJsonImportResult?> ImportCoreAsync(string inputPath, HmiJsonFilterOptions? filters, bool dryRun)
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
                    _screenItems.ProcessScreenDocument(software, screenDocument, effectiveFilters, dryRun, result);
                }

                result.WarningCount = result.Warnings.Count;
                TryWriteReport(result, inputPath, dryRun);
                return result;
            });
        }

    }
}
