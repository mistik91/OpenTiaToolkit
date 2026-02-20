using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.CLI
{
    /// <summary>
    /// One-time test command for find and replace functionality.
    /// </summary>
    public class TestFindReplaceCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestFindReplaceCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public TestFindReplaceCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "test-find-replace";

        /// <inheritdoc/>
        public string Description => "Test find and replace M100->M200";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            Array.Empty<CommandParameter>()
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing test find-replace command...");

            try
            {
                // Create the rename map directly as a Dictionary
                var renameMap = new Dictionary<string, string>
                {
                    { "M100", "M200" }
                };

                logger.LogInformation("Running find-replace-db-members (dry-run)...");
                var dryRunResult = await _plcService.FindAndReplaceDbMembersAsync(
                    renameMap,
                    caseInsensitive: false,
                    dryRun: true
                );

                logger.LogInformation("Dry-run completed:");
                logger.LogInformation("  Total processed: {TotalProcessed}", dryRunResult.Summary.TotalProcessed);
                logger.LogInformation("  Matches found: {MatchesFound}", dryRunResult.Summary.MatchesFound);
                logger.LogInformation("  Skipped: {Skipped}", dryRunResult.Summary.Skipped);

                foreach (var change in dryRunResult.Changes)
                {
                    logger.LogInformation("  PREVIEW: {Path}: {Before} -> {After}", change.Path, change.Before, change.After);
                }

                logger.LogInformation("Running find-replace-db-members (applying changes)...");
                var result = await _plcService.FindAndReplaceDbMembersAsync(
                    renameMap,
                    caseInsensitive: false,
                    dryRun: false
                );

                logger.LogInformation("Changes applied:");
                logger.LogInformation("  Total processed: {TotalProcessed}", result.Summary.TotalProcessed);
                logger.LogInformation("  Matches found: {MatchesFound}", result.Summary.MatchesFound);
                logger.LogInformation("  Changes applied: {ChangesApplied}", result.Summary.ChangesApplied);
                logger.LogInformation("  Errors: {Errors}", result.Summary.Errors);

                foreach (var change in result.Changes)
                {
                    logger.LogInformation("  CHANGED: {Path}: {Before} -> {After}", change.Path, change.Before, change.After);
                }

                return CommandResult.CreateSuccess(
                    data: new
                    {
                        summary = result.Summary,
                        changes = result.Changes
                    },
                    message: $"Find-replace test completed: {result.Summary.ChangesApplied} changes applied"
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Test find-replace failed");
                return CommandResult.CreateFailure(
                    $"Test find-replace failed: {ex.Message}",
                    ex
                );
            }
        }
    }
}
