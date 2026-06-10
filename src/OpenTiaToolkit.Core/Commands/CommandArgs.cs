using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
        _values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (values == null)
        {
            return;
        }

        foreach (var kvp in values)
        {
            _values[kvp.Key] = kvp.Value;
        }
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
        if (TryGetValue(name, out var value))
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString() ?? defaultValue ?? string.Empty,
                    JsonValueKind.Null or JsonValueKind.Undefined => defaultValue ?? string.Empty,
                    _ => jsonElement.ToString()
                };
            }

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
        if (TryGetValue(name, out var value))
        {
            if (value is int i)
                return i;
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out var jsonInt))
                    return jsonInt;
                if (jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), out var parsedJsonString))
                    return parsedJsonString;
            }
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
        if (TryGetValue(name, out var value))
        {
            if (value is bool b)
                return b;
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.True)
                    return true;
                if (jsonElement.ValueKind == JsonValueKind.False)
                    return false;
                if (jsonElement.ValueKind == JsonValueKind.String && bool.TryParse(jsonElement.GetString(), out var parsedJsonBool))
                    return parsedJsonBool;
            }
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
        if (TryGetValue(name, out var value))
        {
            if (value is string[] array)
                return array;
            if (value is IEnumerable<string> enumerable)
                return enumerable.ToArray();
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Array)
                {
                    return jsonElement
                        .EnumerateArray()
                        .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .ToArray();
                }

                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var singleValue = jsonElement.GetString();
                    return SplitStringArrayValue(singleValue);
                }
            }

            if (value is string stringValue)
            {
                return SplitStringArrayValue(stringValue);
            }
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
        return TryGetValue(name, out var value) && value != null;
    }

    /// <summary>
    /// Gets all argument names.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>
    /// Gets a raw object value by argument name.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The object value or null if not found.</returns>
    public object? GetObject(string name)
    {
        if (TryGetValue(name, out var value))
        {
            return value;
        }
        return null;
    }

    private bool TryGetValue(string name, out object? value)
    {
        if (_values.TryGetValue(name, out value))
        {
            return true;
        }

        var normalizedName = NormalizeKey(name);
        foreach (var kvp in _values)
        {
            if (NormalizeKey(kvp.Key) == normalizedName)
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string NormalizeKey(string key)
    {
        return new string(key
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string[] SplitStringArrayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value!
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => item.Trim())
                .Where(static item => item.Length > 0)
                .ToArray();
    }
}
