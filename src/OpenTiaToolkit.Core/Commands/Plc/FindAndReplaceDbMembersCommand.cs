using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to find and replace GlobalDB member names with nested struct support.
    /// </summary>
    public class FindAndReplaceDbMembersCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="FindAndReplaceDbMembersCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public FindAndReplaceDbMembersCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "find-replace-db-members";

        /// <inheritdoc/>
        public string Description => "Finds and replaces GlobalDB member names with nested struct support";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "plc-name",
                    CommandParameterType.String,
                    "The name of the PLC device",
                    required: true),
                new CommandParameter(
                    "rename-map",
                    CommandParameterType.Object,
                    "Dictionary mapping old member names to new names (JSON string or Dictionary)",
                    required: true),
                new CommandParameter(
                    "case-insensitive",
                    CommandParameterType.Boolean,
                    "Enable case-insensitive matching (default: false)",
                    required: false),
                new CommandParameter(
                    "dry-run",
                    CommandParameterType.Boolean,
                    "Preview changes without applying (default: true)",
                    required: false),
                new CommandParameter(
                    "target-blocks",
                    CommandParameterType.StringArray,
                    "Optional list of specific GlobalDB names (null = all)",
                    required: false),
                new CommandParameter(
                    "output-path",
                    CommandParameterType.Path,
                    "Optional CSV file or folder path for find/replace result rows",
                    required: false)
            }
        );

        /// <inheritdoc/>
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                context.StartCommand(Name, "Finding and replacing DB members.");
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var renameMapObj = args.GetObject("rename-map");
                if (renameMapObj == null)
                {
                    return CommandResult.CreateFailure("Missing required parameter: rename-map");
                }

                // Parse rename map
                Dictionary<string, string> renameMap;
                try
                {
                    renameMap = ParseRenameMap(renameMapObj);
                }
                catch (Exception ex)
                {
                    return CommandResult.CreateFailure($"Invalid rename-map format: {ex.Message}");
                }

                if (renameMap.Count == 0)
                {
                    return CommandResult.CreateFailure("Rename map must contain at least one mapping");
                }

                var caseInsensitive = args.GetBool("case-insensitive", false);
                var dryRun = args.GetBool("dry-run", true);
                var targetBlocks = args.GetStringArray("target-blocks");

                logger.LogInformation("Find-replace-db-members: PLC={PlcName}, Mappings={Count}, CaseInsensitive={CaseInsensitive}, DryRun={DryRun}, Targets={TargetCount}",
                    plcName, renameMap.Count, caseInsensitive, dryRun, targetBlocks.Length);

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 20, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint(dryRun ? "Previewing DB member replacements." : "Applying DB member replacements.", 55, "find-replace");
                var result = await _plcService.FindAndReplaceDbMembersAsync(renameMap, caseInsensitive, dryRun, targetBlocks);

                // Build summary message
                var summary = result.Summary;
                var message = $"DB members processed: {summary.TotalProcessed}, " +
                            $"Matches: {summary.MatchesFound}, " +
                            $"Changes: {(dryRun ? "previewed" : "applied")}: {summary.ChangesApplied}, " +
                            $"Skipped: {summary.Skipped}, " +
                            $"Warnings: {summary.Warnings}, " +
                            $"Errors: {summary.Errors}";

                var data = FindReplaceCommandResult.From(result);

                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(data, message))
                {
                    context.Checkpoint("Writing DB member find/replace CSV artifact.", 85, "write-artifact");
                    var explicitOutputPath = args.Has("output-path");
                    var outputPath = PlcCommandSupport.ResolveOutputPath(
                        explicitOutputPath
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                        "plc_db_member_find_replace_results.csv");
                    var bytesWritten = PlcCommandSupport.WriteFindReplaceCsv(result, outputPath);
                    var itemCount = result.Changes.Count + result.Skipped.Count + result.Warnings.Count + result.Errors.Count;
                    var artifact = CommandArtifactSupport.CreateCsvArtifact(
                        outputPath,
                        "plc-db-member-find-replace-results",
                        bytesWritten,
                        itemCount,
                        explicitOutputPath,
                        PlcCommandSupport.FindReplaceCsvColumns,
                        new
                        {
                            plcName,
                            dryRun,
                            caseInsensitive,
                            targetBlocks,
                            mappings = renameMap.Count,
                            summary
                        });

                    context.CompleteCommand(Name, $"{message}. CSV written to: {outputPath}");
                    return CommandResult.CreateSuccess(
                        data: artifact,
                        message: $"{message}. CSV written to: {outputPath}"
                    );
                }

                // Return result with data
                context.CompleteCommand(Name, message);
                return CommandResult.CreateSuccess(
                    data: data,
                    message: message
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in find-replace-db-members");
                return CommandResult.CreateFailure(
                    $"Error in find-replace-db-members: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Parses rename map from various input formats.
        /// </summary>
        private Dictionary<string, string> ParseRenameMap(object renameMapObj)
        {
            return RenameMapParser.Parse(renameMapObj);
        }
    }
}
