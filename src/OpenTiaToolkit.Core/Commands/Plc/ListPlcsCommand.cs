using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Information about a PLC device found in the project.
    /// </summary>
    public class PlcInfo
    {
        /// <summary>Gets or sets the name of the PLC device.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the type/classification of the PLC.</summary>
        public string? Type { get; set; }

        /// <summary>Gets or sets the parent device name.</summary>
        public string? ParentDevice { get; set; }

        /// <summary>Gets or sets a value indicating whether this is a confirmed PLC.</summary>
        public bool IsConfirmedPlc { get; set; }
    }

    /// <summary>
    /// Command to list all PLC devices in the current project.
    /// </summary>
    public class ListPlcsCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListPlcsCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ListPlcsCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "list-plcs";

        /// <inheritdoc/>
        public string Description => "Lists all PLC devices found in the project";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var plcs = await _portalService.GetPlcsAsync();

                if (plcs == null || !plcs.Any())
                {
                    return CommandResult.CreateSuccess(
                        data: Array.Empty<PlcInfo>(),
                        message: "No PLC devices found in the project"
                    );
                }

                var plcList = plcs.ToList();
                return CommandResult.CreateSuccess(
                    data: plcList,
                    message: $"Found {plcList.Count} PLC device(s)"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error listing PLCs");
                return CommandResult.CreateFailure(
                    $"Error listing PLCs: {ex.Message}",
                    ex
                );
            }
        }
    }
}
