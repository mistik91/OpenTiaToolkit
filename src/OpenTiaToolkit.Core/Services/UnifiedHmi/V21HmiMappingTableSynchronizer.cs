using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using static OpenTiaToolkit.Core.Services.V21HmiEngineeringPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal static class V21HmiMappingTableSynchronizer
    {
        internal static MethodInfo ResolveSingleCreateMethod(
            Type compositionType,
            string displayName,
            params Type[] parameterTypes)
        {
            return ResolveSingleCreateMethod(
                compositionType,
                displayName,
                parameterTypes,
                requireGenericMethodDefinition: false);
        }

        internal static MethodInfo ResolveSingleCreateMethod(
            Type compositionType,
            string displayName,
            Type[] parameterTypes,
            bool requireGenericMethodDefinition)
        {
            var matches = compositionType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method =>
                    string.Equals(method.Name, "Create", StringComparison.Ordinal) &&
                    method.IsGenericMethodDefinition == requireGenericMethodDefinition)
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == parameterTypes.Length &&
                        parameters.Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes);
                })
                .OrderBy(method => method.MetadataToken)
                .ToList();

            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"{displayName} is not available on {compositionType.FullName}.");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"{displayName} is ambiguous on {compositionType.FullName}; {matches.Count} matching overloads were found.");
            }

            return matches[0];
        }

        internal static void ApplyDynamizationExport(object targetDynamization, HmiJsonDynamization desired)
        {
            switch (NormalizeDynamizationKind(desired.Type, targetDynamization.GetType().Name))
            {
                case "Tag":
                    if (desired.Binding?.Tag != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "Tag", desired.Binding.Tag);
                    }

                    if (desired.Binding?.ReadOnly.HasValue == true)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "ReadOnly", desired.Binding.ReadOnly.Value);
                    }

                    if (desired.Binding?.UseIndirectAddressing.HasValue == true)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "UseIndirectAddressing", desired.Binding.UseIndirectAddressing.Value);
                    }

                    ApplyValueConverterExport(TryReadPropertyValue(targetDynamization, "ValueConverter"), desired.ValueConverter);
                    break;
                case "Script":
                    ApplyScriptDynamizationExport(targetDynamization, desired.Script);
                    break;
                case "Flashing":
                    ApplyFlashingDynamizationExport(targetDynamization, desired.Flashing);
                    break;
                case "Expression":
                    ApplyValueConverterExport(TryReadPropertyValue(targetDynamization, "ValueConverter"), desired.ValueConverter);
                    break;
                case "ResourceList":
                    if (desired.Binding?.ResourceList != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "ResourceList", desired.Binding.ResourceList);
                    }

                    if (desired.Binding?.Tag != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "Tag", desired.Binding.Tag);
                    }
                    break;
                case "TagParameter":
                    if (desired.Binding?.DynamicTagName != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "DynamicTagName", desired.Binding.DynamicTagName);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported dynamization type '{desired.Type}'.");
            }
        }

        internal static void ApplyScriptDynamizationExport(object targetDynamization, HmiJsonScriptDynamization? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (desired.Async.HasValue)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "Async", desired.Async.Value);
            }

            if (desired.GlobalDefinitionAreaScriptCode != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "GlobalDefinitionAreaScriptCode", desired.GlobalDefinitionAreaScriptCode);
            }

            if (desired.ScriptCode != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "ScriptCode", desired.ScriptCode);
            }

            ApplyScriptTriggerExport(TryReadPropertyValue(targetDynamization, "Trigger"), desired.Trigger);
        }

        internal static void ApplyScriptTriggerExport(object? targetTrigger, HmiJsonScriptTrigger? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (targetTrigger == null)
            {
                throw new InvalidOperationException("Trigger is not available.");
            }

            if (desired.Type != null)
            {
                SetObjectPropertyValueFlexible(targetTrigger, "Type", desired.Type);
            }

            if (desired.CustomDuration != null)
            {
                SetObjectPropertyValueFlexible(targetTrigger, "CustomDuration", desired.CustomDuration);
            }

            if (desired.Tags != null)
            {
                SetObjectPropertyValueRaw(targetTrigger, "Tags", desired.Tags.ToArray());
            }
        }

        internal static void ApplyFlashingDynamizationExport(object targetDynamization, HmiJsonFlashingDynamization? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (desired.Color != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "Color", desired.Color);
            }

            if (desired.AlternateColor != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "AlternateColor", desired.AlternateColor);
            }

            if (desired.FlashingCondition != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "FlashingCondition", desired.FlashingCondition);
            }

            if (desired.FlashingRate != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "FlashingRate", desired.FlashingRate);
            }
        }

        internal static void ApplyValueConverterExport(object? valueConverter, HmiJsonValueConverter? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (valueConverter == null)
            {
                throw new InvalidOperationException("ValueConverter is not available.");
            }

            if (desired.IsFormulaSelected == true)
            {
                SetObjectPropertyValueFlexible(valueConverter, "IsFormulaSelected", true);
            }

            if (desired.Formula != null)
            {
                SetObjectPropertyValueFlexible(valueConverter, "Formula", desired.Formula);
            }

            ApplyMappingTableExport(TryReadPropertyValue(valueConverter, "MappingTable"), desired.MappingTable);

            if (desired.IsFormulaSelected == false)
            {
                SetObjectPropertyValueFlexible(valueConverter, "IsFormulaSelected", false);
            }
        }

        internal static void ApplyMappingTableExport(object? mappingTable, HmiJsonMappingTable? desired)
        {
            if (mappingTable == null)
            {
                if (desired == null)
                {
                    return;
                }

                throw new InvalidOperationException("MappingTable is not available.");
            }

            var entriesComposition = TryReadPropertyValue(mappingTable, "Entries");
            if (entriesComposition == null)
            {
                throw new InvalidOperationException("MappingTable entries are not available.");
            }

            SetObjectPropertyValueFlexible(mappingTable, "ConditionType", desired?.ConditionType ?? "None");
            DeleteEngineeringCompositionEntries(entriesComposition);

            foreach (var entry in desired?.Entries ?? Enumerable.Empty<HmiJsonMappingTableEntry>())
            {
                var targetEntry = CreateMappingTableEntry(entriesComposition, entry);
                ApplyMappingTableEntryExport(targetEntry, entry);
            }
        }

        internal static object CreateMappingTableEntry(object entriesComposition, HmiJsonMappingTableEntry entry)
        {
            var entryKind = NormalizeMappingEntryKind(entry?.EntryType);
            if (string.IsNullOrWhiteSpace(entryKind))
            {
                throw new InvalidOperationException($"Unsupported mapping table entry type '{entry?.EntryType}'.");
            }

            if (string.Equals(entryKind, "Bitmask", StringComparison.OrdinalIgnoreCase))
            {
                var bitmaskCreate = entriesComposition.GetType().GetMethod("Create", new[] { typeof(BitDynamizationType) });
                if (bitmaskCreate != null)
                {
                    var bitTypeName = !string.IsNullOrWhiteSpace(entry?.BitDynamizationType)
                        ? entry!.BitDynamizationType!
                        : BitDynamizationType.SingleBit.ToString();
                    var bitType = Enum.Parse(typeof(BitDynamizationType), bitTypeName, ignoreCase: true);
                    return bitmaskCreate.Invoke(entriesComposition, new[] { bitType })
                        ?? throw new InvalidOperationException($"Failed to create mapping table entry '{entryKind}'.");
                }
            }

            var genericCreate = entriesComposition.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0);
            if (genericCreate == null)
            {
                throw new InvalidOperationException("MappingTable entries Create<T>() is not available.");
            }

            var entryType = ResolveMappingTableEntryClrType(entryKind)
                ?? throw new InvalidOperationException($"Unsupported mapping table entry type '{entryKind}'.");
            return genericCreate.MakeGenericMethod(entryType).Invoke(entriesComposition, null)
                ?? throw new InvalidOperationException($"Failed to create mapping table entry '{entryKind}'.");
        }

        internal static void ApplyMappingTableEntryExport(object targetEntry, HmiJsonMappingTableEntry desired)
        {
            if (desired.Value != null)
            {
                SetObjectPropertyValueFlexible(targetEntry, "Value", desired.Value);
            }

            if (desired.AlternateValue != null)
            {
                SetObjectPropertyValueFlexible(targetEntry, "AlternateValue", desired.AlternateValue);
            }

            if (desired.Flashing.HasValue)
            {
                SetObjectPropertyValueFlexible(targetEntry, "Flashing", desired.Flashing.Value);
            }

            if (desired.FlashingRate != null)
            {
                SetObjectPropertyValueFlexible(targetEntry, "FlashingRate", desired.FlashingRate);
            }

            switch (NormalizeMappingEntryKind(desired.EntryType))
            {
                case "Simple":
                    if (desired.Condition != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "Condition", desired.Condition);
                    }
                    break;
                case "Range":
                    if (desired.From != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "From", desired.From);
                    }

                    if (desired.To != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "To", desired.To);
                    }

                    if (desired.RangeType != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "RangeType", desired.RangeType);
                    }
                    break;
                case "Bitmask":
                    if (desired.BitDynamizationType != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "BitDynamizationType", desired.BitDynamizationType);
                    }

                    if (desired.Condition != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "Condition", desired.Condition);
                    }

                    if (desired.Relevant != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "Relevant", desired.Relevant);
                    }
                    break;
            }
        }

        internal static void DeleteEngineeringCompositionEntries(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                return;
            }

            foreach (var entry in enumerable.Cast<object>().ToList())
            {
                DeleteEngineeringObject(entry);
            }
        }

        internal static bool HasMeaningfulMappingTable(HmiJsonMappingTable? mappingTable)
        {
            return mappingTable != null &&
                   ((!string.IsNullOrWhiteSpace(mappingTable.ConditionType) &&
                     !mappingTable.ConditionType!.Equals("None", StringComparison.OrdinalIgnoreCase)) ||
                    (mappingTable.Entries?.Count ?? 0) > 0);
        }

        internal static string NormalizeMappingEntryKind(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return string.Empty;
            }

            var trimmed = typeName!.Trim();
            if (trimmed.StartsWith("MappingTableEntry", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("MappingTableEntry".Length);
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "simple":
                    return "Simple";
                case "range":
                    return "Range";
                case "bitmask":
                    return "Bitmask";
                default:
                    return string.Empty;
            }
        }

        internal static Type? ResolveMappingTableEntryClrType(string? kind)
        {
            switch (NormalizeMappingEntryKind(kind))
            {
                case "Simple":
                    return typeof(MappingTableEntrySimple);
                case "Range":
                    return typeof(MappingTableEntryRange);
                case "Bitmask":
                    return typeof(MappingTableEntryBitmask);
                default:
                    return null;
            }
        }

        internal static bool HasMeaningfulBinding(HmiJsonDynamizationBinding? binding)
        {
            return binding != null &&
                   (!string.IsNullOrWhiteSpace(binding.Tag) ||
                    binding.ReadOnly.HasValue ||
                    binding.UseIndirectAddressing.HasValue ||
                    !string.IsNullOrWhiteSpace(binding.ResourceList) ||
                    !string.IsNullOrWhiteSpace(binding.DynamicTagName));
        }

        internal static List<string>? ReadStringListValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string stringValue)
            {
                var normalized = NormalizeOptionalString(stringValue);
                return normalized == null ? null : new List<string> { normalized };
            }

            if (value is not IEnumerable enumerable)
            {
                return null;
            }

            var result = enumerable
                .Cast<object>()
                .Select(entry => NormalizeOptionalString(Convert.ToString(entry, CultureInfo.InvariantCulture)))
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Cast<string>()
                .ToList();
            return result.Count > 0 ? result : null;
        }

        internal static string? TryReadStringProperty(object? source, string propertyName)
        {
            return NormalizeOptionalString(FormatValue(TryReadPropertyValue(source, propertyName)));
        }

        internal static bool? TryReadNullableBoolProperty(object? source, string propertyName)
        {
            var value = TryReadPropertyValue(source, propertyName);
            if (value == null)
            {
                return null;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : null;
        }

        internal static string? NormalizeOptionalString(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value?.Trim();
        }

        internal static string NormalizeDynamizationKind(params string?[] candidates)
        {
            foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                var trimmed = candidate!.Trim();
                if (trimmed.EndsWith("Dynamization", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - "Dynamization".Length);
                }

                switch (trimmed.ToLowerInvariant())
                {
                    case "tag":
                        return "Tag";
                    case "script":
                        return "Script";
                    case "flashing":
                        return "Flashing";
                    case "expression":
                        return "Expression";
                    case "resourcelist":
                        return "ResourceList";
                    case "tagparameter":
                        return "TagParameter";
                }
            }

            return string.Empty;
        }

    }
}
