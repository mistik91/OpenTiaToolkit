using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiScreenJsonPatchCloner
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static HmiJsonScreen Screen(HmiJsonScreen source) => Clone(source, "Unable to clone Unified HMI screen JSON.");
        public static HmiJsonItem Item(HmiJsonItem source) => Clone(source, "Unable to clone Unified HMI item JSON.");
        public static T Payload<T>(T source) => Clone(source, "Unable to clone Unified HMI JSON payload.");

        private static T Clone<T>(T source, string message) =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, Options), Options) ?? throw new InvalidOperationException(message);
    }

    internal static class HmiScreenJsonPatchValueCodec
    {
        public static bool TryConvert(object? value, out string? converted, out string? error)
        {
            converted = null; error = null;
            if (value == null) { error = "The operation requires a non-null scalar value."; return false; }
            if (value is JsonElement element) return TryConvert(element, out converted, out error);
            if (value is string text) { converted = text; return true; }
            if (value is bool boolean) { converted = boolean.ToString(); return true; }
            if (value is IFormattable formattable) { converted = formattable.ToString(null, CultureInfo.InvariantCulture); return true; }
            converted = value.ToString();
            if (converted != null) return true;
            error = "The operation value could not be converted to text."; return false;
        }

        private static bool TryConvert(JsonElement value, out string? converted, out string? error)
        {
            converted = null; error = null;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: converted = value.GetString() ?? string.Empty; return true;
                case JsonValueKind.Number: converted = value.GetRawText(); return true;
                case JsonValueKind.True: converted = bool.TrueString; return true;
                case JsonValueKind.False: converted = bool.FalseString; return true;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: error = "The operation requires a non-null scalar value."; return false;
                default: error = "The operation value must be a scalar string, number, or boolean."; return false;
            }
        }
    }

    internal static class HmiScreenJsonPatchResultFactory
    {
        public static HmiScreenJsonPatchOperationResult Success(int index, string op, string scope, string? itemName,
            string? property, string? previousValue, string? newValue) => new HmiScreenJsonPatchOperationResult
            {
                Index = index,
                Op = op,
                Succeeded = true,
                Scope = scope,
                ItemName = itemName,
                Property = property,
                PreviousValue = previousValue,
                NewValue = newValue
            };

        public static HmiScreenJsonPatchOperationResult Failure(int index, string op, string message,
            string? itemName = null, string? property = null, List<string>? suggestions = null) => new HmiScreenJsonPatchOperationResult
            {
                Index = index,
                Op = op,
                Succeeded = false,
                Scope = string.IsNullOrWhiteSpace(itemName) ? string.Empty : "item",
                ItemName = itemName,
                Property = property,
                Message = message,
                Suggestions = suggestions ?? new List<string>()
            };
    }

    internal sealed class HmiScreenJsonCompositeTarget
    {
        private readonly HmiJsonScreen? _screen;
        private readonly HmiJsonItem? _item;
        private HmiScreenJsonCompositeTarget(HmiJsonScreen screen) { _screen = screen; Scope = "screen"; }
        private HmiScreenJsonCompositeTarget(HmiJsonItem item) { _item = item; Scope = "item"; ItemName = item.Name; }
        public string Scope { get; }
        public string? ItemName { get; }
        public List<HmiJsonEventHandler>? EventHandlers { get => _item != null ? _item.EventHandlers : _screen!.EventHandlers; set { if (_item != null) _item.EventHandlers = value; else _screen!.EventHandlers = value; } }
        public List<HmiJsonPropertyEventHandler>? PropertyEventHandlers { get => _item != null ? _item.PropertyEventHandlers : _screen!.PropertyEventHandlers; set { if (_item != null) _item.PropertyEventHandlers = value; else _screen!.PropertyEventHandlers = value; } }
        public List<HmiJsonDynamization>? Dynamizations { get => _item != null ? _item.Dynamizations : _screen!.Dynamizations; set { if (_item != null) _item.Dynamizations = value; else _screen!.Dynamizations = value; } }
        public List<HmiJsonInterfaceBinding>? Interface { get => _item?.Interface; set { if (_item != null) _item.Interface = value; } }
        public static HmiScreenJsonCompositeTarget ForScreen(HmiJsonScreen screen) => new HmiScreenJsonCompositeTarget(screen);
        public static HmiScreenJsonCompositeTarget ForItem(HmiJsonItem item) => new HmiScreenJsonCompositeTarget(item);
    }

    internal sealed class HmiScreenJsonPatchTargetResolver
    {
        private readonly HmiScreenJsonPatchOperationReader _reader;
        public HmiScreenJsonPatchTargetResolver(HmiScreenJsonPatchOperationReader reader) { _reader = reader; }

        public bool TryFindItem(HmiJsonScreen screen, string itemName, out HmiJsonItem? item, out List<string> suggestions, out string? message)
        {
            item = null; suggestions = new List<string>(); message = null;
            var matches = screen.Items.Where(candidate => candidate.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) { item = matches[0]; return true; }
            if (matches.Count > 1)
            {
                suggestions = matches.Select(candidate => candidate.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                message = $"Item name '{itemName}' is ambiguous on the exported screen."; return false;
            }
            suggestions = HmiPatchSuggestionService.SuggestItemNames(screen, itemName);
            message = $"Item '{itemName}' was not found on the exported screen."; return false;
        }

        public bool TryResolveCompositeTarget(HmiScreenJsonPatchOperationContext context, out HmiScreenJsonCompositeTarget target)
        {
            target = null!;
            var itemName = _reader.ItemName(context.Operation);
            if (string.IsNullOrWhiteSpace(itemName)) { target = HmiScreenJsonCompositeTarget.ForScreen(context.Screen); return true; }
            if (!TryFindItem(context.Screen, itemName!, out var item, out var suggestions, out var message))
            {
                context.Results.Add(HmiScreenJsonPatchResultFactory.Failure(context.Index, context.Op, message!, itemName, suggestions: suggestions));
                return false;
            }
            target = HmiScreenJsonCompositeTarget.ForItem(item!); return true;
        }
    }
}
