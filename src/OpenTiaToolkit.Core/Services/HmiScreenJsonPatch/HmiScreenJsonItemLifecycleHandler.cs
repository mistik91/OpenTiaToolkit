using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonItemLifecycleHandler : IHmiScreenJsonPatchOperationHandler
    {
        public IReadOnlyList<string> OperationNames { get; } = new[] { "rename-item", "create-item", "delete-item", "duplicate-item" };

        public void Apply(HmiScreenJsonPatchOperationContext context)
        {
            switch (HmiScreenJsonPatchOperationCatalog.Normalize(context.Op))
            {
                case "renameitem": Rename(context); break;
                case "createitem": Create(context); break;
                case "deleteitem": Delete(context); break;
                case "duplicateitem": Duplicate(context); break;
            }
        }

        private static void Delete(HmiScreenJsonPatchOperationContext context)
        {
            var itemName = context.Reader.ItemName(context.Operation);
            if (string.IsNullOrWhiteSpace(itemName)) { context.Failure("The delete-item operation requires item or itemName."); return; }
            if (!context.Targets.TryFindItem(context.Screen, itemName!, out var item, out var suggestions, out var message))
            { context.Failure(message!, itemName, suggestions: suggestions); return; }
            context.Screen.Items.Remove(item!);
            context.Success("item", item!.Name, null, item.Type, null);
        }

        private static void Rename(HmiScreenJsonPatchOperationContext context)
        {
            var itemName = HmiScreenJsonPatchOperationReader.FirstNonEmpty(context.Reader.ItemName(context.Operation), context.Reader.SourceItemName(context.Operation));
            if (string.IsNullOrWhiteSpace(itemName)) { context.Failure("The rename-item operation requires item, itemName, source, or sourceItem."); return; }
            var newName = context.Reader.NewName(context.Operation);
            if (string.IsNullOrWhiteSpace(newName)) { context.Failure("The rename-item operation requires newName.", itemName); return; }
            if (!context.Targets.TryFindItem(context.Screen, itemName!, out var item, out var suggestions, out var message))
            { context.Failure(message!, itemName, suggestions: suggestions); return; }
            if (context.Screen.Items.Any(candidate => !ReferenceEquals(candidate, item) && candidate.Name.Equals(newName!, StringComparison.OrdinalIgnoreCase)))
            { context.Failure($"Cannot rename item '{item!.Name}' to '{newName}' because another item already has that name.", item.Name); return; }
            var previousName = item!.Name; item.Name = newName!;
            context.Success("item", previousName, "Name", previousName, newName);
        }

        private static void Create(HmiScreenJsonPatchOperationContext context)
        {
            var itemName = context.Reader.ItemName(context.Operation);
            if (string.IsNullOrWhiteSpace(itemName)) { context.Failure("The create-item operation requires item or itemName."); return; }
            var itemType = context.Reader.ItemType(context.Operation);
            if (string.IsNullOrWhiteSpace(itemType)) { context.Failure("The create-item operation requires itemType, item-type, or type.", itemName); return; }
            if (context.Screen.Items.Any(item => item.Name.Equals(itemName!, StringComparison.OrdinalIgnoreCase)))
            { context.Failure($"Item '{itemName}' already exists on the exported screen.", itemName); return; }
            var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (!TryApplyOverrides(context, properties, itemName)) return;
            if (!TryApplyContainedType(context, properties, itemName)) return;
            AddLayout(properties, "Left", context.Operation.Left); AddLayout(properties, "Top", context.Operation.Top);
            AddLayout(properties, "Width", context.Operation.Width); AddLayout(properties, "Height", context.Operation.Height);
            if (context.Results.Any(result => result.Index == context.Index && !result.Succeeded)) return;
            context.Screen.Items.Add(new HmiJsonItem { Name = itemName!, Type = itemType!, Properties = properties });
            context.Success("item", itemName, null, null, itemType);
            foreach (var property in properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                context.Success("item", itemName, property.Key, null, property.Value);
        }

        private static void Duplicate(HmiScreenJsonPatchOperationContext context)
        {
            var sourceName = context.Reader.SourceItemName(context.Operation);
            if (string.IsNullOrWhiteSpace(sourceName)) { context.Failure("The duplicate-item operation requires source or sourceItem."); return; }
            var targetName = context.Reader.TargetItemName(context.Operation);
            if (string.IsNullOrWhiteSpace(targetName)) { context.Failure("The duplicate-item operation requires item, itemName, target, targetItem, or targetName."); return; }
            if (!context.Targets.TryFindItem(context.Screen, sourceName!, out var source, out var suggestions, out var message))
            { context.Failure(message!, sourceName, suggestions: suggestions); return; }
            if (context.Screen.Items.Any(item => item.Name.Equals(targetName!, StringComparison.OrdinalIgnoreCase)))
            { context.Failure($"Item '{targetName}' already exists on the exported screen.", targetName); return; }
            var duplicate = HmiScreenJsonPatchCloner.Item(source!); duplicate.Name = targetName!;
            if (!TryApplyOverrides(context, duplicate.Properties, targetName)) return;
            if (!TryApplyContainedType(context, duplicate.Properties, targetName)) return;
            AddLayout(duplicate.Properties, "Left", context.Operation.Left); AddLayout(duplicate.Properties, "Top", context.Operation.Top);
            AddLayout(duplicate.Properties, "Width", context.Operation.Width); AddLayout(duplicate.Properties, "Height", context.Operation.Height);
            context.Screen.Items.Add(duplicate);
            context.Success("item", targetName, null, source!.Name, duplicate.Type);
            foreach (var property in OverrideProperties(context, duplicate).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                source.Properties.TryGetValue(property, out var previous); duplicate.Properties.TryGetValue(property, out var current);
                context.Success("item", targetName, property, previous, current);
            }
        }

        private static bool TryApplyOverrides(HmiScreenJsonPatchOperationContext context, Dictionary<string, string?> properties, string? itemName)
        {
            if (context.Operation.Properties == null) return true;
            var failures = context.Results.Count(result => result.Index == context.Index && !result.Succeeded);
            foreach (var property in context.Operation.Properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(property.Key)) { context.Failure("The operation contains an empty property name.", itemName); continue; }
                if (!HmiScreenJsonPatchValueCodec.TryConvert(property.Value, out var value, out var error))
                { context.Failure(error ?? $"Property '{property.Key}' requires a scalar value.", itemName, property.Key); continue; }
                properties[property.Key] = value;
            }
            return context.Results.Count(result => result.Index == context.Index && !result.Succeeded) == failures;
        }

        private static bool TryApplyContainedType(
            HmiScreenJsonPatchOperationContext context,
            Dictionary<string, string?> properties,
            string? itemName)
        {
            var containedType = context.Reader.ContainedType(context.Operation);
            if (string.IsNullOrWhiteSpace(containedType))
            {
                return true;
            }

            if (properties.TryGetValue("ContainedType", out var existing) &&
                !string.IsNullOrWhiteSpace(existing) &&
                !string.Equals(existing, containedType, StringComparison.Ordinal))
            {
                context.Failure(
                    "The containedType/contained-type/faceplate-type value must match properties.ContainedType when both are provided.",
                    itemName,
                    "ContainedType");
                return false;
            }

            properties["ContainedType"] = containedType;
            return true;
        }

        private static void AddLayout(Dictionary<string, string?> properties, string property, int? value)
        { if (value.HasValue) properties[property] = value.Value.ToString(CultureInfo.InvariantCulture); }

        private static IEnumerable<string> OverrideProperties(HmiScreenJsonPatchOperationContext context, HmiJsonItem duplicate)
        {
            var operation = context.Operation;
            if (operation.Properties != null) foreach (var property in operation.Properties.Keys.Where(value => !string.IsNullOrWhiteSpace(value))) yield return property;
            if (!string.IsNullOrWhiteSpace(context.Reader.ContainedType(operation))) yield return "ContainedType";
            foreach (var property in new[] { "Left", "Top", "Width", "Height" })
            {
                var supplied = property == "Left" ? operation.Left.HasValue : property == "Top" ? operation.Top.HasValue : property == "Width" ? operation.Width.HasValue : operation.Height.HasValue;
                if (supplied || duplicate.Properties.ContainsKey(property)) yield return property;
            }
        }
    }
}
