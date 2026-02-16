using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Commands;

/// <summary>
/// Provides unified argument handling for commands.
/// Used by both CLI argument parsers and MCP request handlers.
/// </summary>
public class CommandArgs
{
    private readonly Dictionary<string, object?> _values;

    /// <summary>
    /// Initializes a new instance of the CommandArgs class.
    /// </summary>
    /// <param name="values">Dictionary of argument names to values.</param>
    public CommandArgs(Dictionary<string, object?> values)
    {
        _values = values ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates an empty CommandArgs instance.
    /// </summary>
    public static CommandArgs Empty => new(new Dictionary<string, object?>());

    /// <summary>
    /// Gets a string value by argument name.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="defaultValue">Optional default value if not found.</param>
    /// <returns>The string value or default.</returns>
    public string GetString(string name, string? defaultValue = null)
    {
        if (_values.TryGetValue(name, out var value))
        {
            return value?.ToString() ?? defaultValue ?? string.Empty;
        }
        return defaultValue ?? string.Empty;
    }

    /// <summary>
    /// Gets an integer value by argument name.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="defaultValue">Optional default value if not found or invalid.</param>
    /// <returns>The integer value or default.</returns>
    public int GetInt(string name, int defaultValue = 0)
    {
        if (_values.TryGetValue(name, out var value))
        {
            if (value is int i)
                return i;
            if (int.TryParse(value?.ToString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets a boolean value by argument name.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="defaultValue">Optional default value if not found.</param>
    /// <returns>The boolean value or default.</returns>
    public bool GetBool(string name, bool defaultValue = false)
    {
        if (_values.TryGetValue(name, out var value))
        {
            if (value is bool b)
                return b;
            if (bool.TryParse(value?.ToString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets a string array value by argument name.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The string array or empty array if not found.</returns>
    public string[] GetStringArray(string name)
    {
        if (_values.TryGetValue(name, out var value))
        {
            if (value is string[] array)
                return array;
            if (value is IEnumerable<string> enumerable)
                return enumerable.ToArray();
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// Checks if an argument was provided.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>True if the argument exists and is not null.</returns>
    public bool Has(string name)
    {
        return _values.ContainsKey(name) && _values[name] != null;
    }

    /// <summary>
    /// Gets all argument names.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;
}
