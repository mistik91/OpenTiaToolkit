using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to inspect Unified HMI connection/session health.
    /// </summary>
    public class HmiStatusCommand : ICommand
    {
        private readonly IPortalService _portalService;
        private readonly IHmiService _hmiService;

        public HmiStatusCommand(IPortalService portalService, IHmiService hmiService)
        {
            _portalService = portalService;
            _hmiService = hmiService;
        }

        public string Name => "hmi-status";

        public string Description => "Gets Unified HMI session status and optionally probes a screen";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "Optional Unified HMI name expected by the caller", required: false),
                new CommandParameter("screen-path", CommandParameterType.String, "Optional screen path to probe", required: false),
                new CommandParameter("open-if-needed", CommandParameterType.Boolean, "Open hmi-name before probing when no matching HMI is selected", required: false, defaultValue: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var status = new HmiStatusResult
            {
                PortalConnected = _portalService.IsConnected,
                PortalVersion = _portalService.Version,
                HmiOpen = _hmiService.IsHmiOpen,
                CurrentHmiName = _hmiService.CurrentHmiName,
                ExpectedHmiName = args.Has("hmi-name") ? args.GetString("hmi-name") : null,
                ProbeScreenPath = args.Has("screen-path") ? args.GetString("screen-path") : null
            };

            try
            {
                status.RunningInstances = (await _portalService.GetInstancesAsync())
                    .Select(instance => new HmiStatusInstance
                    {
                        ProcessId = instance.ProcessId,
                        Version = instance.Version,
                        IsAttached = instance.IsAttached
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                status.Errors.Add($"Could not list TIA Portal instances: {FormatExceptionMessage(ex)}");
            }

            try
            {
                var projectInfo = await _portalService.GetProjectInfoAsync();
                if (projectInfo != null)
                {
                    status.ProjectName = projectInfo.Name;
                    status.ProjectPath = projectInfo.Path;
                }
            }
            catch (Exception ex)
            {
                status.Errors.Add($"Could not read project info: {FormatExceptionMessage(ex)}");
            }

            var openIfNeeded = args.Has("open-if-needed") && args.GetBool("open-if-needed");
            if (!string.IsNullOrWhiteSpace(status.ExpectedHmiName) &&
                (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, status.ExpectedHmiName, StringComparison.OrdinalIgnoreCase)) &&
                openIfNeeded)
            {
                var expectedHmiName = status.ExpectedHmiName!;
                try
                {
                    status.OpenAttempted = true;
                    status.OpenSucceeded = await _hmiService.OpenUnifiedHmiAsync(expectedHmiName);
                    status.HmiOpen = _hmiService.IsHmiOpen;
                    status.CurrentHmiName = _hmiService.CurrentHmiName;
                    if (!status.OpenSucceeded)
                    {
                        status.Errors.Add($"Failed to open Unified HMI: {expectedHmiName}");
                    }
                }
                catch (Exception ex)
                {
                    status.OpenAttempted = true;
                    status.OpenSucceeded = false;
                    status.Errors.Add($"Opening Unified HMI failed: {FormatExceptionMessage(ex)}");
                }
            }

            if (!string.IsNullOrWhiteSpace(status.ProbeScreenPath))
            {
                var probeScreenPath = status.ProbeScreenPath!;
                try
                {
                    var screen = await _hmiService.GetScreenInfoAsync(probeScreenPath);
                    status.ScreenProbeSucceeded = screen != null;
                    status.ScreenProbeMessage = screen == null
                        ? $"Screen not found or HMI not open: {probeScreenPath}"
                        : $"Screen resolved: {screen.Path}";
                }
                catch (Exception ex)
                {
                    status.ScreenProbeSucceeded = false;
                    status.ScreenProbeMessage = FormatExceptionMessage(ex);
                    status.Errors.Add($"Screen probe failed: {status.ScreenProbeMessage}");
                }
            }

            status.Healthy =
                status.PortalConnected &&
                status.Errors.Count == 0 &&
                (string.IsNullOrWhiteSpace(status.ExpectedHmiName) ||
                 string.Equals(status.CurrentHmiName, status.ExpectedHmiName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(status.ProbeScreenPath) || status.ScreenProbeSucceeded == true);

            return CommandResult.CreateSuccess(
                status,
                status.Healthy ? "Unified HMI session is healthy." : "Unified HMI session has diagnostics to inspect.");
        }

        private static string FormatExceptionMessage(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) &&
                    !messages.Contains(current.Message, StringComparer.Ordinal))
                {
                    messages.Add(current.Message);
                }
            }

            return messages.Count == 0 ? ex.GetType().Name : string.Join(" | Inner: ", messages);
        }
    }

    public sealed class HmiStatusResult
    {
        public bool Healthy { get; set; }
        public bool PortalConnected { get; set; }
        public string? PortalVersion { get; set; }
        public string? ProjectName { get; set; }
        public string? ProjectPath { get; set; }
        public bool HmiOpen { get; set; }
        public string? CurrentHmiName { get; set; }
        public string? ExpectedHmiName { get; set; }
        public bool OpenAttempted { get; set; }
        public bool OpenSucceeded { get; set; }
        public string? ProbeScreenPath { get; set; }
        public bool? ScreenProbeSucceeded { get; set; }
        public string? ScreenProbeMessage { get; set; }
        public List<HmiStatusInstance> RunningInstances { get; set; } = new List<HmiStatusInstance>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public sealed class HmiStatusInstance
    {
        public int ProcessId { get; set; }
        public string? Version { get; set; }
        public bool IsAttached { get; set; }
    }
}
