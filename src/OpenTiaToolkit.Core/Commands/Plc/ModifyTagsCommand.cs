using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    public sealed class ModifyTagsCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        public ModifyTagsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "modify-tags";

        public string Description => "Plans or applies explicit PLC tag create, update, delete, and replace operations";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
            new CommandParameter(
                "operations",
                CommandParameterType.ObjectArray,
                "Inline operation array with action, optional match, and set objects",
                required: false),
            new CommandParameter(
                "input-path",
                CommandParameterType.Path,
                "Optional .json or .csv operation file. Use either operations or input-path",
                required: false),
            new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate and preview operations without applying them", required: false, defaultValue: true),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm delete and replace operations when dry-run is false", required: false, defaultValue: false),
            new CommandParameter("safety-password", CommandParameterType.String, "Optional safety program password used before applying changes", required: false),
            new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for operation results", required: false)
        });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(
            CommandArgs args,
            ILogger logger,
            CommandExecutionContext context)
        {
            context.StartCommand(Name, "Validating PLC tag modification operations.");

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrWhiteSpace(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                IReadOnlyList<TagModificationOperation> operations;
                try
                {
                    operations = ParseOperations(args);
                }
                catch (Exception ex)
                {
                    return CommandResult.CreateFailure($"Invalid tag modification input: {ex.Message}");
                }

                var dryRun = args.GetBool("dry-run", true);
                var containsDestructiveOperations = operations.Any(operation =>
                    string.Equals(operation.Action, "delete", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(operation.Action, "replace", StringComparison.OrdinalIgnoreCase));
                if (!dryRun && containsDestructiveOperations && !args.GetBool("confirm"))
                {
                    return CommandResult.CreateFailure(
                        "Delete and replace operations require confirm=true when dry-run=false.");
                }

                if (!_plcService.IsSoftwareOpen ||
                    !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 20, "open-plc");
                    if (!await _plcService.OpenPlcAsync(plcName))
                    {
                        return CommandResult.CreateFailure($"Failed to open PLC software: {plcName}");
                    }
                }

                SafetyLoginResult? safetyLogin = null;
                var safetyPassword = args.GetString("safety-password");
                if (!dryRun && !string.IsNullOrEmpty(safetyPassword))
                {
                    context.Checkpoint("Logging into offline safety program.", 35, "safety-login");
                    safetyLogin = await _plcService.LoginToSafetyOfflineProgramAsync(safetyPassword);
                    if (!safetyLogin.Success && safetyLogin.PasswordRequired)
                    {
                        return CommandResult.CreateFailure(safetyLogin.Message, new { safetyLogin });
                    }
                }

                context.Checkpoint(
                    dryRun ? "Planning PLC tag modifications." : "Applying PLC tag modifications.",
                    60,
                    dryRun ? "plan-tag-modifications" : "apply-tag-modifications");
                var result = await _plcService.ModifyTagsAsync(
                    operations,
                    dryRun,
                    context.CancellationToken,
                    new BatchProgressBridge(context, 60, 80));
                var message =
                    $"Tag operations: {result.Summary.Total}, planned: {result.Summary.Planned}, " +
                    $"applied: {result.Summary.Applied}, failed: {result.Summary.Failed}";
                var data = new
                {
                    safetyLogin,
                    result.DryRun,
                    result.Summary,
                    result.Operations
                };

                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(data, message))
                {
                    context.Checkpoint("Writing PLC tag modification JSON artifact.", 85, "write-artifact");
                    var explicitOutputPath = args.Has("output-path");
                    var outputPath = CommandArtifactSupport.ResolveOutputPath(
                        explicitOutputPath
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".json"),
                        "plc_tag_modification_results.json");
                    var artifact = CommandArtifactSupport.CreateJsonArtifact(
                        data,
                        outputPath,
                        "plc-tag-modification-results",
                        result.Operations.Count,
                        explicitOutputPath,
                        new { plcName, dryRun, result.Summary });
                    var artifactMessage = $"{message}. JSON written to: {outputPath}";
                    context.CompleteCommand(Name, artifactMessage);
                    return result.Summary.Failed == 0
                        ? CommandResult.CreateSuccess(artifact, artifactMessage)
                        : CommandResult.CreateFailure(artifactMessage, artifact);
                }

                context.CompleteCommand(Name, message);
                return result.Summary.Failed == 0
                    ? CommandResult.CreateSuccess(data, message)
                    : CommandResult.CreateFailure(message, data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in modify-tags");
                return CommandResult.CreateFailure($"Error in modify-tags: {ex.Message}", ex);
            }
        }

        private static IReadOnlyList<TagModificationOperation> ParseOperations(CommandArgs args)
        {
            var hasOperations = args.Has("operations");
            var hasInputPath = args.Has("input-path");
            if (hasOperations == hasInputPath)
            {
                throw new ArgumentException("Provide exactly one of operations or input-path.");
            }

            return hasOperations
                ? TagModificationInputParser.ParseInline(args.GetObject("operations")!)
                : TagModificationInputParser.ParseFile(args.GetString("input-path"));
        }
    }
}
