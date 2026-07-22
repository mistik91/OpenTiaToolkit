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
using static OpenTiaToolkit.Core.Services.V21HmiMappingTableSynchronizer;

namespace OpenTiaToolkit.Core.Services
{
    internal static class V21HmiDynamizationSynchronizer
    {
        internal static List<HmiJsonDynamization> ReadSupportedDynamizationsFromComposition(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("Dynamizations composition is not enumerable.");
            }

            return enumerable
                .Cast<object>()
                .Select(TryReadDynamizationExport)
                .Where(dynamization => dynamization != null)
                .OrderBy(dynamization => dynamization!.Property, StringComparer.OrdinalIgnoreCase)
                .Cast<HmiJsonDynamization>()
                .ToList();
        }

        internal static HmiJsonDynamization? TryReadDynamizationExport(object? dynamization)
        {
            var propertyName = NormalizeOptionalString(TryReadStringProperty(dynamization, "PropertyName"));
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var kind = NormalizeDynamizationKind(
                TryReadStringProperty(dynamization, "DynamizationType"),
                dynamization?.GetType().Name);
            if (ResolveDynamizationClrType(kind) == null)
            {
                return null;
            }

            var export = new HmiJsonDynamization
            {
                Property = propertyName!,
                Type = kind
            };

            switch (kind)
            {
                case "Tag":
                    export.Binding = CreateBinding(
                        tag: TryReadStringProperty(dynamization, "Tag"),
                        readOnly: TryReadNullableBoolProperty(dynamization, "ReadOnly"),
                        useIndirectAddressing: TryReadNullableBoolProperty(dynamization, "UseIndirectAddressing"),
                        resourceList: null,
                        dynamicTagName: null);
                    export.ValueConverter = ReadValueConverterExport(TryReadPropertyValue(dynamization, "ValueConverter"));
                    break;
                case "Script":
                    export.Script = ReadScriptDynamizationExport(dynamization);
                    break;
                case "Flashing":
                    export.Flashing = ReadFlashingDynamizationExport(dynamization);
                    break;
                case "Expression":
                    export.ValueConverter = ReadValueConverterExport(TryReadPropertyValue(dynamization, "ValueConverter"));
                    break;
                case "ResourceList":
                    export.Binding = CreateBinding(
                        tag: TryReadStringProperty(dynamization, "Tag"),
                        readOnly: null,
                        useIndirectAddressing: null,
                        resourceList: TryReadStringProperty(dynamization, "ResourceList"),
                        dynamicTagName: null);
                    break;
                case "TagParameter":
                    export.Binding = CreateBinding(
                        tag: null,
                        readOnly: null,
                        useIndirectAddressing: null,
                        resourceList: null,
                        dynamicTagName: TryReadStringProperty(dynamization, "DynamicTagName"));
                    break;
            }

            return export;
        }

        internal static HmiJsonDynamizationBinding? CreateBinding(
            string? tag,
            bool? readOnly,
            bool? useIndirectAddressing,
            string? resourceList,
            string? dynamicTagName)
        {
            var binding = new HmiJsonDynamizationBinding
            {
                Tag = NormalizeOptionalString(tag),
                ReadOnly = readOnly,
                UseIndirectAddressing = useIndirectAddressing,
                ResourceList = NormalizeOptionalString(resourceList),
                DynamicTagName = NormalizeOptionalString(dynamicTagName)
            };

            return HasMeaningfulBinding(binding) ? binding : null;
        }

        internal static HmiJsonScriptDynamization? ReadScriptDynamizationExport(object? dynamization)
        {
            if (dynamization == null)
            {
                return null;
            }

            var export = new HmiJsonScriptDynamization
            {
                Async = TryReadNullableBoolProperty(dynamization, "Async"),
                GlobalDefinitionAreaScriptCode = TryReadStringProperty(dynamization, "GlobalDefinitionAreaScriptCode"),
                ScriptCode = TryReadStringProperty(dynamization, "ScriptCode"),
                Trigger = ReadScriptTriggerExport(TryReadPropertyValue(dynamization, "Trigger"))
            };

            return export.Async.HasValue ||
                   !string.IsNullOrWhiteSpace(export.GlobalDefinitionAreaScriptCode) ||
                   !string.IsNullOrWhiteSpace(export.ScriptCode) ||
                   export.Trigger != null
                ? export
                : null;
        }

        internal static HmiJsonScriptTrigger? ReadScriptTriggerExport(object? trigger)
        {
            if (trigger == null)
            {
                return null;
            }

            var export = new HmiJsonScriptTrigger
            {
                Type = TryReadStringProperty(trigger, "Type"),
                CustomDuration = TryReadStringProperty(trigger, "CustomDuration"),
                Tags = ReadStringListValue(TryReadPropertyValue(trigger, "Tags"))
            };

            return !string.IsNullOrWhiteSpace(export.Type) ||
                   !string.IsNullOrWhiteSpace(export.CustomDuration) ||
                   (export.Tags?.Count ?? 0) > 0
                ? export
                : null;
        }

        internal static HmiJsonFlashingDynamization? ReadFlashingDynamizationExport(object? dynamization)
        {
            if (dynamization == null)
            {
                return null;
            }

            var export = new HmiJsonFlashingDynamization
            {
                Color = TryReadStringProperty(dynamization, "Color"),
                AlternateColor = TryReadStringProperty(dynamization, "AlternateColor"),
                FlashingCondition = TryReadStringProperty(dynamization, "FlashingCondition"),
                FlashingRate = TryReadStringProperty(dynamization, "FlashingRate")
            };

            return !string.IsNullOrWhiteSpace(export.Color) ||
                   !string.IsNullOrWhiteSpace(export.AlternateColor) ||
                   !string.IsNullOrWhiteSpace(export.FlashingCondition) ||
                   !string.IsNullOrWhiteSpace(export.FlashingRate)
                ? export
                : null;
        }

        internal static HmiJsonValueConverter? ReadValueConverterExport(object? valueConverter)
        {
            if (valueConverter == null)
            {
                return null;
            }

            var export = new HmiJsonValueConverter
            {
                IsFormulaSelected = TryReadNullableBoolProperty(valueConverter, "IsFormulaSelected"),
                Formula = TryReadStringProperty(valueConverter, "Formula"),
                MappingTable = ReadMappingTableExport(TryReadPropertyValue(valueConverter, "MappingTable"))
            };

            return export.IsFormulaSelected.HasValue ||
                   !string.IsNullOrWhiteSpace(export.Formula) ||
                   HasMeaningfulMappingTable(export.MappingTable)
                ? export
                : null;
        }

        internal static HmiJsonMappingTable? ReadMappingTableExport(object? mappingTable)
        {
            if (mappingTable == null)
            {
                return null;
            }

            var entries = ReadMappingTableEntries(TryReadPropertyValue(mappingTable, "Entries"));
            var export = new HmiJsonMappingTable
            {
                ConditionType = TryReadStringProperty(mappingTable, "ConditionType"),
                Entries = entries.Count > 0 ? entries : null
            };

            return HasMeaningfulMappingTable(export) ? export : null;
        }

        internal static List<HmiJsonMappingTableEntry> ReadMappingTableEntries(object? entriesComposition)
        {
            var entries = new List<HmiJsonMappingTableEntry>();
            if (entriesComposition == null)
            {
                return entries;
            }

            if (entriesComposition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("MappingTable entries composition is not enumerable.");
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var entryType = NormalizeMappingEntryKind(entry.GetType().Name);
                if (string.IsNullOrWhiteSpace(entryType))
                {
                    continue;
                }

                var export = new HmiJsonMappingTableEntry
                {
                    EntryType = entryType,
                    Value = TryReadStringProperty(entry, "Value"),
                    AlternateValue = TryReadStringProperty(entry, "AlternateValue"),
                    Flashing = TryReadNullableBoolProperty(entry, "Flashing"),
                    FlashingRate = TryReadStringProperty(entry, "FlashingRate")
                };

                switch (entryType)
                {
                    case "Simple":
                        export.Condition = TryReadStringProperty(entry, "Condition");
                        break;
                    case "Range":
                        export.From = TryReadStringProperty(entry, "From");
                        export.To = TryReadStringProperty(entry, "To");
                        export.RangeType = TryReadStringProperty(entry, "RangeType");
                        break;
                    case "Bitmask":
                        export.BitDynamizationType = TryReadStringProperty(entry, "BitDynamizationType");
                        export.Condition = TryReadStringProperty(entry, "Condition");
                        export.Relevant = TryReadStringProperty(entry, "Relevant");
                        break;
                }

                entries.Add(export);
            }

            return entries;
        }

        internal static bool TryValidateDynamizationDefinitions(
            IEnumerable<HmiJsonDynamization> dynamizations,
            out string? error)
        {
            error = null;
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dynamization in dynamizations ?? Enumerable.Empty<HmiJsonDynamization>())
            {
                if (dynamization == null)
                {
                    continue;
                }

                var currentDynamization = dynamization;

                var propertyName = NormalizeOptionalString(currentDynamization.Property);
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    error = "Dynamizations entries require a Property value.";
                    return false;
                }

                if (!seenProperties.Add(propertyName!))
                {
                    error = $"Dynamizations contains a duplicate property '{propertyName}'.";
                    return false;
                }

                if (ResolveDynamizationClrType(currentDynamization.Type) == null)
                {
                    error = $"Unsupported dynamization type '{currentDynamization.Type}' for property '{propertyName}'.";
                    return false;
                }
            }

            return true;
        }

        internal static bool TryValidateInterfaceBindingDefinitions(
            IEnumerable<HmiJsonInterfaceBinding> bindings,
            out string? error)
        {
            error = null;
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var binding in bindings ?? Enumerable.Empty<HmiJsonInterfaceBinding>())
            {
                var propertyName = NormalizeOptionalString(binding?.PropertyName);
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    error = "Interface entries require a PropertyName value.";
                    return false;
                }

                if (!seenProperties.Add(propertyName!))
                {
                    error = $"Interface contains a duplicate property '{propertyName}'.";
                    return false;
                }

                if (!TryValidateDynamizationDefinitions(binding?.Dynamizations ?? new List<HmiJsonDynamization>(), out error))
                {
                    error = $"Interface.{propertyName}.Dynamizations: {error}";
                    return false;
                }
            }

            return true;
        }

        internal static bool TryValidateDynamizationTarget(
            object? owner,
            Type ownerType,
            HmiJsonDynamization dynamization,
            out string? error)
        {
            error = null;

            if (ResolvePublicInstanceProperty(ownerType, "Dynamizations") == null)
            {
                error = $"Dynamizations is not available on type '{ownerType.Name}'.";
                return false;
            }

            if (owner == null)
            {
                return true;
            }

            if (ResolveProperty(owner, dynamization.Property) != null || ResolveAttribute(owner, dynamization.Property) != null)
            {
                return true;
            }

            error = $"Property or attribute '{dynamization.Property}' was not found on type '{owner.GetType().Name}'.";
            return false;
        }

        internal static bool TryValidateDynamizationDefinition(HmiJsonDynamization dynamization, out string? error)
        {
            error = null;
            var dynamizationType = ResolveDynamizationClrType(dynamization.Type);
            if (dynamizationType == null)
            {
                error = $"Unsupported dynamization type '{dynamization.Type}'.";
                return false;
            }

            switch (NormalizeDynamizationKind(dynamization.Type))
            {
                case "Script":
                    if (string.IsNullOrWhiteSpace(dynamization.Script?.ScriptCode))
                    {
                        error = $"Property '{dynamization.Property}': script dynamization requires non-empty ScriptCode.";
                        return false;
                    }
                    break;
                case "Tag":
                case "Expression":
                    if (!TryValidateValueConverterDefinition(dynamization.ValueConverter, out error))
                    {
                        error = $"Property '{dynamization.Property}': {error}";
                        return false;
                    }
                    break;
            }

            return true;
        }

        internal static bool TryValidateValueConverterDefinition(HmiJsonValueConverter? valueConverter, out string? error)
        {
            error = null;
            if (valueConverter?.MappingTable == null)
            {
                return true;
            }

            var mappingTable = valueConverter.MappingTable;
            if (!TryValidateObjectPropertyValue(typeof(MappingTable), "ConditionType", mappingTable.ConditionType, out error))
            {
                return false;
            }

            foreach (var entry in mappingTable.Entries ?? Enumerable.Empty<HmiJsonMappingTableEntry>())
            {
                var entryType = ResolveMappingTableEntryClrType(entry?.EntryType);
                if (entryType == null)
                {
                    error = $"Unsupported mapping table entry type '{entry?.EntryType}'.";
                    return false;
                }

                if (!TryValidateObjectPropertyValue(entryType, "Value", entry?.Value, out error) ||
                    !TryValidateObjectPropertyValue(entryType, "AlternateValue", entry?.AlternateValue, out error) ||
                    !TryValidateObjectPropertyValue(entryType, "Flashing", entry?.Flashing, out error) ||
                    !TryValidateObjectPropertyValue(entryType, "FlashingRate", entry?.FlashingRate, out error))
                {
                    return false;
                }

                switch (NormalizeMappingEntryKind(entry?.EntryType))
                {
                    case "Simple":
                        if (!TryValidateObjectPropertyValue(entryType, "Condition", entry?.Condition, out error))
                        {
                            return false;
                        }
                        break;
                    case "Range":
                        if (!TryValidateObjectPropertyValue(entryType, "From", entry?.From, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "To", entry?.To, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "RangeType", entry?.RangeType, out error))
                        {
                            return false;
                        }
                        break;
                    case "Bitmask":
                        if (!TryValidateObjectPropertyValue(entryType, "BitDynamizationType", entry?.BitDynamizationType, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "Condition", entry?.Condition, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "Relevant", entry?.Relevant, out error))
                        {
                            return false;
                        }
                        break;
                }
            }

            return true;
        }

        internal static bool TrySynchronizeDynamizations(
            object composition,
            IReadOnlyList<HmiJsonDynamization> desiredDynamizations,
            out string? error)
        {
            error = null;

            try
            {
                var desired = HmiJsonCompositeComparer.CloneDynamizations(desiredDynamizations)?.ToList() ?? new List<HmiJsonDynamization>();
                var currentAll = ReadRawDynamizations(composition);
                var current = currentAll.Values
                    .Where(entry => entry.Export != null)
                    .Select(entry => entry.Export!)
                    .OrderBy(entry => entry.Property, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (HmiJsonCompositeComparer.AreDynamizationCollectionsEquivalent(current, desired))
                {
                    return true;
                }

                var desiredMap = HmiJsonCompositeComparer.BuildDynamizationMap(desired);
                foreach (var currentEntry in currentAll.Values
                    .Where(entry => entry.Export != null)
                    .OrderByDescending(entry => entry.Property, StringComparer.OrdinalIgnoreCase))
                {
                    if (!desiredMap.ContainsKey(currentEntry.Property))
                    {
                        DeleteEngineeringObject(currentEntry.Raw);
                    }
                }

                foreach (var dynamization in desired)
                {
                    currentAll.TryGetValue(dynamization.Property, out var currentEntry);
                    var targetDynamization = currentEntry.Raw;
                    var currentKind = NormalizeDynamizationKind(currentEntry.Export?.Type, currentEntry.Raw?.GetType().Name);
                    var desiredKind = NormalizeDynamizationKind(dynamization.Type);

                    if (targetDynamization != null &&
                        !string.Equals(currentKind, desiredKind, StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteEngineeringObject(targetDynamization);
                        targetDynamization = null;
                    }

                    targetDynamization ??= CreateDynamizationObject(composition, dynamization);
                    ApplyDynamizationExport(targetDynamization, dynamization);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        internal static bool TrySynchronizeInterfaceBindings(
            object composition,
            IReadOnlyList<HmiJsonInterfaceBinding> desiredBindings,
            out string? error)
        {
            error = null;

            try
            {
                var desired = HmiJsonCompositeComparer.CloneInterfaceBindings(desiredBindings)?.ToList()
                              ?? new List<HmiJsonInterfaceBinding>();
                var currentAll = ReadRawInterfaceBindings(composition);
                var current = currentAll.Values
                    .Where(entry => entry.Export != null)
                    .Select(entry => entry.Export!)
                    .OrderBy(entry => entry.PropertyName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (HmiJsonCompositeComparer.AreInterfaceBindingCollectionsEquivalent(current, desired))
                {
                    return true;
                }

                var changedProperties = HmiJsonCompositeComparer.GetChangedInterfaceProperties(current, desired);
                foreach (var binding in desired.Where(binding =>
                    changedProperties.Contains(binding.PropertyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)))
                {
                    var propertyName = NormalizeOptionalString(binding.PropertyName)!;
                    if (!currentAll.TryGetValue(propertyName, out var currentEntry) || currentEntry.Raw == null)
                    {
                        throw new InvalidOperationException($"Interface property '{propertyName}' was not found.");
                    }

                    var valueChanged = !string.Equals(
                        NormalizeOptionalString(currentEntry.Export?.Value),
                        NormalizeOptionalString(binding.Value),
                        StringComparison.Ordinal);

                    try
                    {
                        if (valueChanged)
                        {
                            SetObjectPropertyValueFlexible(currentEntry.Raw, "Value", binding.Value ?? string.Empty);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Interface property '{propertyName}' could not be written on {currentEntry.Raw.GetType().Name}: {FormatExceptionMessage(ex)}",
                            ex);
                    }

                    if (!TryGetNamedComposition(currentEntry.Raw, "Dynamizations", out var dynamizationComposition))
                    {
                        if ((binding.Dynamizations?.Count ?? 0) > 0)
                        {
                            throw new InvalidOperationException($"Interface property '{propertyName}' does not expose Dynamizations.");
                        }

                        continue;
                    }

                    if (!TrySynchronizeDynamizations(dynamizationComposition, binding.Dynamizations ?? new List<HmiJsonDynamization>(), out var dynamizationError))
                    {
                        throw new InvalidOperationException($"Interface property '{propertyName}' dynamizations could not be written: {dynamizationError}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = FormatExceptionMessage(ex);
                return false;
            }
        }

        internal static Dictionary<string, (object Raw, HmiJsonInterfaceBinding? Export, string PropertyName)> ReadRawInterfaceBindings(object composition)
        {
            var result = new Dictionary<string, (object Raw, HmiJsonInterfaceBinding? Export, string PropertyName)>(StringComparer.OrdinalIgnoreCase);
            if (composition is not IEnumerable enumerable)
            {
                return result;
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var propertyName = NormalizeOptionalString(TryReadStringProperty(entry, "PropertyName"));
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                result[propertyName!] = (entry, TryReadInterfaceBindingExport(entry), propertyName!);
            }

            return result;
        }

        internal static Dictionary<string, (object Raw, HmiJsonDynamization? Export, string Property)> ReadRawDynamizations(object composition)
        {
            var result = new Dictionary<string, (object Raw, HmiJsonDynamization? Export, string Property)>(StringComparer.OrdinalIgnoreCase);
            if (composition is not IEnumerable enumerable)
            {
                return result;
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var propertyName = NormalizeOptionalString(TryReadStringProperty(entry, "PropertyName"));
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                result[propertyName!] = (entry, TryReadDynamizationExport(entry), propertyName!);
            }

            return result;
        }

        internal static Type? ResolveDynamizationClrType(string? kind)
        {
            switch (NormalizeDynamizationKind(kind))
            {
                case "Tag":
                    return typeof(TagDynamization);
                case "Script":
                    return typeof(ScriptDynamization);
                case "Flashing":
                    return typeof(FlashingDynamization);
                case "Expression":
                    return typeof(ExpressionDynamization);
                case "ResourceList":
                    return typeof(ResourceListDynamization);
                case "TagParameter":
                    return typeof(TagParameterDynamization);
                default:
                    return null;
            }
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

        internal static object CreateDynamizationObject(object composition, HmiJsonDynamization dynamization)
        {
            var createMethod = ResolveSingleCreateMethod(
                composition.GetType(),
                "Dynamizations.Create<T>(string)",
                new[] { typeof(string) },
                requireGenericMethodDefinition: true);

            var dynamizationType = ResolveDynamizationClrType(dynamization.Type)
                ?? throw new InvalidOperationException($"Unsupported dynamization type '{dynamization.Type}'.");
            return createMethod.MakeGenericMethod(dynamizationType).Invoke(composition, new object[] { dynamization.Property })
                ?? throw new InvalidOperationException($"Failed to create dynamization '{dynamization.Property}'.");
        }

        internal static HmiJsonInterfaceBinding? TryReadInterfaceBindingExport(object? binding)
        {
            var propertyName = NormalizeOptionalString(TryReadStringProperty(binding, "PropertyName"));
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var rawValue = TryReadPropertyValue(binding, "Value");
            return new HmiJsonInterfaceBinding
            {
                PropertyName = propertyName!,
                ValueType = rawValue?.GetType().Name,
                Value = NormalizeOptionalString(FormatValue(rawValue)),
                Dynamizations = TryReadDynamizationsFromOwner(binding)
            };
        }

        internal static List<HmiJsonDynamization>? TryReadDynamizationsFromOwner(object? owner)
        {
            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                return null;
            }

            var dynamizations = ReadSupportedDynamizationsFromComposition(composition);
            return dynamizations.Count > 0 ? dynamizations : null;
        }

    }
}
