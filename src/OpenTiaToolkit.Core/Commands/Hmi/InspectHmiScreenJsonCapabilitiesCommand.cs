using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to inspect agent-oriented Unified HMI screen JSON capabilities.
    /// </summary>
    public class InspectHmiScreenJsonCapabilitiesCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public InspectHmiScreenJsonCapabilitiesCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "inspect-hmi-screen-json-capabilities";

        public string Description => "Inspects one Unified HMI screen and returns agent-oriented patch capabilities and guidance";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("item-names", CommandParameterType.StringArray, "Optional item names to include", required: false),
                new CommandParameter("properties", CommandParameterType.StringArray, "Optional property/composite names to include", required: false),
                new CommandParameter("include-item-types", CommandParameterType.Boolean, "Include supported item types and aliases", required: false, defaultValue: true),
                new CommandParameter("include-writable-properties", CommandParameterType.Boolean, "Include writable property metadata for exported items", required: false, defaultValue: true),
                new CommandParameter("include-snapshot", CommandParameterType.Boolean, "Include the full screen snapshot in the result", required: false, defaultValue: false),
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

            var filters = HmiJsonCommandSupport.BuildFilters(args);
            var snapshot = await _hmiService.GetScreenJsonAsync(
                screenPath,
                HasAgentFilters(filters) ? filters : null,
                args.Has("working-path") ? args.GetString("working-path") : null);
            if (snapshot?.Screen == null)
            {
                return CommandResult.CreateFailure($"Failed to inspect Unified HMI screen JSON capabilities: {screenPath}");
            }

            var includeItemTypes = !args.Has("include-item-types") || args.GetBool("include-item-types", true);
            var includeWritableProperties = !args.Has("include-writable-properties") || args.GetBool("include-writable-properties", true);
            var includeSnapshot = args.Has("include-snapshot") && args.GetBool("include-snapshot");
            var itemTypes = includeItemTypes
                ? (await _hmiService.GetSupportedItemTypesAsync()).ToList()
                : new List<HmiItemTypeInfo>();
            var items = new List<HmiScreenJsonItemCapability>();
            var warnings = new List<string>();

            foreach (var item in snapshot.Screen.Items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var writableProperties = new List<HmiItemPropertyInfo>();
                if (includeWritableProperties)
                {
                    try
                    {
                        writableProperties = (await _hmiService.GetItemPropertiesAsync(snapshot.ScreenPath, item.Name))
                            .Where(property => property.IsWritable)
                            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Item '{item.Name}' writable properties could not be inspected: {ex.Message}");
                    }
                }

                items.Add(new HmiScreenJsonItemCapability
                {
                    Name = item.Name,
                    Type = item.Type,
                    ExportedProperties = item.Properties.Keys.OrderBy(property => property, StringComparer.OrdinalIgnoreCase).ToList(),
                    WritableProperties = writableProperties,
                    AvailableCompositeProperties = HmiScreenJsonAgentSupport.CompositePropertyNames,
                    RecommendedPatchArgs = new HmiScreenJsonRecommendedPatchArgs
                    {
                        HmiName = snapshot.HmiName,
                        ScreenPath = snapshot.ScreenPath,
                        BaseHash = snapshot.BaseHash,
                        ItemNames = new[] { item.Name },
                        Properties = snapshot.SnapshotFilters?.Properties ?? Array.Empty<string>(),
                        DryRun = true,
                        Backup = true
                    }
                });
            }

            warnings.AddRange(snapshot.Warnings);
            var result = new HmiScreenJsonCapabilitiesResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                HmiName = snapshot.HmiName,
                ScreenPath = snapshot.ScreenPath,
                Snapshot = includeSnapshot ? snapshot : null,
                RecommendedPatchArgs = snapshot.RecommendedPatchArgs,
                Capabilities = HmiScreenJsonAgentSupport.CreateCapabilities(),
                SupportedItemTypes = itemTypes,
                Items = items,
                Warnings = warnings
                    .Where(warning => !string.IsNullOrWhiteSpace(warning))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            return CommandResult.CreateSuccess(result, $"Inspected Unified HMI screen JSON capabilities: {snapshot.ScreenPath}");
        }

        private static bool HasAgentFilters(HmiJsonFilterOptions filters)
        {
            return filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
