using System.Linq;

namespace OpenTiaToolkit.Core.Services.V21;

internal static class V21DocumentMessages
{
    internal static string[] Read(object? result) =>
        (TiaReflectionHelper.GetEnumerableProperty(result, "Messages") ?? Enumerable.Empty<object>())
        .Select(message => TiaReflectionHelper.GetProperty(message, "Message")?.ToString())
        .Where(message => !string.IsNullOrWhiteSpace(message))
        .Select(message => message!).ToArray();
}
