using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to open a Unified HMI by name.
    /// </summary>
    public class OpenUnifiedHmiCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public OpenUnifiedHmiCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "open-unified-hmi";

        public string Description => "Opens a Unified HMI by device or device item name";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "hmi-name",
                    CommandParameterType.String,
                    "The Unified HMI device or device item name",
                    required: true)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            var success = await _hmiService.OpenUnifiedHmiAsync(hmiName);
            return success
                ? CommandResult.CreateSuccess(new { opened = true, hmiName }, $"Unified HMI opened: {hmiName}")
                : CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
        }
    }
}
