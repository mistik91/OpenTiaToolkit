using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to copy a Unified HMI screen from a template screen.
    /// </summary>
    public class CopyHmiScreenFromTemplateCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public CopyHmiScreenFromTemplateCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "copy-hmi-screen-from-template";

        public string Description => "Copies a Unified HMI screen from a template screen with optional find/replace";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("template-screen-path", CommandParameterType.String, "The template screen path", required: true),
                new CommandParameter("target-screen-path", CommandParameterType.String, "The new target screen path", required: true),
                new CommandParameter("find-text", CommandParameterType.String, "Optional text to replace while cloning", required: false),
                new CommandParameter("replace-text", CommandParameterType.String, "Optional replacement text", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Copying Unified HMI screen from template.");
            var hmiName = args.GetString("hmi-name");
            var templateScreenPath = args.GetString("template-screen-path");
            var targetScreenPath = args.GetString("target-screen-path");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(templateScreenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: template-screen-path");
            }

            if (string.IsNullOrWhiteSpace(targetScreenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: target-screen-path");
            }

            var findText = args.Has("find-text") ? args.GetString("find-text") : null;
            var replaceText = args.Has("replace-text") ? args.GetString("replace-text") : null;

            if (args.Has("replace-text") && !args.Has("find-text"))
            {
                return CommandResult.CreateFailure("Parameter replace-text requires find-text.");
            }

            if (args.Has("find-text") && !args.Has("replace-text"))
            {
                replaceText = string.Empty;
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

            context.Checkpoint($"Copying Unified HMI screen: {templateScreenPath} -> {targetScreenPath}.", 65, "copy-screen");
            var copyResult = await _hmiService.CopyScreenFromTemplateAsync(templateScreenPath, targetScreenPath, findText, replaceText);
            context.CompleteCommand(Name, copyResult != null ? $"Unified HMI screen copied: {copyResult.TargetPath}" : $"Failed to copy Unified HMI screen from template: {templateScreenPath}");
            return copyResult != null
                ? CommandResult.CreateSuccess(copyResult, $"Unified HMI screen copied: {copyResult.TargetPath}")
                : CommandResult.CreateFailure($"Failed to copy Unified HMI screen from template: {templateScreenPath}");
        }
    }
}
