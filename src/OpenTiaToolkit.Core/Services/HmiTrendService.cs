using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Commands.Hmi;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Unified HMI trend and logging configuration operations.
    /// </summary>
    internal sealed class HmiTrendService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly ILogger? _logger;

        public HmiTrendService(EngineeringSessionContext sessionContext, V21UnifiedHmiAdapter adapter, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _logger = logger;
        }

        public async Task<HmiTrendInfo?> CreateTrendAsync(
            string tagPath,
            string dataLogName,
            bool cyclic,
            string? cycle,
            string storageFolder,
            int logMaxSize,
            int durationDays,
            int segmentMaxSize)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                if (!_adapter.TryGetTag(software, tagPath, out HmiTag? tag, out var normalizedTagPath) || tag == null)
                {
                    _logger?.LogWarning("Unified HMI tag not found: {TagPath}", tagPath);
                    return null;
                }

                HmiDataLog dataLog = _adapter.EnsureDataLog(software, dataLogName, out var dataLogCreated);
                _adapter.ConfigureDataLog(
                    dataLog,
                    Math.Max(1, logMaxSize),
                    Math.Max(1, durationDays),
                    Math.Max(1, segmentMaxSize));

                if (!string.IsNullOrWhiteSpace(storageFolder) &&
                    !string.Equals(storageFolder.Trim(), "/Trends", StringComparison.Ordinal))
                {
                    _logger?.LogWarning(
                        "Unified HMI storage folder is read-only; requested folder '{StorageFolder}' was not applied.",
                        storageFolder.Trim());
                }

                var loggingTagName = _adapter.GetLoggingTagName(normalizedTagPath);
                HmiLoggingTag loggingTag = _adapter.EnsureLoggingTag(tag, loggingTagName, out var loggingTagCreated);

                var loggingMode = cyclic ? HmiLoggingMode.Cyclic : HmiLoggingMode.OnChange;
                var effectiveCycle = cyclic
                    ? string.IsNullOrWhiteSpace(cycle) ? "T5s" : cycle!.Trim()
                    : null;

                _adapter.ConfigureLoggingTag(loggingTag, dataLog.Name, loggingMode, effectiveCycle);

                return _adapter.ToTrendInfo(tag, normalizedTagPath, loggingTag, dataLog, dataLogCreated, loggingTagCreated);
            });
        }
    }
}
