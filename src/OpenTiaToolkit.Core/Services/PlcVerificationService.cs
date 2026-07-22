using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC verification-loop operations such as compare and guarded simulator download.
    /// </summary>
    internal sealed class PlcVerificationService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger? _logger;
        private readonly V21TypeServiceResolver _types;
        private readonly V21OnlineVerificationAccess _online;

        public PlcVerificationService(EngineeringSessionContext sessionContext, ILogger? logger, V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
            var adapter = v21Adapter ?? new V21OpennessAdapter();
            _types = adapter.Types;
            _online = adapter.Online;
        }

        public async Task<PlcSoftwareCompareResult> CompareSoftwareToOnlineAsync()
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return new PlcSoftwareCompareResult(false, true, "No PLC software is currently open");
                }

                try
                {
                    _logger?.LogInformation("Comparing PLC software to online target");
                    if (!_online.TryCompareToOnline(plcSoftware, out var result, out var failureReason))
                    {
                        return new PlcSoftwareCompareResult(false, true, failureReason ?? "PLC software does not expose CompareToOnline.");
                    }

                    var entries = _online.ExtractCompareEntries(result!);
                    var differenceCount = 0;
                    foreach (var entry in entries)
                    {
                        if (entry.HasDifference)
                        {
                            differenceCount++;
                        }
                    }

                    var message = differenceCount == 0
                        ? "PLC software matches the online target."
                        : $"PLC software differs from the online target: {differenceCount} difference(s).";

                    return new PlcSoftwareCompareResult(true, true, message, entries);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to compare PLC software to online target");
                    return new PlcSoftwareCompareResult(false, true, "PLC compare failed: " + ex.Message);
                }
            });
        }

        public async Task<PlcSoftwareDownloadResult> DownloadSoftwareAsync(bool toSimulator, bool dryRun = true, bool confirm = false, bool onlyChanges = false)
        {
            return await Task.Run(() =>
            {
                if (!toSimulator)
                {
                    return new PlcSoftwareDownloadResult(
                        false,
                        dryRun,
                        false,
                        false,
                        "Real-hardware download is blocked. Pass --to-simulator=true to use the guarded PLCSIM Advanced path.",
                        onlyChanges: onlyChanges);
                }

                var plcSoftware = _sessionContext.PlcSoftware;
                var deviceItem = _sessionContext.CurrentPlcDeviceItem;
                if (plcSoftware == null || deviceItem == null)
                {
                    return new PlcSoftwareDownloadResult(false, dryRun, true, false, "No PLC software is currently open.", onlyChanges: onlyChanges);
                }

                var downloadProvider = _types.GetService(deviceItem, "DownloadProvider");
                if (downloadProvider == null)
                {
                    return new PlcSoftwareDownloadResult(false, dryRun, true, false, "DownloadProvider is not available for the selected PLC device item.", onlyChanges: onlyChanges);
                }

                var warnings = new List<string>();
                if (dryRun)
                {
                    return new PlcSoftwareDownloadResult(
                        true,
                        true,
                        true,
                        false,
                        onlyChanges
                            ? "Dry-run successful. PLC software changes download is planned for PLCSIM Advanced; no download was started."
                            : "Dry-run successful. PLC software download is planned for PLCSIM Advanced; no download was started.",
                        warnings,
                        onlyChanges: onlyChanges);
                }

                if (!confirm)
                {
                    return new PlcSoftwareDownloadResult(
                        false,
                        false,
                        true,
                        false,
                        "Download requires --confirm=true when --dry-run=false.",
                        warnings,
                        onlyChanges: onlyChanges);
                }

                if (!_online.IsDownloadConfigurationReady(downloadProvider))
                {
                    return new PlcSoftwareDownloadResult(
                        false,
                        false,
                        true,
                        false,
                        "TIA download connection configuration is not configured. Configure the PLCSIM Advanced connection in TIA Portal, then retry.",
                        warnings,
                        onlyChanges: onlyChanges);
                }

                var onlineProvider = _types.GetService(deviceItem, "OnlineProvider");
                if (onlineProvider != null && !_online.TryGoOffline(onlineProvider, out var offlineFailure))
                {
                    warnings.Add(offlineFailure ?? "Failed to go offline before download.");
                }

                _logger?.LogInformation("Downloading PLC software to PLCSIM Advanced target");
                if (!_online.TryDownloadSoftware(downloadProvider, onlyChanges, out var state, out var failureReason))
                {
                    return new PlcSoftwareDownloadResult(
                        false,
                        false,
                        true,
                        false,
                        failureReason ?? "TIA download failed.",
                        warnings,
                        state,
                        onlyChanges);
                }

                return new PlcSoftwareDownloadResult(
                    true,
                    false,
                    true,
                    true,
                    onlyChanges
                        ? "PLC software changes downloaded to PLCSIM Advanced."
                        : "PLC software downloaded to PLCSIM Advanced.",
                    warnings,
                    state,
                    onlyChanges);
            });
        }

        public async Task<PlcOnlineStatusResult> GetOnlineStatusAsync(bool goOnline = true)
        {
            return await Task.Run(() =>
            {
                var onlineProvider = GetOnlineProvider(out var failure);
                if (onlineProvider == null)
                {
                    return new PlcOnlineStatusResult(false, "Unavailable", false, failure ?? "OnlineProvider is not available.");
                }

                if (goOnline && !_online.TryGoOnline(onlineProvider, out _, out var goOnlineFailure))
                {
                    var currentState = _online.GetOnlineState(onlineProvider);
                    return new PlcOnlineStatusResult(
                        false,
                        currentState,
                        IsOnline(currentState),
                        goOnlineFailure ?? "Failed to go online.");
                }

                var state = _online.GetOnlineState(onlineProvider);
                return new PlcOnlineStatusResult(
                    true,
                    state,
                    IsOnline(state),
                    $"PLC online state: {state}.");
            });
        }

        public async Task<PlcOnlineStatusResult> GoOfflineAsync()
        {
            return await Task.Run(() =>
            {
                var onlineProvider = GetOnlineProvider(out var failure);
                if (onlineProvider == null) return new PlcOnlineStatusResult(false, "Unavailable", false, failure ?? "OnlineProvider is not available.");
                if (!_online.TryGoOffline(onlineProvider, out var offlineFailure))
                {
                    string currentState = _online.GetOnlineState(onlineProvider);
                    return new PlcOnlineStatusResult(false, currentState, IsOnline(currentState), offlineFailure ?? "Failed to go offline.");
                }
                string state = _online.GetOnlineState(onlineProvider);
                return new PlcOnlineStatusResult(true, state, IsOnline(state), $"PLC online state: {state}.");
            });
        }

        public async Task<PlcCpuControlResult> ControlCpuAsync(string action, bool confirm = false)
        {
            return await Task.Run(() =>
            {
                var normalizedAction = string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase)
                    ? "stop"
                    : "start";

                var onlineProvider = GetOnlineProvider(out var failure);
                if (onlineProvider == null)
                {
                    return new PlcCpuControlResult(false, normalizedAction, false, "Unavailable", failure ?? "OnlineProvider is not available.");
                }

                if (!_online.TryGoOnline(onlineProvider, out var onlineState, out var goOnlineFailure))
                {
                    return new PlcCpuControlResult(false, normalizedAction, false, onlineState ?? _online.GetOnlineState(onlineProvider), goOnlineFailure ?? "Failed to go online.");
                }

                if (!confirm)
                {
                    return new PlcCpuControlResult(
                        false,
                        normalizedAction,
                        false,
                        onlineState ?? _online.GetOnlineState(onlineProvider),
                        $"CPU {normalizedAction} requires --confirm=true.");
                }

                if (!_online.TryControlCpu(onlineProvider, normalizedAction, out var finalState, out var controlFailure))
                {
                    return new PlcCpuControlResult(
                        false,
                        normalizedAction,
                        false,
                        finalState ?? _online.GetOnlineState(onlineProvider),
                        controlFailure ?? $"TIA Openness does not expose a CPU {normalizedAction} operation for the selected PLC.");
                }

                return new PlcCpuControlResult(
                    true,
                    normalizedAction,
                    true,
                    finalState ?? _online.GetOnlineState(onlineProvider),
                    $"CPU {normalizedAction} command executed.");
            });
        }

        private object? GetOnlineProvider(out string? failure)
        {
            failure = null;
            if (_sessionContext.CurrentPlcDeviceItem == null || _sessionContext.PlcSoftware == null)
            {
                failure = "No PLC software is currently open.";
                return null;
            }

            var onlineProvider = _types.GetService(_sessionContext.CurrentPlcDeviceItem, "OnlineProvider");
            if (onlineProvider == null)
            {
                failure = "OnlineProvider is not available for the selected PLC device item.";
            }

            return onlineProvider;
        }

        private static bool IsOnline(string? state)
        {
            return string.Equals(state, "Online", StringComparison.OrdinalIgnoreCase);
        }
    }
}
