using OpenTiaToolkit.Core.Models;
using System;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonPatchOperationReader
    {
        public string OperationName(HmiScreenJsonPatchOperation operation) =>
            FirstNonEmpty(operation.Op, Extension(operation, "operation")) ?? string.Empty;

        public string? ItemName(HmiScreenJsonPatchOperation operation) =>
            FirstNonEmpty(operation.Item, operation.ItemName, Extension(operation, "item-name"));

        public string? PropertyName(HmiScreenJsonPatchOperation operation) =>
            FirstNonEmpty(operation.Property, Extension(operation, "property-name"));

        public string? InterfacePropertyName(HmiScreenJsonPatchOperation operation) => FirstNonEmpty(
            Extension(operation, "propertyName"), Extension(operation, "interfaceProperty"),
            Extension(operation, "interface-property"), operation.Property, Extension(operation, "property-name"));

        public string? NewName(HmiScreenJsonPatchOperation operation) => FirstNonEmpty(
            operation.NewName, operation.TargetName, Extension(operation, "targetName"),
            Extension(operation, "target-name"), Extension(operation, "new-name"));

        public string? SourceItemName(HmiScreenJsonPatchOperation operation) =>
            FirstNonEmpty(operation.Source, operation.SourceItem, Extension(operation, "source-item"));

        public string? TargetItemName(HmiScreenJsonPatchOperation operation) => FirstNonEmpty(
            operation.Item, operation.ItemName, operation.Target, operation.TargetItem, operation.TargetName,
            Extension(operation, "targetName"), Extension(operation, "item-name"),
            Extension(operation, "target-name"), Extension(operation, "target-item"));

        public string? ItemType(HmiScreenJsonPatchOperation operation) =>
            FirstNonEmpty(operation.ItemType, operation.Type, Extension(operation, "item-type"));

        public string? ContainedType(HmiScreenJsonPatchOperation operation) => FirstNonEmpty(
            operation.ContainedType,
            Extension(operation, "contained-type"),
            Extension(operation, "faceplateType"),
            Extension(operation, "faceplate-type"));

        public string? EventType(HmiScreenJsonPatchOperation operation) => FirstNonEmpty(
            operation.EventType, operation.EventHandler?.EventType, operation.PropertyEventHandler?.EventType,
            Extension(operation, "event-type"));

        public string? DynamizationType(HmiScreenJsonPatchOperation operation) => FirstNonEmpty(
            operation.DynamizationType, operation.Dynamization?.Type,
            Extension(operation, "dynamization-type"), Extension(operation, "dynamic-type"));

        public string? Tag(HmiScreenJsonPatchOperation operation) =>
            FirstNonEmpty(operation.Tag, operation.Dynamization?.Binding?.Tag, Extension(operation, "tag"));

        public static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private static string? Extension(HmiScreenJsonPatchOperation operation, string propertyName)
        {
            if (operation.ExtensionData == null) return null;
            foreach (var entry in operation.ExtensionData)
            {
                if (!entry.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                return entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : entry.Value.ToString();
            }
            return null;
        }
    }
}
