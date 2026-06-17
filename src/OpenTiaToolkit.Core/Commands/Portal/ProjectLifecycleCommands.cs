using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Security;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    internal static class ProjectCommandSupport
    {
        public const string DefaultUmacPasswordEnvironmentVariable = "OPENTIA_UMAC_PASSWORD";

        public static ProjectAuthenticationOptions? GetAuthenticationOptions(CommandArgs args)
        {
            var user = args.GetString("umac-user");
            if (string.IsNullOrWhiteSpace(user))
            {
                return null;
            }

            var passwordEnvironmentVariable = args.GetString("umac-password-env", DefaultUmacPasswordEnvironmentVariable);
            var password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable);
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    $"UMAC password environment variable '{passwordEnvironmentVariable}' is not set.");
            }

            return new ProjectAuthenticationOptions(
                user,
                ToSecureString(password),
                args.GetString("umac-user-type", "project"));
        }

        public static CommandParameter[] UmacParameters()
        {
            return new[]
            {
                new CommandParameter("umac-user", CommandParameterType.String, "Optional UMAC user name", required: false),
                new CommandParameter("umac-password-env", CommandParameterType.String, $"Environment variable containing the UMAC password (default: {DefaultUmacPasswordEnvironmentVariable})", required: false, defaultValue: DefaultUmacPasswordEnvironmentVariable),
                new CommandParameter("umac-user-type", CommandParameterType.String, "UMAC user type: project or global", required: false, defaultValue: "project")
            };
        }

        private static SecureString ToSecureString(string value)
        {
            var secureString = new SecureString();
            foreach (var character in value)
            {
                secureString.AppendChar(character);
            }

            secureString.MakeReadOnly();
            return secureString;
        }
    }

    public sealed class CreateProjectCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        public CreateProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        public string Name => "create-project";

        public string Description => "Creates a new TIA Portal project and selects it as the current project";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("project-path", CommandParameterType.Path, "Project folder path or .ap21 file path", required: true)
        });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                context.StartCommand(Name, "Creating TIA Portal project.");
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var projectPath = args.GetString("project-path");
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return CommandResult.CreateFailure("Project path is required");
                }

                context.Checkpoint($"Creating TIA Portal project: {projectPath}.", 55, "create-project");
                var result = await _portalService.CreateProjectAsync(projectPath);
                context.CompleteCommand(Name, result == null ? $"Failed to create project: {projectPath}" : $"Project created: {result.Name}");
                return result == null
                    ? CommandResult.CreateFailure(FormatFailure(_portalService, $"Failed to create project: {projectPath}"))
                    : CommandResult.CreateSuccess(result, $"Project created: {result.Name}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating project");
                return CommandResult.CreateFailure($"Error creating project: {ex.Message}", ex);
            }
        }

        private static string FormatFailure(IPortalService portalService, string message)
        {
            return string.IsNullOrWhiteSpace(portalService.LastError)
                ? message
                : $"{message}: {portalService.LastError}";
        }
    }

    public sealed class AddDeviceCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        public AddDeviceCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        public string Name => "add-device";

        public string Description => "Adds a device with its primary item to the current project";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("order-number", CommandParameterType.String, "TIA hardware type identifier or order number", required: true),
            new CommandParameter("version", CommandParameterType.String, "Optional catalog version appended to the order number when needed", required: false),
            new CommandParameter("name", CommandParameterType.String, "Name for the device and primary item", required: true)
        });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                context.StartCommand(Name, "Adding device to current project.");
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var orderNumber = args.GetString("order-number");
                var name = args.GetString("name");
                if (string.IsNullOrWhiteSpace(orderNumber))
                {
                    return CommandResult.CreateFailure("order-number is required");
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    return CommandResult.CreateFailure("name is required");
                }

                context.Checkpoint($"Adding device: {name}.", 55, "add-device");
                var result = await _portalService.AddDeviceAsync(
                    orderNumber,
                    args.Has("version") ? args.GetString("version") : null,
                    name);

                context.CompleteCommand(Name, result == null ? $"Failed to add device: {name}" : $"Device added: {result.Name}");
                return result == null
                    ? CommandResult.CreateFailure(FormatFailure(_portalService, $"Failed to add device: {name}"))
                    : CommandResult.CreateSuccess(result, $"Device added: {result.Name}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding device");
                return CommandResult.CreateFailure($"Error adding device: {ex.Message}", ex);
            }
        }

        private static string FormatFailure(IPortalService portalService, string message)
        {
            return string.IsNullOrWhiteSpace(portalService.LastError)
                ? message
                : $"{message}: {portalService.LastError}";
        }
    }

    public sealed class ArchiveProjectCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        public ArchiveProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        public string Name => "archive-project";

        public string Description => "Archives the current project to a .zap21 archive";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("archive-path", CommandParameterType.Path, "Target .zap21 archive path", required: true),
            new CommandParameter("mode", CommandParameterType.String, "Archive mode: Compressed, None, DiscardRestorableData, or DiscardRestorableDataAndCompressed", required: false, defaultValue: "Compressed")
        });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Validating project archive inputs.");

            try
            {
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var archivePath = args.GetString("archive-path");
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    return CommandResult.CreateFailure("archive-path is required");
                }

                context.Checkpoint("Archiving current TIA Portal project.", 55, "archive-project");
                var result = await _portalService.ArchiveProjectAsync(
                    archivePath,
                    args.GetString("mode", "Compressed"));

                if (result != null)
                {
                    context.CompleteCommand(Name, $"Project archived: {result.ArchivePath}");
                }

                return result == null
                    ? CommandResult.CreateFailure(FormatFailure(_portalService, $"Failed to archive project: {archivePath}"))
                    : CommandResult.CreateSuccess(result, $"Project archived: {result.ArchivePath}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error archiving project");
                return CommandResult.CreateFailure($"Error archiving project: {ex.Message}", ex);
            }
        }

        private static string FormatFailure(IPortalService portalService, string message)
        {
            return string.IsNullOrWhiteSpace(portalService.LastError)
                ? message
                : $"{message}: {portalService.LastError}";
        }
    }

    public sealed class RetrieveProjectCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        public RetrieveProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        public string Name => "retrieve-project";

        public string Description => "Retrieves an archived project and selects it as the current project";

        public CommandSchema Schema => new CommandSchema(Description, Combine(
            new[]
            {
                new CommandParameter("archive-path", CommandParameterType.Path, "Source .zap archive path", required: true),
                new CommandParameter("target-directory", CommandParameterType.Path, "Directory where the project should be retrieved", required: true),
                new CommandParameter("upgrade", CommandParameterType.Boolean, "Upgrade the retrieved project to the current TIA version", required: false, defaultValue: true)
            },
            ProjectCommandSupport.UmacParameters()));

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Validating project retrieve inputs.");

            try
            {
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var archivePath = args.GetString("archive-path");
                var targetDirectory = args.GetString("target-directory");
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    return CommandResult.CreateFailure("archive-path is required");
                }

                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    return CommandResult.CreateFailure("target-directory is required");
                }

                context.Checkpoint("Retrieving archived TIA Portal project.", 55, "retrieve-project");
                var result = await _portalService.RetrieveProjectAsync(
                    archivePath,
                    targetDirectory,
                    !args.Has("upgrade") || args.GetBool("upgrade", true),
                    ProjectCommandSupport.GetAuthenticationOptions(args));

                if (result != null)
                {
                    context.CompleteCommand(Name, $"Project retrieved: {result.Name}");
                }

                return result == null
                    ? CommandResult.CreateFailure(FormatFailure(_portalService, $"Failed to retrieve project: {archivePath}"))
                    : CommandResult.CreateSuccess(result, $"Project retrieved: {result.Name}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving project");
                return CommandResult.CreateFailure($"Error retrieving project: {ex.Message}", ex);
            }
        }

        private static CommandParameter[] Combine(CommandParameter[] first, CommandParameter[] second)
        {
            var result = new CommandParameter[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);
            return result;
        }

        private static string FormatFailure(IPortalService portalService, string message)
        {
            return string.IsNullOrWhiteSpace(portalService.LastError)
                ? message
                : $"{message}: {portalService.LastError}";
        }
    }
}
