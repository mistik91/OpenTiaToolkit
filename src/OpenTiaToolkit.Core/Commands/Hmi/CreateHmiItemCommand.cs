using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to create a Unified HMI screen item.
    /// </summary>
    public class CreateHmiItemCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public CreateHmiItemCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "create-hmi-item";

        public string Description => "Creates a Unified HMI item on a screen";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("item-type", CommandParameterType.String, "The item type or alias", required: true),
                new CommandParameter("item-name", CommandParameterType.String, "The new item name", required: true),
                new CommandParameter("left", CommandParameterType.Integer, "Optional left position", required: false),
                new CommandParameter("top", CommandParameterType.Integer, "Optional top position", required: false),
                new CommandParameter("width", CommandParameterType.Integer, "Optional item width", required: false),
                new CommandParameter("height", CommandParameterType.Integer, "Optional item height", required: false),
                new CommandParameter("contained-type", CommandParameterType.String, "Optional faceplate type assigned before interface initialization", required: false),
                new CommandParameter("faceplate-type", CommandParameterType.String, "Compatibility alias for contained-type", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Creating Unified HMI item.");
            var hmiName = args.GetString("hmi-name");
            var screenPath = args.GetString("screen-path");
            var itemType = args.GetString("item-type");
            var itemName = args.GetString("item-name");
            var containedType = args.GetString("contained-type");
            var faceplateType = args.GetString("faceplate-type");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: screen-path");
            }

            if (string.IsNullOrWhiteSpace(itemType))
            {
                return CommandResult.CreateFailure("Missing required parameter: item-type");
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                return CommandResult.CreateFailure("Missing required parameter: item-name");
            }

            if (!string.IsNullOrWhiteSpace(containedType) &&
                !string.IsNullOrWhiteSpace(faceplateType) &&
                !string.Equals(containedType, faceplateType, StringComparison.Ordinal))
            {
                return CommandResult.CreateFailure(
                    "Parameters contained-type and faceplate-type must match when both are provided.");
            }

            containedType = !string.IsNullOrWhiteSpace(containedType) ? containedType : faceplateType;

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                context.Checkpoint($"Opening Unified HMI: {hmiName}.", 25, "open-hmi");
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            context.Checkpoint($"Ensuring Unified HMI item: {itemName}.", 65, "create-item");
            var itemInfo = await _hmiService.CreateScreenItemAsync(
                screenPath,
                itemType,
                itemName,
                args.Has("left") ? (int?)args.GetInt("left") : null,
                args.Has("top") ? (int?)args.GetInt("top") : null,
                args.Has("width") ? (int?)args.GetInt("width") : null,
                args.Has("height") ? (int?)args.GetInt("height") : null,
                containedType);

            context.CompleteCommand(Name, itemInfo != null ? $"Unified HMI item ensured: {itemName}" : $"Failed to create Unified HMI item: {itemName}");
            return itemInfo != null
                ? CommandResult.CreateSuccess(itemInfo, $"Unified HMI item ensured: {itemName}")
                : CommandResult.CreateFailure($"Failed to create Unified HMI item: {itemName}");
        }
    }
}
