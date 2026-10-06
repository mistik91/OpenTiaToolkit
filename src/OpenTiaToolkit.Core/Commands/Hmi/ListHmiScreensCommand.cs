using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to list Unified HMI screens.
    /// </summary>
    public class ListHmiScreensCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public ListHmiScreensCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "list-hmi-screens";

        public string Description => "Lists screens in a Unified HMI";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("group-path", CommandParameterType.String, "Optional screen group path", required: false),
                new CommandParameter("recursive", CommandParameterType.Boolean, "Traverse nested screen groups", required: false, defaultValue: true)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Listing Unified HMI screens.");
            var hmiName = args.GetString("hmi-name");
            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
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

            var groupPath = args.GetString("group-path");
            var recursive = !args.Has("recursive") || args.GetBool("recursive", true);
            context.Checkpoint("Reading Unified HMI screens.", 60, "list-screens");
            var screens = await _hmiService.GetScreensAsync(groupPath, recursive);

            context.CompleteCommand(Name, screens.Count == 0 ? "No screens found in the Unified HMI" : $"Found {screens.Count} screen(s)");
            return CommandResult.CreateSuccess(
                data: screens.ToList(),
                message: screens.Count == 0 ? "No screens found in the Unified HMI" : $"Found {screens.Count} screen(s)");
        }
    }
}
