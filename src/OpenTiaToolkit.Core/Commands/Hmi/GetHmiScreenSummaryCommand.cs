using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

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

        private static readonly string[] FaceplateSummaryProperties =
        {
            "Name",
            "ContainedType",
            "Interface.GEN",
            "Interface.NAME",
            "Interface.FORMAT"
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
                new CommandParameter("include-faceplates", CommandParameterType.Boolean, "Include compact faceplate interface entries and configuration counts", required: false, defaultValue: false),
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

            var includeFaceplates = args.Has("include-faceplates") && args.GetBool("include-faceplates");
            var filters = new HmiJsonFilterOptions
            {
                Properties = includeFaceplates ? FaceplateSummaryProperties : SummaryProperties
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
            var summary = HmiScreenSummaryResult.FromSnapshot(snapshot, includeItems, includeFaceplates);
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

        public int? FaceplateCount { get; set; }

        public int? ConfiguredFaceplateCount { get; set; }

        public int? PlaceholderFaceplateCount { get; set; }

        public List<HmiScreenSummaryFaceplate>? Faceplates { get; set; }

        public static HmiScreenSummaryResult FromSnapshot(
            HmiScreenJsonSnapshotResult snapshot,
            bool includeItems,
            bool includeFaceplates = false)
        {
            var items = snapshot.Screen?.Items ?? new List<HmiJsonItem>();
            var faceplates = items
                .Where(item => string.Equals(item.Type, "HmiFaceplateContainer", StringComparison.OrdinalIgnoreCase))
                .Select(CreateFaceplateSummary)
                .ToList();
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
                    : null,
                FaceplateCount = includeFaceplates ? faceplates.Count : null,
                ConfiguredFaceplateCount = includeFaceplates
                    ? faceplates.Count(faceplate => !string.IsNullOrWhiteSpace(faceplate.Gen))
                    : null,
                PlaceholderFaceplateCount = includeFaceplates
                    ? faceplates.Count(faceplate => string.IsNullOrWhiteSpace(faceplate.Gen))
                    : null,
                Faceplates = includeFaceplates ? faceplates : null
            };
        }

        private static HmiScreenSummaryFaceplate CreateFaceplateSummary(HmiJsonItem item)
        {
            var bindings = (item.Interface ?? new List<HmiJsonInterfaceBinding>())
                .Where(binding => !string.IsNullOrWhiteSpace(binding.PropertyName))
                .GroupBy(binding => binding.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

            return new HmiScreenSummaryFaceplate
            {
                Name = item.Name,
                ContainedType = item.Properties.TryGetValue("ContainedType", out var containedType)
                    ? containedType
                    : null,
                Gen = GetBinding(bindings, "GEN"),
                DisplayName = NormalizeText(GetBinding(bindings, "NAME")),
                Format = GetBinding(bindings, "FORMAT")
            };
        }

        private static string? GetBinding(IReadOnlyDictionary<string, string?> bindings, string propertyName)
        {
            return bindings.TryGetValue(propertyName, out var value) ? value : null;
        }

        private static string? NormalizeText(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var trimmed = rawValue!.Trim();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                try
                {
                    var document = XDocument.Parse(trimmed);
                    var text = string.Concat(document.DescendantNodes().OfType<XText>().Select(node => node.Value)).Trim();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
                catch
                {
                    return trimmed;
                }
            }

            return trimmed;
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

    public sealed class HmiScreenSummaryFaceplate
    {
        public string? Name { get; set; }

        public string? ContainedType { get; set; }

        public string? Gen { get; set; }

        public string? DisplayName { get; set; }

        public string? Format { get; set; }
    }
}
