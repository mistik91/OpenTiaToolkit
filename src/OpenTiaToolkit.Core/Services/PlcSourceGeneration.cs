using OpenTiaToolkit.Core.Models;
using System;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services;

internal static class PlcSourceGeneration
{
    internal static void ValidateDestinations(string group, string typeGroup)
    {
        if (group.Length > 0 && typeGroup.Length > 0)
            throw new InvalidOperationException("Siemens source generation accepts one subgroup destination per source. Split TYPE and block declarations into separate sources in the same transaction to target both subgroups.");
    }

    internal static PlcSourceOperationResult Generate(EngineeringSessionContext context, object? source, string sourceName, string? groupPath, string? typeGroupPath, bool dryRun, bool confirm)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return Failure("source-name is required.");
        if (source == null) return Failure($"External source '{sourceName}' was not found.");
        try
        {
            var group = PlcWorkflowPrimitives.NormalizeGroupPath(groupPath, context.CurrentPlcName ?? string.Empty);
            var typeGroup = PlcWorkflowPrimitives.NormalizeTypeGroupPath(typeGroupPath, context.CurrentPlcName ?? string.Empty);
            ValidateDestinations(group, typeGroup);
            object? destination = null;
            if (group.Length > 0) destination = TiaReflectionHelper.GetBlockGroupByPath(TiaReflectionHelper.GetBlockGroup(context.PlcSoftware)!, group);
            if (typeGroup.Length > 0) destination = TiaReflectionHelper.GetTypeGroupByPath(TiaReflectionHelper.GetTypeGroup(context.PlcSoftware)!, typeGroup);
            bool targeted = group.Length > 0 || typeGroup.Length > 0;
            if (targeted && destination == null) return Failure($"PLC {(group.Length > 0 ? "block" : "type")} group was not found: '{(group.Length > 0 ? group : typeGroup)}'.");
            var methods = source.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? method;
            object[] arguments;
            if (targeted)
            {
                method = methods.FirstOrDefault(candidate => candidate.Name == "GenerateBlocksFromSource" &&
                    candidate.GetParameters() is var parameters && parameters.Length == 2 &&
                    parameters[0].ParameterType.IsInstanceOfType(destination) && parameters[1].ParameterType.IsEnum && Enum.IsDefined(parameters[1].ParameterType, "None"));
                if (method == null) return Failure("External source does not expose the requested group-targeted GenerateBlocksFromSource API; root generation was not attempted.");
                arguments = new[] { destination!, Enum.Parse(method.GetParameters()[1].ParameterType, "None") };
            }
            else
            {
                method = new[] { "GenerateBlocksFromSource", "GenerateBlocks", "Generate" }
                    .Select(name => methods.FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == 0)).FirstOrDefault(candidate => candidate != null);
                if (method == null) return Failure("External source does not expose a supported block generation method.");
                arguments = Array.Empty<object>();
            }
            if (dryRun) return new PlcSourceOperationResult(true, false, $"Dry-run: source '{sourceName}' supports generation at the requested destination.", sourceName);
            if (!confirm) return Failure("Generating blocks mutates the PLC project. Rerun with confirm=true.");
            method.Invoke(source, arguments);
            return new PlcSourceOperationResult(true, true, $"Generated blocks from source '{sourceName}'.", sourceName);
        }
        catch (Exception ex) { return Failure(ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message); }

        PlcSourceOperationResult Failure(string message) => new(false, false, message, sourceName);
    }
}
