using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to open a new TIA Portal instance.
    /// Starts with user interface by default (can be disabled with --with-ui=false).
    /// </summary>
    public class OpenTiaPortalCommand : ICommand
    {
        private readonly IPortalService _portalService;
        private readonly HmiWebViewSession _captureSessions;

        public OpenTiaPortalCommand(IPortalService portalService) : this(portalService, new HmiWebViewSession()) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenTiaPortalCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        /// <param name="captureSessions">The launcher for capture-ready instances.</param>
        public OpenTiaPortalCommand(IPortalService portalService, HmiWebViewSession captureSessions)
        {
            _portalService = portalService;
            _captureSessions = captureSessions;
        }

        /// <inheritdoc/>
        public string Name => "open-tia-portal";

        /// <inheritdoc/>
        public string Description => "Opens a new TIA Portal instance (with UI by default)";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "with-ui",
                    CommandParameterType.Boolean,
                    "Start TIA Portal with user interface",
                    required: false,
                    defaultValue: true),
                new CommandParameter("hmi-capture", CommandParameterType.Boolean,
                    "Launch a visible V21 instance with an isolated WebView2 profile and local HMI capture enabled",
                    required: false, defaultValue: false)
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var withUi = args.GetBool("with-ui", true);
                if (args.GetBool("hmi-capture"))
                {
                    if (!withUi)
                        return CommandResult.CreateFailure("HMI editor capture requires a visible TIA Portal instance.");

                    var instanceId = _captureSessions.Launch();
                    return CommandResult.CreateSuccess(
                        new { opened = true, version = "V21", withUi, hmiCapture = true, instanceId },
                        "Started capture-ready TIA Portal. Use connect-instance with the returned instance ID after TIA starts.");
                }

                var success = await _portalService.OpenTiaPortalAsync(withUi);

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            opened = true,
                            version = _portalService.Version,
                            withUi
                        },
                        message: $"Opened new TIA Portal instance {_portalService.Version} (UI: {withUi})"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to open TIA Portal instance"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error opening TIA Portal");
                return CommandResult.CreateFailure(
                    $"Error opening TIA Portal: {ex.Message}",
                    ex
                );
            }
        }
    }
}
