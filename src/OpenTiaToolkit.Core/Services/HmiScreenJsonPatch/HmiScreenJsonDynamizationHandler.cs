using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonDynamizationHandler : IHmiScreenJsonPatchOperationHandler
    {
        public IReadOnlyList<string> OperationNames { get; } = new[] { "set-dynamization", "remove-dynamization" };

        public void Apply(HmiScreenJsonPatchOperationContext context)
        {
            if (!context.Targets.TryResolveCompositeTarget(context, out var target)) return;
            if (HmiScreenJsonPatchOperationCatalog.Normalize(context.Op) == "setdynamization") Set(context, target); else Remove(context, target);
        }

        private static void Set(HmiScreenJsonPatchOperationContext context, HmiScreenJsonCompositeTarget target)
        {
            var dynamization = Build(context.Operation, context.Reader);
            if (dynamization == null || string.IsNullOrWhiteSpace(dynamization.Property) || string.IsNullOrWhiteSpace(dynamization.Type))
            { context.Failure("The set-dynamization operation requires dynamization or property plus dynamizationType/tag.", context.Reader.ItemName(context.Operation), "Dynamizations"); return; }
            target.Dynamizations ??= new List<HmiJsonDynamization>();
            target.Dynamizations.RemoveAll(existing => existing.Property.Equals(dynamization.Property, StringComparison.OrdinalIgnoreCase));
            target.Dynamizations.Add(dynamization);
            target.Dynamizations = target.Dynamizations.OrderBy(existing => existing.Property, StringComparer.OrdinalIgnoreCase).ToList();
            context.Success(target.Scope, target.ItemName, "Dynamizations." + dynamization.Property, null, dynamization.Type);
        }

        private static void Remove(HmiScreenJsonPatchOperationContext context, HmiScreenJsonCompositeTarget target)
        {
            var property = context.Reader.PropertyName(context.Operation);
            if (string.IsNullOrWhiteSpace(property)) { context.Failure("The remove-dynamization operation requires property.", context.Reader.ItemName(context.Operation), "Dynamizations"); return; }
            var values = target.Dynamizations ?? new List<HmiJsonDynamization>();
            if (values.RemoveAll(value => value.Property.Equals(property!, StringComparison.OrdinalIgnoreCase)) == 0)
            { context.Failure($"Dynamization for property '{property}' was not found.", context.Reader.ItemName(context.Operation), "Dynamizations." + property); return; }
            target.Dynamizations = values;
            context.Success(target.Scope, target.ItemName, "Dynamizations." + property, property, null);
        }

        internal static HmiJsonDynamization? Build(HmiScreenJsonPatchOperation operation, HmiScreenJsonPatchOperationReader reader)
        {
            if (operation.Dynamization != null) return HmiScreenJsonPatchCloner.Payload(operation.Dynamization);
            var property = reader.PropertyName(operation); var type = reader.DynamizationType(operation); var tag = reader.Tag(operation);
            if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(tag)) type = "Tag";
            if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(operation.DynamicTagName)) type = "TagParameter";
            if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(operation.ScriptCode)) type = "Script";
            if (string.IsNullOrWhiteSpace(property) || string.IsNullOrWhiteSpace(type)) return null;
            var result = new HmiJsonDynamization { Property = property!, Type = type! };
            if (type!.Equals("Tag", StringComparison.OrdinalIgnoreCase))
            { if (string.IsNullOrWhiteSpace(tag)) return null; result.Binding = new HmiJsonDynamizationBinding { Tag = tag }; }
            else if (type.Equals("TagParameter", StringComparison.OrdinalIgnoreCase))
            { if (string.IsNullOrWhiteSpace(operation.DynamicTagName)) return null; result.Binding = new HmiJsonDynamizationBinding { DynamicTagName = operation.DynamicTagName }; }
            else if (type.Equals("Script", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(operation.ScriptCode)) return null;
                result.Script = new HmiJsonScriptDynamization { Async = operation.Async, GlobalDefinitionAreaScriptCode = operation.GlobalDefinitionAreaScriptCode, ScriptCode = operation.ScriptCode };
            }
            return result;
        }
    }
}
