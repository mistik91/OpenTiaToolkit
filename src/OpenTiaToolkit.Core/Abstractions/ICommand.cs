using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Core.Abstractions;

/// <summary>
/// Defines a command that can be executed by both CLI and MCP interfaces.
/// All commands must implement this interface to ensure unified behavior.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Gets the kebab-case command name (e.g., "open-project", "list-blocks").
    /// This name is used for both CLI invocation and MCP tool registration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a human-readable description of what the command does.
    /// Used for help text and MCP tool descriptions.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the expected arguments schema for this command.
    /// Defines what parameters the command accepts and their types.
    /// </summary>
    CommandSchema Schema { get; }

    /// <summary>
    /// Executes the command with the provided arguments.
    /// </summary>
    /// <param name="args">The command arguments parsed from CLI or MCP request.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>A CommandResult containing the execution outcome.</returns>
    Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger);
}
