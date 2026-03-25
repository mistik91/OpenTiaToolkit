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

                // Parse command name and arguments
                var commandName = args[0].TrimStart('-');
                var commandArgs = ParseArguments(args.Skip(1).ToArray());

                // Find the command
                if (!_commands.TryGetValue(commandName, out var command))
                {
                    var availableCommands = string.Join(", ", _commands.Keys.OrderBy(k => k));
                    return CommandResult.CreateFailure(
                        $"Unknown command: '{commandName}'. Available commands: {availableCommands}"
                    );
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
        private CommandArgs ParseArguments(string[] args)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                // Check if it's a named argument (starts with -- or -)
                if (arg.StartsWith("--"))
                {
                    var name = arg.Substring(2).Trim();

                    // Check if it's in --name=value format
                    if (name.Contains("="))
                    {
                        var parts = name.Split('=');
                        var key = parts[0];
                        var value = parts.Length > 1 ? string.Join("=", parts.Skip(1)) : "true";
                        values[key] = value;
                    }
                    // Check if next arg is a value (not another flag)
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        values[name] = args[i + 1];
                        i++; // Skip next arg as we've consumed it
                    }
                    else
                    {
                        // Boolean flag
                        values[name] = "true";
                    }
                }
                else if (arg.StartsWith("-"))
                {
                    // Short form argument
                    var name = arg.Substring(1).Trim();
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        values[name] = args[i + 1];
                        i++; // Skip next arg as we've consumed it
                    }
                    else
                    {
                        values[name] = "true";
                    }
                }
                else
                {
                    // Positional argument - store with index
                    values[$"pos_{values.Count}"] = arg;
                }
            }

            return new CommandArgs(values);
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
                    "Usage: OpenTiaToolkit.CLI.exe <command> [options]",
                    "",
                    "Available commands:"
                };

                foreach (var kvp in _commands.OrderBy(k => k.Key))
                {
                    var command = kvp.Value;
                    lines.Add($"  {command.Name,-25} {command.Description}");
                }

                lines.Add("");
                lines.Add("Use 'help <command>' for more information about a specific command.");
                lines.Add("");
                lines.Add("Examples:");
                lines.Add("  OpenTiaToolkit.CLI.exe connect-portal");
                lines.Add("  OpenTiaToolkit.CLI.exe open-project --project-path=\"C:\\Projects\\My.ap21\"");
                lines.Add("  OpenTiaToolkit.CLI.exe list-devices");

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
    }
}
