using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to summarize a Unified HMI screen without returning full property payloads.
    /// </summary>
    public class GetHmiScreenSummaryCommand : ICommand
    {
        private static readonly string[] SummaryProperties =
        {
            "Name",
            "ContainedType"
        };

        private readonly IHmiService _hmiService;

        public GetHmiScreenSummaryCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "get-hmi-screen-summary";

        public string Description => "Summarizes item counts and faceplate contained types for one Unified HMI screen";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("include-items", CommandParameterType.Boolean, "Include compact per-item entries", required: false, defaultValue: false),
                new CommandParameter("working-path", CommandParameterType.Path, "Optional artifact root path", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            var screenPath = args.GetString("screen-path");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: screen-path");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var filters = new HmiJsonFilterOptions
            {
                Properties = SummaryProperties
            };
            var snapshot = await _hmiService.GetScreenJsonAsync(
                screenPath,
                filters,
                args.Has("working-path") ? args.GetString("working-path") : null);
            if (snapshot?.Screen == null)
            {
                return CommandResult.CreateFailure($"Failed to summarize Unified HMI screen: {screenPath}");
            }

            var includeItems = args.Has("include-items") && args.GetBool("include-items");
            var summary = HmiScreenSummaryResult.FromSnapshot(snapshot, includeItems);
            return CommandResult.CreateSuccess(summary, $"Unified HMI screen summarized: {screenPath}");
        }
    }

    public sealed class HmiScreenSummaryResult
    {
        public string? HmiName { get; set; }

        public string? ScreenPath { get; set; }

        public string? BaseHash { get; set; }

        public int ItemCount { get; set; }

        public List<HmiScreenSummaryCount> ItemTypes { get; set; } = new List<HmiScreenSummaryCount>();

        public List<HmiScreenSummaryCount> FaceplateContainedTypes { get; set; } = new List<HmiScreenSummaryCount>();

        public List<HmiScreenSummaryItem>? Items { get; set; }

        public static HmiScreenSummaryResult FromSnapshot(HmiScreenJsonSnapshotResult snapshot, bool includeItems)
        {
            var items = snapshot.Screen?.Items ?? new List<HmiJsonItem>();
            return new HmiScreenSummaryResult
            {
                HmiName = snapshot.HmiName,
                ScreenPath = snapshot.ScreenPath,
                BaseHash = snapshot.BaseHash,
                ItemCount = items.Count,
                ItemTypes = CountBy(items.Select(item => item.Type)),
                FaceplateContainedTypes = CountBy(items
                    .Select(item => item.Properties.TryGetValue("ContainedType", out var containedType) ? containedType : null)
                    .Where(containedType => !string.IsNullOrWhiteSpace(containedType))
                    .Select(containedType => containedType!)),
                Items = includeItems
                    ? items.Select(item => new HmiScreenSummaryItem
                    {
                        Name = item.Name,
                        Type = item.Type,
                        ContainedType = item.Properties.TryGetValue("ContainedType", out var containedType)
                            ? containedType
                            : null
                    }).ToList()
                    : null
            };
        }

        private static List<HmiScreenSummaryCount> CountBy(IEnumerable<string?> values)
        {
            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new HmiScreenSummaryCount
                {
                    Name = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public sealed class HmiScreenSummaryCount
    {
        public string? Name { get; set; }

        public int Count { get; set; }
    }

    public sealed class HmiScreenSummaryItem
    {
        public string? Name { get; set; }

        public string? Type { get; set; }

        public string? ContainedType { get; set; }
    }
}
