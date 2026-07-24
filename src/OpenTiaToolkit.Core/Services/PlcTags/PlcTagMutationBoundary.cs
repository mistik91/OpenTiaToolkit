using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using System;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    /// <summary>
    /// Serialized, per-tag mutation boundary for comment application and compensating recreation.
    /// </summary>
    internal sealed class PlcTagMutationBoundary
    {
        private readonly IPlcTagRepository _repository;
        private readonly ILogger? _logger;

        public PlcTagMutationBoundary(IPlcTagRepository repository, ILogger? logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger;
        }

        public bool TrySetComment(object tag, string comment, string culture = "en-US")
        {
            var success = _repository.TrySetTagComment(tag, comment, culture);
            if (success)
            {
                _logger?.LogDebug("Successfully set comment for culture: {Culture}", culture);
                return true;
            }

            _logger?.LogWarning(
                "Culture '{Culture}' is not enabled in the TIA Portal Project Languages. Cannot set comment.",
                culture);
            return false;
        }

        public bool TryRestore(
            object tagTable,
            TagSnapshot snapshot,
            out object? restoredTag,
            out string message)
        {
            try
            {
                if (!_repository.TryCreateTag(
                    tagTable,
                    snapshot.Name,
                    snapshot.DataType,
                    snapshot.LogicalAddress,
                    out restoredTag,
                    out var failureReason))
                {
                    message = $"CRITICAL: restoring the original tag failed: {failureReason ?? "tag creation failed"}.";
                    return false;
                }

                if (snapshot.Comment != null && !TrySetComment(restoredTag!, snapshot.Comment))
                {
                    message = "The original tag was restored, but its comment could not be restored.";
                    return true;
                }

                message = "The original tag was restored.";
                return true;
            }
            catch (Exception ex)
            {
                restoredTag = null;
                message = $"CRITICAL: restoring the original tag failed: {ex.GetType().Name}: {ex.Message}.";
                return false;
            }
        }
    }
}
