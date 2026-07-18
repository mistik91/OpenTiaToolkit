using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonInterfaceHandler : IHmiScreenJsonPatchOperationHandler
    {
        public IReadOnlyList<string> OperationNames { get; } = new[] { "set-interface" };

        public void Apply(HmiScreenJsonPatchOperationContext context)
        {
            if (!context.Targets.TryResolveCompositeTarget(context, out var target)) return;
            if (!string.Equals(target.Scope, "item", StringComparison.OrdinalIgnoreCase))
            { context.Failure("The set-interface operation requires an item because faceplate Interface belongs to a screen item.", context.Reader.ItemName(context.Operation), "Interface"); return; }
            var property = context.Reader.InterfacePropertyName(context.Operation);
            if (string.IsNullOrWhiteSpace(property)) { context.Failure("The set-interface operation requires propertyName or property.", context.Reader.ItemName(context.Operation), "Interface"); return; }
            var hasValue = context.Operation.Value != null;
            var dynamization = BuildDynamization(context.Operation, property!, context.Reader);
            if (!hasValue && dynamization == null) { context.Failure("The set-interface operation requires value and/or a dynamization.", context.Reader.ItemName(context.Operation), "Interface." + property); return; }
            target.Interface ??= new List<HmiJsonInterfaceBinding>();
            var existing = target.Interface.FirstOrDefault(binding => binding.PropertyName.Equals(property!, StringComparison.OrdinalIgnoreCase));
            var previous = existing?.Value; string? value = previous;
            if (hasValue && !HmiScreenJsonPatchValueCodec.TryConvert(context.Operation.Value, out value, out var error))
            { context.Failure(error ?? "The set-interface operation requires a scalar value.", context.Reader.ItemName(context.Operation), "Interface." + property); return; }
            if (existing == null) target.Interface.Add(new HmiJsonInterfaceBinding { PropertyName = property!, Value = value }); else existing.Value = value;
            var binding = target.Interface.First(candidate => candidate.PropertyName.Equals(property!, StringComparison.OrdinalIgnoreCase));
            if (dynamization != null)
            {
                binding.Dynamizations ??= new List<HmiJsonDynamization>();
                binding.Dynamizations.RemoveAll(candidate => candidate.Property.Equals(dynamization.Property, StringComparison.OrdinalIgnoreCase));
                binding.Dynamizations.Add(dynamization);
                binding.Dynamizations = binding.Dynamizations.OrderBy(candidate => candidate.Property, StringComparer.OrdinalIgnoreCase).ToList();
            }
            target.Interface = target.Interface.OrderBy(candidate => candidate.PropertyName, StringComparer.OrdinalIgnoreCase).ToList();
            var resultProperty = dynamization == null ? "Interface." + property : "Interface." + property + ".Dynamizations." + dynamization.Property;
            context.Success(target.Scope, target.ItemName, resultProperty, previous, value);
        }

        private static HmiJsonDynamization? BuildDynamization(HmiScreenJsonPatchOperation operation, string property, HmiScreenJsonPatchOperationReader reader)
        {
            if (operation.Dynamization == null && string.IsNullOrWhiteSpace(operation.DynamizationType) &&
                string.IsNullOrWhiteSpace(operation.DynamicTagName) && string.IsNullOrWhiteSpace(operation.Tag) && string.IsNullOrWhiteSpace(operation.ScriptCode)) return null;
            var clone = HmiScreenJsonPatchCloner.Payload(operation);
            if (clone.Dynamization == null) clone.Property = property;
            if (clone.Dynamization != null && string.IsNullOrWhiteSpace(clone.Dynamization.Property)) clone.Dynamization.Property = property;
            return HmiScreenJsonDynamizationHandler.Build(clone, reader);
        }
    }
}
