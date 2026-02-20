using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to find and replace UDT member names with nested struct support.
    /// </summary>
    public class FindAndReplaceUdtMembersCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="FindAndReplaceUdtMembersCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public FindAndReplaceUdtMembersCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "find-replace-udt-members";

        /// <inheritdoc/>
        public string Description => "Finds and replaces UDT member names with nested struct support";

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
                    "target-types",
                    CommandParameterType.StringArray,
                    "Optional list of specific UDT names (null = all)",
                    required: false)
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
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
                var targetTypes = args.GetStringArray("target-types");

                logger.LogInformation("Find-replace-udt-members: PLC={PlcName}, Mappings={Count}, CaseInsensitive={CaseInsensitive}, DryRun={DryRun}, Targets={TargetCount}",
                    plcName, renameMap.Count, caseInsensitive, dryRun, targetTypes.Length);

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.FindAndReplaceUdtMembersAsync(renameMap, caseInsensitive, dryRun, targetTypes);

                // Build summary message
                var summary = result.Summary;
                var message = $"UDT members processed: {summary.TotalProcessed}, " +
                            $"Matches: {summary.MatchesFound}, " +
                            $"Changes: {(dryRun ? "previewed" : "applied")}: {summary.ChangesApplied}, " +
                            $"Skipped: {summary.Skipped}, " +
                            $"Warnings: {summary.Warnings}, " +
                            $"Errors: {summary.Errors}";

                // Return result with data
                return CommandResult.CreateSuccess(
                    data: new
                    {
                        summary,
                        changes = result.Changes,
                        skipped = result.Skipped,
                        warnings = result.Warnings,
                        errors = result.Errors
                    },
                    message: message
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in find-replace-udt-members");
                return CommandResult.CreateFailure(
                    $"Error in find-replace-udt-members: {ex.Message}",
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
