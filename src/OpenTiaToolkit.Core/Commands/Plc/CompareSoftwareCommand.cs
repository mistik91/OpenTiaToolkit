using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to compare PLC software against the online target.
    /// </summary>
    public class CompareSoftwareCommand : ICommand
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

                var target = args.GetString("target", "online");
                if (!string.Equals(target, "online", System.StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResult.CreateFailure("Unsupported compare target. Currently supported: online.");
                }

                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.CompareSoftwareToOnlineAsync();
                if (!result.Success)
                {
                    return CommandResult.CreateFailure(result.Message, result);
                }

                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(result, result.Message))
                {
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

                    return CommandResult.CreateSuccess(artifact, $"{result.Message} JSON written to: {outputPath}");
                }

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
