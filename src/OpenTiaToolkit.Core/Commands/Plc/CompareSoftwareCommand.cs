using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to compare PLC software against the online target.
    /// </summary>
    public class CompareSoftwareCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        public CompareSoftwareCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "compare-software";

        public string Description => "Compares PLC software to the online target";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("target", CommandParameterType.String, "Compare target. Currently supported: online", required: false, defaultValue: "online"),
                new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for compare results", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Validating PLC software compare inputs.");

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var target = args.GetString("target", "online");
                if (!string.Equals(target, "online", System.StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResult.CreateFailure("Unsupported compare target. Currently supported: online.");
                }

                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Comparing PLC software to online target.", 55, "compare-software");
                var result = await _plcService.CompareSoftwareToOnlineAsync();
                if (!result.Success)
                {
                    return CommandResult.CreateFailure(result.Message, result);
                }

                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(result, result.Message))
                {
                    context.Checkpoint("Writing PLC software compare artifact.", 85, "write-artifact");
                    var outputPath = PlcCommandSupport.ResolveOutputPath(
                        args.Has("output-path")
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".json"),
                        "plc_software_compare.json");
                    var artifact = PlcCommandSupport.CreateJsonArtifact(
                        result,
                        outputPath,
                        "plc-software-compare",
                        result.Entries.Count,
                        args.Has("output-path"),
                        new
                        {
                            plcName,
                            target = "online",
                            result.DifferenceCount
                        });

                    context.CompleteCommand(Name, $"{result.Message} JSON written to: {outputPath}");
                    return CommandResult.CreateSuccess(artifact, $"{result.Message} JSON written to: {outputPath}");
                }

                context.CompleteCommand(Name, result.Message);
                return CommandResult.CreateSuccess(result, result.Message);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error comparing PLC software");
                return CommandResult.CreateFailure($"Error comparing PLC software: {ex.Message}", ex);
            }
        }
    }
}
