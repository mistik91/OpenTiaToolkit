using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to get Unified HMI item properties.
    /// </summary>
    public class GetHmiItemPropertiesCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public GetHmiItemPropertiesCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "get-hmi-item-properties";

        public string Description => "Gets readable properties for a Unified HMI item";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("item-name", CommandParameterType.String, "The screen item name", required: true),
                new CommandParameter("properties", CommandParameterType.StringArray, "Optional property names", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Reading Unified HMI item properties.");
            var hmiName = args.GetString("hmi-name");
            var screenPath = args.GetString("screen-path");
            var itemName = args.GetString("item-name");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: screen-path");
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                return CommandResult.CreateFailure("Missing required parameter: item-name");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                context.Checkpoint($"Opening Unified HMI: {hmiName}.", 25, "open-hmi");
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var requestedProperties = args.GetStringArray("properties");
            if (requestedProperties.Length == 1 && requestedProperties[0].Contains(","))
            {
                requestedProperties = requestedProperties[0]
                    .Split(',')
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            context.Checkpoint($"Reading properties for Unified HMI item: {itemName}.", 65, "read-properties");
            var properties = await _hmiService.GetItemPropertiesAsync(
                screenPath,
                itemName,
                requestedProperties.Length == 0 ? null : requestedProperties);

            context.CompleteCommand(Name, properties.Count == 0 ? "No properties found for the Unified HMI item" : $"Found {properties.Count} property value(s)");
            return CommandResult.CreateSuccess(
                data: properties.ToList(),
                message: properties.Count == 0 ? "No properties found for the Unified HMI item" : $"Found {properties.Count} property value(s)");
        }
    }
}
