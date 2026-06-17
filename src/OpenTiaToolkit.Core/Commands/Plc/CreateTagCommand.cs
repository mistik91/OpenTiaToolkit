using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to create a new PLC tag.
    /// </summary>
    public class CreateTagCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateTagCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public CreateTagCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "create-tag";

        /// <inheritdoc/>
        public string Description => "Creates a new PLC tag in the specified tag table";

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
                    "tag-name",
                    CommandParameterType.String,
                    "The name of the tag to create",
                    required: true),
                new CommandParameter(
                    "data-type",
                    CommandParameterType.String,
                    "The data type (e.g., 'Bool', 'Int', 'DInt', 'Real', 'String')",
                    required: true),
                new CommandParameter(
                    "logical-address",
                    CommandParameterType.String,
                    "Optional logical address (e.g., '%M10.0', '%MW12', '%DB1.DBD0')",
                    required: false),
                new CommandParameter(
                    "tag-table",
                    CommandParameterType.String,
                    "Optional tag table name (default: first/default table)",
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
                context.StartCommand(Name, "Creating PLC tag.");
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var tagName = args.GetString("tag-name");
                if (string.IsNullOrEmpty(tagName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: tag-name");
                }

                var dataType = args.GetString("data-type");
                if (string.IsNullOrEmpty(dataType))
                {
                    return CommandResult.CreateFailure("Missing required parameter: data-type");
                }

                var logicalAddress = args.GetString("logical-address");
                var tagTable = args.GetString("tag-table");

                logger.LogInformation("Create-tag: PLC={PlcName}, Tag={TagName}, Type={DataType}, Address={Address}, Table={Table}",
                    plcName, tagName, dataType, logicalAddress ?? "(auto)", tagTable ?? "(default)");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint($"Creating PLC tag: {tagName}.", 65, "create-tag");
                var result = await _plcService.CreateTagAsync(tagName, dataType, logicalAddress, tagTable);

                if (!result.Success)
                {
                    var errorMsg = result.Error?.Error ?? "Unknown error";
                    var details = result.Error?.Details;
                    var fullMessage = string.IsNullOrEmpty(details) ? errorMsg : $"{errorMsg}: {details}";
                    context.CompleteCommand(Name, fullMessage);
                    return CommandResult.CreateFailure(fullMessage);
                }

                // Build success message
                var message = $"Tag '{result.TagName}' created successfully in '{result.TagTableName}' " +
                            $"(Type: {result.DataType}, Address: {result.LogicalAddress ?? "(auto-assigned)"})";

                // Return result with data
                context.CompleteCommand(Name, message);
                return CommandResult.CreateSuccess(
                    data: new
                    {
                        tagName = result.TagName,
                        tagTableName = result.TagTableName,
                        dataType = result.DataType,
                        logicalAddress = result.LogicalAddress,
                        warnings = result.Warnings
                    },
                    message: message
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in create-tag");
                return CommandResult.CreateFailure(
                    $"Error in create-tag: {ex.Message}",
                    ex
                );
            }
        }
    }
}
