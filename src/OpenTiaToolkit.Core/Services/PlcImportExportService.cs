using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade retaining the import/export surface consumed by <see cref="PlcService"/>.
    /// </summary>
    internal sealed class PlcImportExportService
    {
        private readonly PlcXmlTransferService _xml;
        private readonly PlcDocumentTransferService _documents;

        public PlcImportExportService(
            EngineeringSessionContext sessionContext,
            IPortalService portalService,
            PlcBrowseService browseService,
            ILogger? logger,
            V21DocumentAccess documents,
            V21EngineeringObjectAccess objects)
            : this(
                new PlcXmlTransferService(sessionContext, logger, documents, objects),
                new PlcDocumentTransferService(sessionContext, portalService, browseService, logger, documents))
        {
        }

        internal PlcImportExportService(PlcXmlTransferService xml, PlcDocumentTransferService documents)
        {
            _xml = xml ?? throw new ArgumentNullException(nameof(xml));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        }

        public Task<bool> ExportPlcAsync(string exportPath) =>
            _xml.ExportPlcAsync(exportPath);

        public Task<bool> ExportBlockAsync(string blockName, string exportPath) =>
            _xml.ExportBlockAsync(blockName, exportPath);

        public Task<ExportResult> ExportBlocksAsDocumentsAsync(string? filterRegex, string exportPath) =>
            _documents.ExportBlocksAsync(filterRegex, exportPath);

        public Task<bool> ExportBlockAsDocumentsAsync(string blockName, string exportPath) =>
            _documents.ExportBlockAsync(blockName, exportPath);

        public Task<bool> ExportTypeAsync(string typeName, string exportPath) =>
            _xml.ExportTypeAsync(typeName, exportPath);

        public Task<ExportResult> ExportTypesAsync(string? filterRegex, string exportPath) =>
            _xml.ExportTypesAsync(filterRegex, exportPath);

        public Task<bool> ImportBlockAsync(string importPath, string? groupPath = null) =>
            _xml.ImportBlockAsync(importPath, groupPath);

        public Task<ImportResult> ImportBlocksAsync(string importDirectory, string? filterRegex = null, string? groupPath = null) =>
            _xml.ImportBlocksAsync(importDirectory, filterRegex, groupPath);

        public Task<bool> ImportTypeAsync(string importPath, string? groupPath = null) =>
            _xml.ImportTypeAsync(importPath, groupPath);

        public Task<ImportResult> ImportTypesAsync(string importDirectory, string? filterRegex = null, string? groupPath = null) =>
            _xml.ImportTypesAsync(importDirectory, filterRegex, groupPath);

        public Task<bool> ImportBlockFromDocumentsAsync(
            string importDirectory,
            string fileNameWithoutExtension,
            string? groupPath = null,
            string importOption = "Override") =>
            _documents.ImportBlockAsync(importDirectory, fileNameWithoutExtension, groupPath, importOption);

        public Task<ImportResult> ImportBlocksFromDocumentsAsync(
            string importDirectory,
            string? filterRegex = null,
            string? groupPath = null,
            string importOption = "Override") =>
            _documents.ImportBlocksAsync(importDirectory, filterRegex, groupPath, importOption);
    }
}
