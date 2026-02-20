using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace OpenTiaToolkit.CLI
{
    /// <summary>
    /// Command to list all members in a GlobalDB for debugging.
    /// </summary>
    public class ListDbMembersCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListDbMembersCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ListDbMembersCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "list-db-members";

        /// <inheritdoc/>
        public string Description => "List all members in GlobalDBs for debugging";

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
                    "block-name",
                    CommandParameterType.String,
                    "Optional specific GlobalDB name (null = all)",
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

                var blockName = args.GetString("block-name");

                // Access the private _plcSoftware field via reflection
                var plcServiceField = _plcService.GetType().GetField("_plcSoftware", BindingFlags.NonPublic | BindingFlags.Instance);
                var plcSoftware = plcServiceField?.GetValue(_plcService);

                if (plcSoftware == null)
                {
                    return CommandResult.CreateFailure("PLC software is not open. Use open-plc first.");
                }

                var results = new List<object>();

                // Get BlockGroup
                var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                if (blockGroup == null)
                {
                    return CommandResult.CreateFailure("BlockGroup not found");
                }

                // Get all Global DBs
                var blocks = TiaReflectionHelper.GetBlocks(blockGroup);
                var blockList = new List<object>();

                if (blocks != null)
                {
                    foreach (var block in blocks)
                    {
                        var typeName = TiaReflectionHelper.GetTypeName(block);
                        if (typeName == "GlobalDB")
                        {
                            blockList.Add(block);
                        }
                    }
                }

                // Also search recursively in groups
                var groups = TiaReflectionHelper.GetGroups(blockGroup);
                if (groups != null)
                {
                    FindGlobalDBsRecursive(groups, blockList);
                }

                logger.LogInformation("Found {Count} GlobalDBs", blockList.Count);

                foreach (var block in blockList)
                {
                    var currentBlockName = TiaReflectionHelper.GetName(block) ?? "Unknown";

                    if (!string.IsNullOrEmpty(blockName) && !currentBlockName.Equals(blockName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    logger.LogInformation("Processing block: {BlockName}", currentBlockName);

                    try
                    {
                        var members = new List<object>();

                        // Get Structure property
                        var structure = TiaReflectionHelper.GetBlockStructure(block);
                        logger.LogInformation("  Structure type: {Type}", structure?.GetType().FullName);

                        if (structure != null)
                        {
                            // Get Composition from Structure
                            var composition = TiaReflectionHelper.GetStructureComposition(structure);
                            logger.LogInformation("  Composition type: {Type}", composition?.GetType().FullName);

                            if (composition != null)
                            {
                                // Get Members from Composition
                                var membersCollection = TiaReflectionHelper.GetCompositionMembers(composition);
                                logger.LogInformation("  Members collection type: {Type}", membersCollection != null ? "IEnumerable" : "null");

                                if (membersCollection != null)
                                {
                                    int count = 0;
                                    foreach (var member in membersCollection)
                                    {
                                        count++;
                                        var memberName = TiaReflectionHelper.GetName(member) ?? "Unknown";
                                        var memberType = member?.GetType().FullName;

                                        // Check if member has children (nested struct)
                                        var memberComposition = TiaReflectionHelper.GetStructureComposition(member);
                                        var hasChildren = memberComposition != null;

                                        members.Add(new
                                        {
                                            Index = count,
                                            Name = memberName,
                                            Type = memberType,
                                            HasChildren = hasChildren
                                        });

                                        logger.LogInformation("    Member {Count}: {Name} ({Type})", count, memberName, memberType);
                                    }
                                    logger.LogInformation("  Found {Count} members", count);
                                }
                            }

                            // If composition didn't work, try Members directly on structure
                            if (members.Count == 0)
                            {
                                var structMembers = TiaReflectionHelper.GetStructureMembers(structure);
                                if (structMembers != null)
                                {
                                    logger.LogInformation("  Found members directly on structure");
                                    int count = 0;
                                    foreach (var member in structMembers)
                                    {
                                        count++;
                                        var memberName = TiaReflectionHelper.GetName(member) ?? "Unknown";
                                        var memberType = member?.GetType().FullName;

                                        members.Add(new
                                        {
                                            Index = count,
                                            Name = memberName,
                                            Type = memberType,
                                            HasChildren = false
                                        });

                                        logger.LogInformation("    Member {Count}: {Name} ({Type})", count, memberName, memberType);
                                    }
                                    logger.LogInformation("  Found {Count} members via structure.Members", count);
                                }
                            }
                        }

                        results.Add(new
                        {
                            BlockName = currentBlockName,
                            BlockType = TiaReflectionHelper.GetTypeName(block),
                            MemberCount = members.Count,
                            Members = members
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing block: {BlockName}", currentBlockName);
                        results.Add(new
                        {
                            BlockName = currentBlockName,
                            Error = ex.Message,
                            StackTrace = ex.StackTrace
                        });
                    }
                }

                return CommandResult.CreateSuccess(
                    data: new
                    {
                        blocks = results
                    },
                    message: $"Listed {results.Count} blocks with {results.Sum(b => (int)(b.GetType().GetProperty("MemberCount")?.GetValue(b) ?? 0))} total members"
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in list-db-members");
                return CommandResult.CreateFailure(
                    $"Error: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Recursively finds GlobalDBs in groups.
        /// </summary>
        private void FindGlobalDBsRecursive(System.Collections.IEnumerable groups, List<object> blockList)
        {
            foreach (var group in groups)
            {
                var blocks = TiaReflectionHelper.GetBlocks(group);
                if (blocks != null)
                {
                    foreach (var block in blocks)
                    {
                        var typeName = TiaReflectionHelper.GetTypeName(block);
                        if (typeName == "GlobalDB")
                        {
                            blockList.Add(block);
                        }
                    }
                }

                var subGroups = TiaReflectionHelper.GetGroups(group);
                if (subGroups != null)
                {
                    FindGlobalDBsRecursive(subGroups, blockList);
                }
            }
        }
    }
}
