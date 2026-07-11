using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTiaToolkit.Application.Execution;

public static class AuditRedactor
{
    public static IReadOnlyDictionary<string, string> Redact(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, JsonElement> pair in arguments)
        {
            bool sensitive = pair.Key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pair.Key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pair.Key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0;
            result[pair.Key] = sensitive ? "[REDACTED]" : pair.Value.ToString();
        }

        return result;
    }
}
