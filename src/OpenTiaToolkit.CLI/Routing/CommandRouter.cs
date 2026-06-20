using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.CLI.Routing
{
    /// <summary>
    /// Routes command-line arguments to the appropriate command implementation.
    /// </summary>
    public class CommandRouter
    {
        private readonly Dictionary<string, ICommand> _commands;
        private readonly ILogger<CommandRouter> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandRouter"/> class.
        /// </summary>
        /// <param name="commands">The available commands.</param>
        /// <param name="logger">Logger instance.</param>
        public CommandRouter(IEnumerable<ICommand> commands, ILogger<CommandRouter> logger)
        {
            _commands = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            _logger = logger;
        }

        /// <summary>
        /// Executes a command based on the provided arguments.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>The execution result.</returns>
        public async Task<CommandResult> ExecuteAsync(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    return CommandResult.CreateFailure(
                        "No command specified. Use 'help' to see available commands."
                    );
                }

                var commandName = args[0];

                // Find the command
                if (!_commands.TryGetValue(commandName, out var command))
                {
                    var availableCommands = string.Join(", ", _commands.Keys.OrderBy(k => k));
                    return CommandResult.CreateFailure(
                        $"Unknown command: '{commandName}'. Available commands: {availableCommands}"
                    );
                }

                var commandArgs = ParseArguments(args.Skip(1).ToArray(), command.Schema, out var parseError);
                if (parseError != null)
                {
                    return CommandResult.CreateFailure(parseError);
                }

                _logger.LogInformation("Routing to command: {CommandName}", command.Name);

                // Execute the command
                return await command.ExecuteAsync(commandArgs, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command");
                return CommandResult.CreateFailure(
                    $"Error executing command: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// Gets all available commands.
        /// </summary>
        public IReadOnlyDictionary<string, ICommand> Commands => _commands;

        /// <summary>
        /// Parses command-line arguments into a CommandArgs dictionary.
        /// Supports both --name=value and --name value formats.
        /// </summary>
        private CommandArgs ParseArguments(string[] args, CommandSchema schema, out string? error)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var parameters = schema.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                // Check if it's a named argument (starts with -- or -)
                if (arg.StartsWith("--"))
                {
                    var name = arg.Substring(2).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = "Empty parameter name.";
                        return CommandArgs.Empty;
                    }

                    // Check if it's in --name=value format
                    if (name.Contains("="))
                    {
                        var equalsIndex = name.IndexOf('=');
                        var key = name.Substring(0, equalsIndex);
                        var value = name.Substring(equalsIndex + 1);
                        if (!parameters.ContainsKey(key))
                        {
                            error = $"Unknown parameter: --{key}";
                            return CommandArgs.Empty;
                        }

                        values[key] = value;
                    }
                    else if (!parameters.TryGetValue(name, out var parameter))
                    {
                        error = $"Unknown parameter: --{name}";
                        return CommandArgs.Empty;
                    }
                    else if (ShouldConsumeValue(parameter, args, i))
                    {
                        values[name] = args[i + 1];
                        i++; // Skip next arg as we've consumed it
                    }
                    else if (parameter.Type == CommandParameterType.Boolean)
                    {
                        // Boolean flag
                        values[name] = "true";
                    }
                    else
                    {
                        error = $"Missing value for parameter: --{name}";
                        return CommandArgs.Empty;
                    }
                }
                else if (arg.StartsWith("-"))
                {
                    // Short form argument
                    var name = arg.Substring(1).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = "Empty parameter name.";
                        return CommandArgs.Empty;
                    }

                    if (!parameters.TryGetValue(name, out var parameter))
                    {
                        error = $"Unknown parameter: -{name}";
                        return CommandArgs.Empty;
                    }

                    if (ShouldConsumeValue(parameter, args, i))
                    {
                        values[name] = args[i + 1];
                        i++; // Skip next arg as we've consumed it
                    }
                    else if (parameter.Type == CommandParameterType.Boolean)
                    {
                        values[name] = "true";
                    }
                    else
                    {
                        error = $"Missing value for parameter: -{name}";
                        return CommandArgs.Empty;
                    }
                }
                else
                {
                    error = $"Unexpected positional argument: {arg}";
                    return CommandArgs.Empty;
                }
            }

            foreach (var parameter in schema.Parameters.Where(parameter => parameter.Required))
            {
                if (!values.TryGetValue(parameter.Name, out var value) || IsMissingValue(value))
                {
                    error = $"Missing required parameter: {parameter.Name}";
                    return CommandArgs.Empty;
                }
            }

            return new CommandArgs(values);
        }

        private static bool ShouldConsumeValue(CommandParameter parameter, string[] args, int currentIndex)
        {
            if (currentIndex + 1 >= args.Length)
            {
                return false;
            }

            if (parameter.Type == CommandParameterType.Boolean)
            {
                return !IsNamedArgument(args[currentIndex + 1]) &&
                       bool.TryParse(args[currentIndex + 1], out _);
            }

            return !IsNamedArgument(args[currentIndex + 1]) ||
                   (parameter.Type == CommandParameterType.Integer && IsNegativeInteger(args[currentIndex + 1]));
        }

        private static bool IsNamedArgument(string value)
        {
            return value.StartsWith("-");
        }

        private static bool IsNegativeInteger(string value)
        {
            return int.TryParse(value, out var parsed) && parsed < 0;
        }

        private static bool IsMissingValue(object? value)
        {
            return value == null || value is string stringValue && string.IsNullOrWhiteSpace(stringValue);
        }

        /// <summary>
        /// Prints help information for all commands or a specific command.
        /// </summary>
        /// <param name="commandName">Optional command name to get detailed help for.</param>
        /// <returns>Help text string.</returns>
        public string GetHelp(string? commandName = null)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                // General help - list all commands
                var lines = new List<string>
                {
                    "OpenTiaToolkit CLI - TIA Portal Automation Tool",
                    "",
                    "Usage: OpenTiaToolkit.CLI.exe <command> [options] [--format table|json]",
                    "       OpenTiaToolkit.CLI.exe --script <file>|-",
                    "",
                    "Available commands:"
                };

                foreach (var group in _commands
                             .Values
                             .OrderBy(GetCommandCategory)
                             .ThenBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                             .GroupBy(GetCommandCategory))
                {
                    lines.Add("");
                    lines.Add(group.Key + ":");
                    foreach (var command in group)
                    {
                        lines.Add($"  {command.Name,-25} {command.Description}");
                    }
                }

                lines.Add("");
                lines.Add("Global options:");
                lines.Add("  --format table|json       Select table output for humans or JSON output for scripts.");
                lines.Add("  --script <file>|-         Execute REPL-shaped commands from a file or stdin in one stateful process.");
                lines.Add("");
                lines.Add("Use 'help <command>' or '<command> --help' for more information about a specific command.");
                lines.Add("");
                lines.Add("Examples:");
                lines.Add("  OpenTiaToolkit.CLI.exe connect-portal");
                lines.Add("  OpenTiaToolkit.CLI.exe open-project --project-path=\"C:\\Projects\\My.ap21\"");
                lines.Add("  OpenTiaToolkit.CLI.exe list-devices");
                lines.Add("  OpenTiaToolkit.CLI.exe list-devices --format json");
                lines.Add("  OpenTiaToolkit.CLI.exe --script .\\inspect-project.commands.txt");

                return string.Join(Environment.NewLine, lines);
            }
            else
            {
                // Specific command help
                var lookupName = commandName!.TrimStart('-');
                if (!_commands.TryGetValue(lookupName, out var command))
                {
                    return $"Unknown command: '{commandName}'";
                }

                var lines = new List<string>
                {
                    $"Command: {command.Name}",
                    $"Description: {command.Description}",
                    ""
                };

                if (command.Schema.Parameters.Any())
                {
                    lines.Add("Parameters:");
                    foreach (var param in command.Schema.Parameters)
                    {
                        var required = param.Required ? "required" : $"optional (default: {param.DefaultValue ?? "none"})";
                        lines.Add($"  --{param.Name,-20} {param.Type,-12} {required}");
                        lines.Add($"    {param.Description}");
                        lines.Add("");
                    }
                }
                else
                {
                    lines.Add("No parameters.");
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        private static string GetCommandCategory(ICommand command)
        {
            var commandNamespace = command.GetType().Namespace ?? string.Empty;
            var segments = commandNamespace.Split('.');
            var category = segments.Length > 0 ? segments[segments.Length - 1] : string.Empty;

            switch (category)
            {
                case "Portal":
                    return "Portal";
                case "Plc":
                    return "PLC";
                case "Hmi":
                    return "Unified HMI";
                case "Hardware":
                    return "Hardware";
                case "Vci":
                    return "VCI";
                case "Devices":
                    return "Devices";
                default:
                    return "Other";
            }
        }
    }
}
