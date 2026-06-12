using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to generate a minimal Unified HMI JSON patch from two exports.
    /// </summary>
    public class DiffHmiJsonCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public DiffHmiJsonCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "diff-hmi-json";

        public string Description => "Creates a minimal Unified HMI JSON patch from two exports";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("base-path", CommandParameterType.Path, "Base export folder or .json file path", required: true),
                new CommandParameter("modified-path", CommandParameterType.Path, "Edited export folder or .json file path", required: true),
                new CommandParameter("patch-path", CommandParameterType.Path, "Target patch folder or .json file path", required: true),
                new CommandParameter("screens", CommandParameterType.String, "Optional comma-separated screen paths", required: false),
                new CommandParameter("item-names", CommandParameterType.String, "Optional comma-separated item names", required: false),
                new CommandParameter("properties", CommandParameterType.String, "Optional comma-separated property paths", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var basePath = args.GetString("base-path");
            var modifiedPath = args.GetString("modified-path");
            var patchPath = args.GetString("patch-path");

            if (string.IsNullOrWhiteSpace(basePath))
            {
                return CommandResult.CreateFailure("Missing required parameter: base-path");
            }

            if (string.IsNullOrWhiteSpace(modifiedPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: modified-path");
            }

            if (string.IsNullOrWhiteSpace(patchPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: patch-path");
            }

            var filters = HmiJsonCommandSupport.BuildFilters(args);
            var diffResult = await _hmiService.DiffJsonAsync(
                basePath,
                modifiedPath,
                patchPath,
                HasAnyFilter(filters) ? filters : null);

            return diffResult != null
                ? CommandResult.CreateSuccess(diffResult, $"Unified HMI JSON patch written to: {diffResult.OutputPath}")
                : CommandResult.CreateFailure("Failed to create Unified HMI JSON patch.");
        }

        private static bool HasAnyFilter(HmiJsonFilterOptions filters)
        {
            return filters.Screens.Any() || filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
