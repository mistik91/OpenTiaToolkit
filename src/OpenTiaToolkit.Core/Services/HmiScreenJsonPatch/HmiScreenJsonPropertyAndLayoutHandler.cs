using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonPropertyAndLayoutHandler : IHmiScreenJsonPatchOperationHandler
    {
        public IReadOnlyList<string> OperationNames { get; } = new[] { "set-property", "set-properties", "move-item", "resize-item", "set-layout" };

        public void Apply(HmiScreenJsonPatchOperationContext context)
        {
            switch (HmiScreenJsonPatchOperationCatalog.Normalize(context.Op))
            {
                case "setproperty": SetProperty(context); break;
                case "setproperties": SetProperties(context); break;
                case "moveitem": Layout(context, true, false); break;
                case "resizeitem": Layout(context, false, true); break;
                case "setlayout": Layout(context, true, true); break;
            }
        }

        private static void SetProperty(HmiScreenJsonPatchOperationContext context)
        {
            var property = context.Reader.PropertyName(context.Operation);
            if (string.IsNullOrWhiteSpace(property)) { context.Failure("The set-property operation requires property."); return; }
            if (!HmiScreenJsonPatchValueCodec.TryConvert(context.Operation.Value, out var value, out var error))
            { context.Failure(error ?? "The set-property operation requires a scalar value."); return; }
            ApplyProperty(context, property!, value);
        }

        private static void SetProperties(HmiScreenJsonPatchOperationContext context)
        {
            var properties = context.Operation.Properties;
            if (properties == null || properties.Count == 0) { context.Failure("The set-properties operation requires a non-empty properties object."); return; }
            foreach (var property in properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(property.Key)) { context.Failure("The set-properties operation contains an empty property name."); continue; }
                if (!HmiScreenJsonPatchValueCodec.TryConvert(property.Value, out var value, out var error))
                { context.Failure(error ?? $"Property '{property.Key}' requires a scalar value.", property: property.Key); continue; }
                ApplyProperty(context, property.Key, value);
            }
        }

        private static void ApplyProperty(HmiScreenJsonPatchOperationContext context, string property, string? value)
        {
            var itemName = context.Reader.ItemName(context.Operation);
            var scope = string.IsNullOrWhiteSpace(itemName) ? "screen" : "item";
            Dictionary<string, string?> properties;
            string? resolvedItemName = null;
            if (string.IsNullOrWhiteSpace(itemName)) properties = context.Screen.Properties;
            else if (context.Targets.TryFindItem(context.Screen, itemName!, out var item, out var suggestions, out var message))
            { properties = item!.Properties; resolvedItemName = item.Name; }
            else { context.Failure(message!, itemName, property, suggestions); return; }
            properties.TryGetValue(property, out var previousValue);
            properties[property] = value;
            context.Success(scope, resolvedItemName, property, previousValue, value);
        }

        private static void Layout(HmiScreenJsonPatchOperationContext context, bool includePosition, bool includeSize)
        {
            var itemName = context.Reader.ItemName(context.Operation);
            if (string.IsNullOrWhiteSpace(itemName)) { context.Failure("The layout operation requires item or itemName."); return; }
            if (!context.Targets.TryFindItem(context.Screen, itemName!, out var item, out var suggestions, out var message))
            { context.Failure(message!, itemName, suggestions: suggestions); return; }
            var changes = new Dictionary<string, int>();
            if (includePosition) { Add(changes, "Left", context.Operation.Left); Add(changes, "Top", context.Operation.Top); }
            if (includeSize) { Add(changes, "Width", context.Operation.Width); Add(changes, "Height", context.Operation.Height); }
            if (changes.Count == 0) { context.Failure("The layout operation did not include any applicable Left, Top, Width, or Height values.", item!.Name); return; }
            foreach (var change in changes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                item!.Properties.TryGetValue(change.Key, out var previousValue);
                var newValue = change.Value.ToString(CultureInfo.InvariantCulture);
                item.Properties[change.Key] = newValue;
                context.Success("item", item.Name, change.Key, previousValue, newValue);
            }
        }

        private static void Add(Dictionary<string, int> changes, string property, int? value) { if (value.HasValue) changes[property] = value.Value; }
    }
}
