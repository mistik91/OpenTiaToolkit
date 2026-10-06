using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using System;
using System.Collections.Generic;
using System.Linq;
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

                var config = new HmiTrendCreateRequest
                {
                    TagPath = normalizedTagPath,
                    DataLogName = dataLogName,
                    Cyclic = cyclic,
                    Cycle = cycle,
                    StorageFolder = storageFolder,
                    LogMaxSize = logMaxSize,
                    DurationDays = durationDays,
                    SegmentMaxSize = segmentMaxSize
                };
                return ConfigureTrend(tag, normalizedTagPath, config, null);
            });
        }

        public async Task<HmiLoggingTagDeleteResult?> RemoveLoggingTagAsync(
            string tagPath,
            string dataLogName,
            bool confirm,
            string? loggingTagName)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                if (!confirm)
                {
                    return new HmiLoggingTagDeleteResult
                    {
                        TagPath = _adapter.NormalizeTagPath(tagPath),
                        DataLogName = dataLogName,
                        Error = "Deletion requires confirm=true."
                    };
                }

                if (!_adapter.TryGetTag(software, tagPath, out HmiTag? tag, out var normalizedTagPath) || tag == null)
                {
                    return new HmiLoggingTagDeleteResult
                    {
                        TagPath = _adapter.NormalizeTagPath(tagPath),
                        DataLogName = dataLogName,
                        Error = $"Unified HMI tag was not found: {tagPath}"
                    };
                }

                var effectiveLoggingTagName = string.IsNullOrWhiteSpace(loggingTagName)
                    ? _adapter.GetLoggingTagName(normalizedTagPath)
                    : loggingTagName!.Trim();
                var loggingTag = _adapter.FindLoggingTag(tag, effectiveLoggingTagName);
                if (loggingTag == null)
                {
                    return new HmiLoggingTagDeleteResult
                    {
                        TagPath = normalizedTagPath,
                        LoggingTagName = effectiveLoggingTagName,
                        DataLogName = dataLogName,
                        Error = $"Logging tag was not found: {effectiveLoggingTagName}"
                    };
                }

                if (!string.Equals(loggingTag.DataLog, dataLogName, StringComparison.OrdinalIgnoreCase))
                {
                    return new HmiLoggingTagDeleteResult
                    {
                        TagPath = normalizedTagPath,
                        LoggingTagName = loggingTag.Name,
                        DataLogName = loggingTag.DataLog,
                        Error = $"Logging tag '{loggingTag.Name}' references data log '{loggingTag.DataLog}', not '{dataLogName}'."
                    };
                }

                _adapter.DeleteLoggingTag(loggingTag);
                return new HmiLoggingTagDeleteResult
                {
                    TagPath = normalizedTagPath,
                    LoggingTagName = effectiveLoggingTagName,
                    DataLogName = dataLogName,
                    Deleted = true
                };
            });
        }

        public async Task<HmiDataLogDeleteResult?> DeleteDataLogAsync(string dataLogName, bool confirm)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                var result = new HmiDataLogDeleteResult { DataLogName = dataLogName };
                if (!confirm)
                {
                    result.Error = "Deletion requires confirm=true.";
                    return result;
                }

                var dataLog = _adapter.FindDataLog(software, dataLogName);
                if (dataLog == null)
                {
                    result.Error = $"Data log was not found: {dataLogName}";
                    return result;
                }

                result.References = _adapter.ListLoggingTagReferences(software, dataLogName).ToList();
                if (result.References.Count > 0)
                {
                    result.Error = $"Data log '{dataLogName}' is still referenced by {result.References.Count} logging tag(s).";
                    return result;
                }

                _adapter.DeleteDataLog(dataLog);
                result.Deleted = true;
                return result;
            });
        }

        public Task<HmiTrendBulkResult?> CreateTrendsAsync(
            IReadOnlyList<HmiTrendCreateRequest> configs,
            bool dryRun)
        {
            var software = _sessionContext.HmiSoftware;
            if (software == null)
            {
                _logger?.LogWarning("No Unified HMI is currently open");
                return Task.FromResult<HmiTrendBulkResult?>(null);
            }

            var result = NewBulkResult("create-hmi-trends", dryRun, configs.Count);
            var dataLogCache = new Dictionary<string, CachedDataLog>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in configs)
            {
                if (string.IsNullOrWhiteSpace(config.TagPath) || string.IsNullOrWhiteSpace(config.DataLogName))
                {
                    AddFailure(result, config.TagPath, config.DataLogName, "tagPath and dataLogName are required.");
                    continue;
                }

                if (!_adapter.TryGetTag(software, config.TagPath, out var tag, out var normalizedTagPath) || tag == null)
                {
                    AddNotFound(result, config.TagPath, config.DataLogName, $"Unified HMI tag was not found: {config.TagPath}");
                    continue;
                }

                if (dryRun)
                {
                    result.PlannedCount++;
                    result.Results.Add(new HmiTrendBulkItemResult
                    {
                        TagPath = normalizedTagPath,
                        DataLogName = config.DataLogName,
                        LoggingTagName = _adapter.GetLoggingTagName(normalizedTagPath),
                        Status = "Planned",
                        Message = "Trend configuration would be created or updated."
                    });
                    continue;
                }

                try
                {
                    var configured = ConfigureTrend(tag, normalizedTagPath, config, dataLogCache);

                    result.AppliedCount++;
                    result.Results.Add(new HmiTrendBulkItemResult
                    {
                        TagPath = configured.TagPath,
                        DataLogName = configured.DataLogName,
                        LoggingTagName = configured.LoggingTagName,
                        Status = "Applied",
                        Trend = configured
                    });
                }
                catch (Exception ex)
                {
                    AddFailure(result, normalizedTagPath, config.DataLogName, ex.Message);
                }
            }

            return Task.FromResult<HmiTrendBulkResult?>(result);
        }

        private HmiTrendInfo ConfigureTrend(
            HmiTag tag,
            string normalizedTagPath,
            HmiTrendCreateRequest config,
            IDictionary<string, CachedDataLog>? dataLogCache)
        {
            var dataLog = EnsureDataLog(config, dataLogCache, out var dataLogCreated);

            if (!string.IsNullOrWhiteSpace(config.StorageFolder) &&
                !string.Equals(config.StorageFolder.Trim(), "/Trends", StringComparison.Ordinal))
            {
                _logger?.LogWarning(
                    "Unified HMI storage folder is read-only; requested folder '{StorageFolder}' was not applied.",
                    config.StorageFolder.Trim());
            }

            var loggingTagName = _adapter.GetLoggingTagName(normalizedTagPath);
            HmiLoggingTag loggingTag = _adapter.EnsureLoggingTag(tag, loggingTagName, out var loggingTagCreated);

            var loggingMode = config.Cyclic ? HmiLoggingMode.Cyclic : HmiLoggingMode.OnChange;
            var effectiveCycle = config.Cyclic
                ? string.IsNullOrWhiteSpace(config.Cycle) ? "T5s" : config.Cycle!.Trim()
                : null;

            _adapter.ConfigureLoggingTag(loggingTag, dataLog.Name, loggingMode, effectiveCycle);

            return _adapter.ToTrendInfo(tag, normalizedTagPath, loggingTag, dataLog, dataLogCreated, loggingTagCreated);
        }

        private HmiDataLog EnsureDataLog(
            HmiTrendCreateRequest config,
            IDictionary<string, CachedDataLog>? dataLogCache,
            out bool dataLogCreated)
        {
            if (dataLogCache != null && dataLogCache.TryGetValue(config.DataLogName, out var cached))
            {
                dataLogCreated = false;
                return cached.DataLog;
            }

            HmiDataLog dataLog = _adapter.EnsureDataLog(_sessionContext.HmiSoftware!, config.DataLogName, out dataLogCreated);
            _adapter.ConfigureDataLog(
                dataLog,
                Math.Max(1, config.LogMaxSize),
                Math.Max(1, config.DurationDays),
                Math.Max(1, config.SegmentMaxSize));

            if (dataLogCache != null)
            {
                dataLogCache[config.DataLogName] = new CachedDataLog(dataLog);
            }

            return dataLog;
        }

        private sealed class CachedDataLog
        {
            public CachedDataLog(HmiDataLog dataLog)
            {
                DataLog = dataLog;
            }

            public HmiDataLog DataLog { get; }
        }

        public async Task<HmiTrendBulkResult?> RemoveLoggingTagsAsync(
            IReadOnlyList<HmiLoggingTagRemoveRequest> configs,
            bool confirm,
            bool dryRun)
        {
            var software = _sessionContext.HmiSoftware;
            if (software == null)
            {
                _logger?.LogWarning("No Unified HMI is currently open");
                return null;
            }

            var result = NewBulkResult("remove-hmi-logging-tags", dryRun, configs.Count);
            if (!dryRun && !confirm)
            {
                result.BlockedCount = configs.Count;
                result.Results.AddRange(configs.Select(config => new HmiTrendBulkItemResult
                {
                    TagPath = config.TagPath,
                    DataLogName = config.DataLogName,
                    LoggingTagName = config.LoggingTagName,
                    Status = "Blocked",
                    Message = "Deletion requires confirm=true."
                }));
                return result;
            }

            foreach (var config in configs)
            {
                if (string.IsNullOrWhiteSpace(config.TagPath) || string.IsNullOrWhiteSpace(config.DataLogName))
                {
                    AddFailure(result, config.TagPath, config.DataLogName, "tagPath and dataLogName are required.");
                    continue;
                }

                if (!_adapter.TryGetTag(software, config.TagPath, out var tag, out var normalizedTagPath) || tag == null)
                {
                    AddNotFound(result, config.TagPath, config.DataLogName, $"Unified HMI tag was not found: {config.TagPath}");
                    continue;
                }

                var loggingTagName = string.IsNullOrWhiteSpace(config.LoggingTagName)
                    ? _adapter.GetLoggingTagName(normalizedTagPath)
                    : config.LoggingTagName!.Trim();
                var loggingTag = _adapter.FindLoggingTag(tag, loggingTagName);
                if (loggingTag == null)
                {
                    AddNotFound(result, normalizedTagPath, config.DataLogName, $"Logging tag was not found: {loggingTagName}", loggingTagName);
                    continue;
                }

                if (!string.Equals(loggingTag.DataLog, config.DataLogName, StringComparison.OrdinalIgnoreCase))
                {
                    AddBlocked(
                        result,
                        normalizedTagPath,
                        config.DataLogName,
                        $"Logging tag '{loggingTag.Name}' references data log '{loggingTag.DataLog}', not '{config.DataLogName}'.",
                        loggingTag.Name);
                    continue;
                }

                if (dryRun)
                {
                    result.PlannedCount++;
                    result.Results.Add(new HmiTrendBulkItemResult
                    {
                        TagPath = normalizedTagPath,
                        DataLogName = config.DataLogName,
                        LoggingTagName = loggingTag.Name,
                        Status = "Planned",
                        Message = "Logging tag would be removed."
                    });
                    continue;
                }

                try
                {
                    _adapter.DeleteLoggingTag(loggingTag);
                    result.AppliedCount++;
                    result.Results.Add(new HmiTrendBulkItemResult
                    {
                        TagPath = normalizedTagPath,
                        DataLogName = config.DataLogName,
                        LoggingTagName = loggingTagName,
                        Status = "Applied"
                    });
                }
                catch (Exception ex)
                {
                    AddFailure(result, normalizedTagPath, config.DataLogName, ex.Message, loggingTagName);
                }
            }

            return await Task.FromResult(result);
        }

        public async Task<HmiTrendBulkResult?> DeleteDataLogsAsync(
            IReadOnlyList<HmiDataLogDeleteRequest> configs,
            bool confirm,
            bool dryRun)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                var result = NewBulkResult("delete-hmi-data-logs", dryRun, configs.Count);
                if (!dryRun && !confirm)
                {
                    result.BlockedCount = configs.Count;
                    result.Results.AddRange(configs.Select(config => new HmiTrendBulkItemResult
                    {
                        DataLogName = config.DataLogName,
                        Status = "Blocked",
                        Message = "Deletion requires confirm=true."
                    }));
                    return result;
                }

                var requestedNames = new HashSet<string>(
                    configs.Where(config => !string.IsNullOrWhiteSpace(config.DataLogName)).Select(config => config.DataLogName),
                    StringComparer.OrdinalIgnoreCase);
                var references = _adapter.ListLoggingTagReferences(software, requestedNames)
                    .GroupBy(reference => reference.DataLogName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var config in configs)
                {
                    if (string.IsNullOrWhiteSpace(config.DataLogName))
                    {
                        AddFailure(result, null, config.DataLogName, "dataLogName is required.");
                        continue;
                    }

                    var dataLog = _adapter.FindDataLog(software, config.DataLogName);
                    if (dataLog == null)
                    {
                        AddNotFound(result, null, config.DataLogName, $"Data log was not found: {config.DataLogName}");
                        continue;
                    }

                    if (references.TryGetValue(config.DataLogName, out var activeReferences) && activeReferences.Count > 0)
                    {
                        result.BlockedCount++;
                        result.Results.Add(new HmiTrendBulkItemResult
                        {
                            DataLogName = config.DataLogName,
                            Status = "Blocked",
                            Message = $"Data log '{config.DataLogName}' is still referenced by {activeReferences.Count} logging tag(s).",
                            References = activeReferences
                        });
                        continue;
                    }

                    if (dryRun)
                    {
                        result.PlannedCount++;
                        result.Results.Add(new HmiTrendBulkItemResult
                        {
                            DataLogName = config.DataLogName,
                            Status = "Planned",
                            Message = "Data log would be deleted."
                        });
                        continue;
                    }

                    try
                    {
                        _adapter.DeleteDataLog(dataLog);
                        result.AppliedCount++;
                        result.Results.Add(new HmiTrendBulkItemResult
                        {
                            DataLogName = config.DataLogName,
                            Status = "Applied"
                        });
                    }
                    catch (Exception ex)
                    {
                        AddFailure(result, null, config.DataLogName, ex.Message);
                    }
                }

                return result;
            });
        }

        private static HmiTrendBulkResult NewBulkResult(string operation, bool dryRun, int requestedCount)
        {
            return new HmiTrendBulkResult
            {
                Operation = operation,
                DryRun = dryRun,
                RequestedCount = requestedCount
            };
        }

        private static void AddFailure(HmiTrendBulkResult result, string? tagPath, string dataLogName, string message, string? loggingTagName = null)
        {
            result.FailedCount++;
            result.Results.Add(new HmiTrendBulkItemResult
            {
                TagPath = tagPath,
                DataLogName = dataLogName,
                LoggingTagName = loggingTagName,
                Status = "Failed",
                Message = message
            });
        }

        private static void AddNotFound(HmiTrendBulkResult result, string? tagPath, string dataLogName, string message, string? loggingTagName = null)
        {
            result.NotFoundCount++;
            result.Results.Add(new HmiTrendBulkItemResult
            {
                TagPath = tagPath,
                DataLogName = dataLogName,
                LoggingTagName = loggingTagName,
                Status = "NotFound",
                Message = message
            });
        }

        private static void AddBlocked(HmiTrendBulkResult result, string? tagPath, string dataLogName, string message, string? loggingTagName = null)
        {
            result.BlockedCount++;
            result.Results.Add(new HmiTrendBulkItemResult
            {
                TagPath = tagPath,
                DataLogName = dataLogName,
                LoggingTagName = loggingTagName,
                Status = "Blocked",
                Message = message
            });
        }
    }
}
