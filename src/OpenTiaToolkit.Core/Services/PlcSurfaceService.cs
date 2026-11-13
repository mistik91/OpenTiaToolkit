using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade for the PLC surface capabilities exposed by <see cref="PlcService"/>.
    /// </summary>
    internal sealed class PlcSurfaceService
    {
        private readonly PlcTagExportService _tags;
        private readonly PlcCrossReferenceService _crossReferences;
        private readonly PlcAncillarySurfaceReader _surfaces;
        private readonly PlcExternalSourceService _sources;

        public PlcSurfaceService(
            EngineeringSessionContext sessionContext,
            PlcBrowseService browseService,
            ILogger? logger,
            V21TypeServiceResolver types,
            V21PlcTagStructureAccess tags,
            V21EngineeringObjectAccess objects)
            : this(
                new PlcTagExportService(sessionContext, tags, objects),
                new PlcCrossReferenceService(sessionContext, types, tags, objects),
                new PlcAncillarySurfaceReader(sessionContext),
                new PlcExternalSourceService(sessionContext, browseService))
        {
            _ = logger;
        }

        internal PlcSurfaceService(
            PlcTagExportService tags,
            PlcCrossReferenceService crossReferences,
            PlcAncillarySurfaceReader surfaces,
            PlcExternalSourceService sources)
        {
            _tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _crossReferences = crossReferences ?? throw new ArgumentNullException(nameof(crossReferences));
            _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        }

        public Task<PlcTagExportResult> ExportTagsAsync(string culture = "en-US") =>
            _tags.ExportAsync(culture);

        public Task<PlcCrossReferenceResult> GetCrossReferencesAsync(string name) =>
            _crossReferences.GetAsync(name);
        internal Func<string, Task<PlcCrossReferenceResult>> CreateCrossReferenceLookup() =>
            _crossReferences.CreateLookup();

        public Task<IReadOnlyList<PlcSurfaceItem>> ListExternalSourcesAsync() =>
            _surfaces.ListExternalSourcesAsync();

        public Task<IReadOnlyList<PlcSurfaceItem>> ListWatchTablesAsync() =>
            _surfaces.ListWatchTablesAsync();

        public Task<IReadOnlyList<PlcSurfaceItem>> ListTechnologyObjectsAsync() =>
            _surfaces.ListTechnologyObjectsAsync();

        public Task<IReadOnlyList<PlcSurfaceItem>> ListPlcAlarmTextListsAsync() =>
            _surfaces.ListPlcAlarmTextListsAsync();

        public Task<PlcSourceOperationResult> ImportSourceAsync(string sourcePath, bool dryRun = true, bool confirm = false) =>
            _sources.ImportAsync(sourcePath, dryRun, confirm);

        public Task<PlcSourceOperationResult> GenerateBlocksFromSourceAsync(string sourceName, bool dryRun = true, bool confirm = false) =>
            _sources.GenerateBlocksAsync(sourceName, dryRun, confirm);

        public Task<PlcSourceOperationResult> DeleteExternalSourceAsync(string sourceName, bool confirm = false) =>
            _sources.DeleteAsync(sourceName, confirm);

        public Task<PlcSourceOperationResult> ExportBlockAsSourceAsync(string blockName, string outputPath) =>
            _sources.ExportBlockAsync(blockName, outputPath);
    }
}
