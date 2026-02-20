using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Commands;

/// <summary>
/// Defines the schema and expected arguments for a command.
/// Used for validation, help text generation, and MCP tool schema generation.
/// </summary>
public class CommandSchema
{
    /// <summary>
    /// Initializes a new instance of the CommandSchema class.
    /// </summary>
    /// <param name="description">Detailed description of what the command does.</param>
    /// <param name="parameters">Optional list of parameters the command accepts.</param>
    public CommandSchema(string description, IReadOnlyList<CommandParameter>? parameters = null)
    {
        Description = description;
        Parameters = parameters ?? new List<CommandParameter>();
    }

    /// <summary>
    /// Gets the detailed description of the command.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the list of parameters that this command accepts.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; }
}

/// <summary>
/// Defines a single parameter for a command.
/// </summary>
public class CommandParameter
{
    /// <summary>
    /// Initializes a new instance of the CommandParameter class.
    /// </summary>
    /// <param name="name">The kebab-case parameter name (e.g., "project-path").</param>
    /// <param name="type">The type of the parameter (string, int, bool, etc.).</param>
    /// <param name="description">Human-readable description of the parameter.</param>
    /// <param name="required">Whether the parameter is required (default: false).</param>
    /// <param name="defaultValue">Optional default value if not provided.</param>
    public CommandParameter(
        string name,
        CommandParameterType type,
        string description,
        bool required = false,
        object? defaultValue = null)
    {
        Name = name;
        Type = type;
        Description = description;
        Required = required;
        DefaultValue = defaultValue;
    }

    /// <summary>
    /// Gets the parameter name in kebab-case.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the parameter type.
    /// </summary>
    public CommandParameterType Type { get; }

    /// <summary>
    /// Gets the parameter description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets whether the parameter is required.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Gets the optional default value for the parameter.
    /// </summary>
    public object? DefaultValue { get; }
}

/// <summary>
/// Defines the supported parameter types for commands.
/// </summary>
public enum CommandParameterType
{
    /// <summary>String value</summary>
    String,
    /// <summary>Integer value</summary>
    Integer,
    /// <summary>Boolean value (true/false)</summary>
    Boolean,
    /// <summary>Array of strings</summary>
    StringArray,
    /// <summary>Path to a file or directory</summary>
    Path,
    /// <summary>Object value (JSON object or Dictionary)</summary>
    Object
}
