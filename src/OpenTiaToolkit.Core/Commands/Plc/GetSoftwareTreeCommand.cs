using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to get the hierarchical tree structure of PLC software.
    /// </summary>
    public class GetSoftwareTreeCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetSoftwareTreeCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public GetSoftwareTreeCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "get-software-tree";

        /// <inheritdoc/>
        public string Description => "Gets the hierarchical PLC software structure";

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
                    "depth",
                    CommandParameterType.Integer,
                    "Maximum depth to traverse (null for unlimited)",
                    required: false,
                    defaultValue: null),
                new CommandParameter(
                    "output-path",
                    CommandParameterType.Path,
                    "Optional JSON file or folder path for the PLC software tree",
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
                context.StartCommand(Name, "Reading PLC software tree.");
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var depth = args.GetInt("depth", 0);
                var maxDepth = args.Has("depth") ? (int?)depth : null;

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 20, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Traversing PLC software tree.", 55, "read-tree");
                var tree = await _plcService.GetSoftwareTreeAsync(maxDepth);

                if (tree != null)
                {
                    var treeData = SerializeSoftwareTreeNode(tree);
                    var resultData = new
                    {
                        plcName,
                        tree = treeData
                    };
                    var message = "Software tree retrieved successfully";

                    if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(resultData, message))
                    {
                        context.Checkpoint("Writing PLC software tree JSON artifact.", 85, "write-artifact");
                        var outputPath = PlcCommandSupport.ResolveOutputPath(
                            args.Has("output-path")
                                ? args.GetString("output-path")
                                : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".json"),
                            "plc_software_tree.json");
                        var artifact = PlcCommandSupport.CreateJsonArtifact(
                            resultData,
                            outputPath,
                            "plc-software-tree",
                            CountSoftwareTreeNodes(tree),
                            args.Has("output-path"),
                            new
                            {
                                plcName,
                                maxDepth
                            });

                        context.CompleteCommand(Name, $"{message}. JSON written to: {outputPath}");
                        return CommandResult.CreateSuccess(
                            data: artifact,
                            message: $"{message}. JSON written to: {outputPath}"
                        );
                    }

                    context.CompleteCommand(Name, message);
                    return CommandResult.CreateSuccess(
                        data: resultData,
                        message: message
                    );
                }
                else
                {
                    context.CompleteCommand(Name, "Failed to get software tree");
                    return CommandResult.CreateFailure(
                        "Failed to get software tree"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting software tree");
                return CommandResult.CreateFailure(
                    $"Error getting software tree: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Serializes a SoftwareTreeNode to an anonymous object for JSON serialization.
        /// </summary>
        private object SerializeSoftwareTreeNode(SoftwareTreeNode node)
        {
            return new
            {
                name = node.Name,
                type = node.Type,
                path = node.Path,
                children = node.Children.Select(SerializeSoftwareTreeNode).ToArray()
            };
        }

        private static int CountSoftwareTreeNodes(SoftwareTreeNode node)
        {
            return 1 + node.Children.Sum(CountSoftwareTreeNodes);
        }
    }
}
