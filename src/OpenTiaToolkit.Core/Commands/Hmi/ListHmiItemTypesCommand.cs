using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to list supported Unified HMI item types.
    /// </summary>
    public class ListHmiItemTypesCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public ListHmiItemTypesCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "list-hmi-item-types";

        public string Description => "Lists the validated Unified HMI item types supported by this toolkit";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("filter", CommandParameterType.String, "Optional text filter", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Listing supported Unified HMI item types.");
            var filter = args.GetString("filter");
            var itemTypes = await _hmiService.GetSupportedItemTypesAsync(filter);

            context.CompleteCommand(Name, itemTypes.Count == 0 ? "No Unified HMI item types matched the filter" : $"Found {itemTypes.Count} supported item type(s)");
            return CommandResult.CreateSuccess(
                data: itemTypes.ToList(),
                message: itemTypes.Count == 0 ? "No Unified HMI item types matched the filter" : $"Found {itemTypes.Count} supported item type(s)");
        }
    }
}
