using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to update PLC tags from a CSV file based on page-based naming rules.
    /// Tags with page &gt;= 100 are matched by base name (can change both name and address).
    /// Tags with page &lt; 100 are matched by full name (address only).
    /// </summary>
    public class UpdateTagsFromCsvCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateTagsFromCsvCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public UpdateTagsFromCsvCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "update-tags-from-csv";

        /// <inheritdoc/>
        public string Description => "Updates PLC tags from a CSV file based on page-based naming rules";

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
                    "csv-path",
                    CommandParameterType.String,
                    "Path to the CSV file containing tag updates",
                    required: true),
                new CommandParameter(
                    "dry-run",
                    CommandParameterType.Boolean,
                    "Preview changes without applying (default: true)",
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

                var csvPath = args.GetString("csv-path");
                if (string.IsNullOrEmpty(csvPath))
                {
                    return CommandResult.CreateFailure("Missing required parameter: csv-path");
                }

                var dryRun = args.GetBool("dry-run", true);

                logger.LogInformation("UpdateTagsFromCsv: PLC={PlcName}, CsvPath={CsvPath}, DryRun={DryRun}",
                    plcName, csvPath, dryRun);

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.UpdateTagsFromCsvAsync(csvPath, dryRun);

                // Build summary message
                var summary = result.Summary;
                var message = $"CSV rows processed: {summary.TotalCsvRows}, " +
                              $"Existing tags scanned: {summary.ExistingTagsScanned}, " +
                              $"Tags updated: {(dryRun ? "previewed" : "applied")}: {summary.TagsUpdated}, " +
                              $"Tags created: {(dryRun ? "previewed" : "applied")}: {summary.TagsCreated}, " +
                              $"Skipped: {summary.TagsSkipped}, " +
                              $"Warnings: {result.Warnings.Count}, " +
                              $"Errors: {result.Errors.Count}";

                // Return result with data
                return CommandResult.CreateSuccess(
                    data: new
                    {
                        summary,
                        updated = result.Updated,
                        created = result.Created,
                        warnings = result.Warnings,
                        errors = result.Errors
                    },
                    message: message
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in update-tags-from-csv");
                return CommandResult.CreateFailure(
                    $"Error in update-tags-from-csv: {ex.Message}",
                    ex
                );
            }
        }
    }
}
