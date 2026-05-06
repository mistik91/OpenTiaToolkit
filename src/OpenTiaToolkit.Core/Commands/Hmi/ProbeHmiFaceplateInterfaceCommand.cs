using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to inspect a faceplate container interface and optionally probe one reversible write.
    /// </summary>
    public class ProbeHmiFaceplateInterfaceCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public ProbeHmiFaceplateInterfaceCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "probe-hmi-faceplate-interface";

        public string Description => "Inspects Unified HMI faceplate interface metadata and optionally probes one reversible value write";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("item-name", CommandParameterType.String, "The faceplate item name", required: false),
                new CommandParameter("item-name-path", CommandParameterType.Path, "Optional text file containing the exact faceplate item name", required: false),
                new CommandParameter("property", CommandParameterType.String, "Optional interface property name to target", required: false),
                new CommandParameter("write", CommandParameterType.Boolean, "Attempt a direct write to the targeted interface property", required: false, defaultValue: false),
                new CommandParameter("value", CommandParameterType.String, "The direct write value when write is true", required: false),
                new CommandParameter("restore", CommandParameterType.Boolean, "Restore the original value after a write probe", required: false, defaultValue: true)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            var screenPath = args.GetString("screen-path");
            var itemName = TryGetItemName(args, out var itemNameError);

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: screen-path");
            }

            if (itemNameError != null)
            {
                return CommandResult.CreateFailure(itemNameError);
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                return CommandResult.CreateFailure("Missing required parameter: item-name or item-name-path");
            }

            var write = args.Has("write") && args.GetBool("write");
            var propertyName = args.Has("property") ? args.GetString("property") : null;
            if (write && string.IsNullOrWhiteSpace(propertyName))
            {
                return CommandResult.CreateFailure("Parameter property is required when write is true.");
            }

            if (write && !args.Has("value"))
            {
                return CommandResult.CreateFailure("Parameter value is required when write is true.");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var result = await _hmiService.ProbeFaceplateInterfaceAsync(
                screenPath,
                itemName!,
                propertyName,
                write,
                args.Has("value") ? args.GetString("value") : null,
                !args.Has("restore") || args.GetBool("restore", true));

            if (result == null)
            {
                return CommandResult.CreateFailure($"Failed to probe Unified HMI faceplate interface: {screenPath}/{itemName}");
            }

            if (write && !result.WriteSucceeded)
            {
                return CommandResult.CreateFailure("Unified HMI faceplate interface write probe failed.", result);
            }

            return CommandResult.CreateSuccess(result, $"Unified HMI faceplate interface probed: {screenPath}/{itemName}");
        }

        private static string? TryGetItemName(CommandArgs args, out string? error)
        {
            error = null;

            if (args.Has("item-name-path"))
            {
                var itemNamePath = args.GetString("item-name-path");
                if (string.IsNullOrWhiteSpace(itemNamePath))
                {
                    error = "Parameter item-name-path must contain a text file path.";
                    return null;
                }

                try
                {
                    return File.ReadAllText(itemNamePath).Trim();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                {
                    error = $"Failed to read item-name-path '{itemNamePath}': {ex.Message}";
                    return null;
                }
            }

            return args.Has("item-name") ? args.GetString("item-name") : null;
        }
    }
}
