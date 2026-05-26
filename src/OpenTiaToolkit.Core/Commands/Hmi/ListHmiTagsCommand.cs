using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to list Unified HMI tags.
    /// </summary>
    public class ListHmiTagsCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public ListHmiTagsCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "list-hmi-tags";

        public string Description => "Lists tags in a Unified HMI";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("filter", CommandParameterType.String, "Optional text filter", required: false),
                new CommandParameter("root-path", CommandParameterType.String, "Optional tag path to enumerate only that subtree", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var tags = await _hmiService.GetTagsAsync(
                args.Has("filter") ? args.GetString("filter") : null,
                args.Has("root-path") ? args.GetString("root-path") : null);
            return CommandResult.CreateSuccess(tags, $"Found {tags.Count} Unified HMI tag(s)");
        }
    }
}
