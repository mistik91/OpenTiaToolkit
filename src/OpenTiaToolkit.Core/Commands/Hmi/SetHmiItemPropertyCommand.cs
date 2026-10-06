using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to set a Unified HMI item property.
    /// </summary>
    public class SetHmiItemPropertyCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public SetHmiItemPropertyCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "set-hmi-item-property";

        public string Description => "Sets a property or attribute on a Unified HMI item";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("item-name", CommandParameterType.String, "The screen item name", required: true),
                new CommandParameter("property", CommandParameterType.String, "The property or attribute name", required: true),
                new CommandParameter("value", CommandParameterType.String, "The new property value", required: true)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Updating Unified HMI item property.");
            var hmiName = args.GetString("hmi-name");
            var screenPath = args.GetString("screen-path");
            var itemName = args.GetString("item-name");
            var propertyName = args.GetString("property");
            var value = args.GetString("value");

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

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return CommandResult.CreateFailure("Missing required parameter: property");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return CommandResult.CreateFailure("Missing required parameter: value");
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

            context.Checkpoint($"Updating Unified HMI item property: {itemName}.{propertyName}.", 65, "set-property");
            var success = await _hmiService.SetItemPropertyAsync(screenPath, itemName, propertyName, value);
            context.CompleteCommand(Name, success ? $"Updated Unified HMI item property: {itemName}.{propertyName}" : $"Failed to update Unified HMI item property: {itemName}.{propertyName}");
            return success
                ? CommandResult.CreateSuccess(new { updated = true, itemName, propertyName }, $"Updated Unified HMI item property: {itemName}.{propertyName}")
                : CommandResult.CreateFailure($"Failed to update Unified HMI item property: {itemName}.{propertyName}");
        }
    }
}
