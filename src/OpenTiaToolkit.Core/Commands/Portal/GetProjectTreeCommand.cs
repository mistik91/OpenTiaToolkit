using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to get the hierarchical tree structure of the current project.
    /// </summary>
    public class GetProjectTreeCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetProjectTreeCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public GetProjectTreeCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "get-project-tree";

        /// <inheritdoc/>
        public string Description => "Gets the hierarchical project structure";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "depth",
                    CommandParameterType.Integer,
                    "Maximum depth to traverse (null for unlimited)",
                    required: false,
                    defaultValue: null)
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
            context.StartCommand(Name, "Reading TIA Portal project tree.");

            try
            {
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var depth = args.GetInt("depth");
                context.Checkpoint("Traversing project tree.", 50, "get-project-tree");
                var tree = await _portalService.GetProjectTreeAsync(depth);

                if (tree != null)
                {
                    context.Checkpoint("Serializing project tree result.", 85, "serialize-tree");
                    var treeData = SerializeTreeNode(tree);

                    context.CompleteCommand(Name, "Project tree retrieved successfully");
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            tree = treeData
                        },
                        message: "Project tree retrieved successfully"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to get project tree (no project open or error reading structure)"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting project tree");
                return CommandResult.CreateFailure(
                    $"Error getting project tree: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Serializes a ProjectTreeNode to an anonymous object for JSON serialization.
        /// </summary>
        private object SerializeTreeNode(ProjectTreeNode node)
        {
            return new
            {
                name = node.Name,
                type = node.Type,
                path = node.Path,
                children = node.Children.Select(SerializeTreeNode).ToArray()
            };
        }
    }
}
