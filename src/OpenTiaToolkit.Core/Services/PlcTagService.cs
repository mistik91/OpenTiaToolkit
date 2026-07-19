using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.PlcTags;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade that composes the cohesive PLC-tag workflows.
    /// </summary>
    internal sealed class PlcTagService
    {
        private readonly PlcTagFindReplaceWorkflow _findReplace;
        private readonly PlcTagCreationWorkflow _creation;
        private readonly PlcTagModificationWorkflow _modification;
        private readonly PlcTagCsvWorkflow _csv;

        public PlcTagService(
            EngineeringSessionContext sessionContext,
            ILogger? logger,
            V21OpennessAdapter? v21Adapter = null,
            IPlcTagNamingStrategy? tagNamingStrategy = null)
        {
            if (sessionContext == null)
            {
                throw new ArgumentNullException(nameof(sessionContext));
            }

            var repository = new V21PlcTagRepository(v21Adapter ?? new V21OpennessAdapter());
            var namingStrategy = tagNamingStrategy ?? new PageBasedPlcTagNamingStrategy();
            _findReplace = new PlcTagFindReplaceWorkflow(sessionContext, repository, logger);
            _creation = new PlcTagCreationWorkflow(sessionContext, repository, logger);
            _modification = new PlcTagModificationWorkflow(sessionContext, repository, logger);
            _csv = new PlcTagCsvWorkflow(sessionContext, repository, namingStrategy, logger);
        }

        public Task<FindReplaceResult> FindAndReplaceTagsAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            bool substringMatch = false,
            CancellationToken cancellationToken = default,
            IProgress<BatchOperationProgress>? progress = null) =>
            _findReplace.FindAndReplaceTagsAsync(
                renameMap,
                caseInsensitive,
                dryRun,
                substringMatch,
                cancellationToken,
                progress);

        public Task<CreateTagResult> CreateTagAsync(
            string tagName,
            string dataType,
            string? logicalAddress = null,
            string? tagTableName = null) =>
            _creation.CreateTagAsync(tagName, dataType, logicalAddress, tagTableName);

        public Task<TagModificationResult> ModifyTagsAsync(
            IReadOnlyList<TagModificationOperation> operations,
            bool dryRun = true,
            CancellationToken cancellationToken = default,
            IProgress<BatchOperationProgress>? progress = null) =>
            _modification.ModifyTagsAsync(operations, dryRun, cancellationToken, progress);

        public Task<TagUpdateResult> UpdateTagsFromCsvAsync(string csvFilePath, bool dryRun = true) =>
            _csv.UpdateTagsFromCsvAsync(csvFilePath, dryRun);
    }
}
