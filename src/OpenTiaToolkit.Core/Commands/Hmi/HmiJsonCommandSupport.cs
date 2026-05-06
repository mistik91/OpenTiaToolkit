using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    internal static class HmiJsonCommandSupport
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static HmiJsonFilterOptions BuildFilters(CommandArgs args)
        {
            return new HmiJsonFilterOptions
            {
                Screens = ParseListArgument(args, "screens"),
                ItemNames = ParseListArgument(args, "item-names"),
                Properties = ParseListArgument(args, "properties")
            };
        }

        private static IReadOnlyList<string> ParseListArgument(CommandArgs args, string name)
        {
            var rawObject = args.GetObject(name);
            if (rawObject is string[] directArray)
            {
                return directArray
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (rawObject is IEnumerable<string> directEnumerable && rawObject is not string)
            {
                return directEnumerable
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var rawValue = args.GetString(name);

            if (rawObject is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Array)
                {
                    return jsonElement
                        .EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    rawValue = jsonElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Array.Empty<string>();
            }

            return rawValue!
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool TryGetScreenArgument(CommandArgs args, string name, out HmiJsonScreen? screen, out string? error)
        {
            screen = null;
            error = null;

            var rawObject = args.GetObject(name);
            if (rawObject == null)
            {
                error = $"Missing required parameter: {name}";
                return false;
            }

            if (rawObject is HmiJsonScreen directScreen)
            {
                screen = directScreen;
                return true;
            }

            try
            {
                switch (rawObject)
                {
                    case JsonElement jsonElement:
                        screen = DeserializeScreen(jsonElement);
                        break;
                    case string jsonText:
                        screen = JsonSerializer.Deserialize<HmiJsonScreen>(jsonText, JsonOptions);
                        break;
                    default:
                        screen = JsonSerializer.Deserialize<HmiJsonScreen>(
                            JsonSerializer.Serialize(rawObject, JsonOptions),
                            JsonOptions);
                        break;
                }

                if (screen == null)
                {
                    error = $"Parameter {name} could not be deserialized as a Unified HMI screen JSON object.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Parameter {name} is not valid Unified HMI screen JSON: {ex.Message}";
                return false;
            }
        }

        public static bool TryGetPatchOperationsArgument(
            CommandArgs args,
            string name,
            out IReadOnlyList<HmiScreenJsonPatchOperation>? operations,
            out string? error)
        {
            operations = null;
            error = null;

            var rawObject = args.GetObject(name);
            if (rawObject == null)
            {
                error = $"Missing required parameter: {name}";
                return false;
            }

            if (rawObject is HmiScreenJsonPatchRequest directRequest)
            {
                return ValidateOperations(name, directRequest.Operations, out operations, out error);
            }

            if (rawObject is HmiScreenJsonPatchOperation directOperation)
            {
                operations = new[] { directOperation };
                return true;
            }

            if (rawObject is IEnumerable<HmiScreenJsonPatchOperation> directOperations)
            {
                return ValidateOperations(name, directOperations.ToList(), out operations, out error);
            }

            try
            {
                IReadOnlyList<HmiScreenJsonPatchOperation>? parsedOperations;
                switch (rawObject)
                {
                    case JsonElement jsonElement:
                        parsedOperations = DeserializePatchOperations(jsonElement);
                        break;
                    case string jsonText:
                        return TryParsePatchOperationsJson(name, jsonText, out operations, out error);
                    default:
                        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(rawObject, JsonOptions)))
                        {
                            parsedOperations = DeserializePatchOperations(document.RootElement);
                        }

                        break;
                }

                return ValidateOperations(name, parsedOperations, out operations, out error);
            }
            catch (Exception ex)
            {
                error = $"Parameter {name} is not valid Unified HMI screen patch operations JSON: {ex.Message}";
                return false;
            }
        }

        public static bool TryParsePatchOperationsJson(
            string name,
            string jsonText,
            out IReadOnlyList<HmiScreenJsonPatchOperation>? operations,
            out string? error)
        {
            operations = null;
            error = null;

            try
            {
                using var document = JsonDocument.Parse(jsonText);
                var parsedOperations = DeserializePatchOperations(document.RootElement);
                return ValidateOperations(name, parsedOperations, out operations, out error);
            }
            catch (Exception ex)
            {
                error = $"Parameter {name} is not valid Unified HMI screen patch operations JSON: {ex.Message}";
                return false;
            }
        }

        private static HmiJsonScreen? DeserializeScreen(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var jsonText = jsonElement.GetString();
                return string.IsNullOrWhiteSpace(jsonText)
                    ? null
                    : JsonSerializer.Deserialize<HmiJsonScreen>(jsonText!, JsonOptions);
            }

            return jsonElement.Deserialize<HmiJsonScreen>(JsonOptions);
        }

        private static IReadOnlyList<HmiScreenJsonPatchOperation>? DeserializePatchOperations(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var jsonText = jsonElement.GetString();
                if (string.IsNullOrWhiteSpace(jsonText))
                {
                    return null;
                }

                using var document = JsonDocument.Parse(jsonText!);
                return DeserializePatchOperations(document.RootElement);
            }

            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                return jsonElement.Deserialize<List<HmiScreenJsonPatchOperation>>(JsonOptions);
            }

            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                if (jsonElement.TryGetProperty("operations", out var operationsElement) ||
                    jsonElement.TryGetProperty("Operations", out operationsElement))
                {
                    return DeserializePatchOperations(operationsElement);
                }

                var singleOperation = jsonElement.Deserialize<HmiScreenJsonPatchOperation>(JsonOptions);
                return singleOperation == null ? null : new[] { singleOperation };
            }

            return null;
        }

        private static bool ValidateOperations(
            string name,
            IReadOnlyList<HmiScreenJsonPatchOperation>? candidate,
            out IReadOnlyList<HmiScreenJsonPatchOperation>? operations,
            out string? error)
        {
            operations = candidate;
            error = null;

            if (candidate == null || candidate.Count == 0)
            {
                operations = null;
                error = $"Parameter {name} must contain at least one Unified HMI screen patch operation.";
                return false;
            }

            return true;
        }
    }
}
