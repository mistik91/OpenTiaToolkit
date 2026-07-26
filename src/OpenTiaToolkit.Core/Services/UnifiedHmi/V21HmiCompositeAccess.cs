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
using static OpenTiaToolkit.Core.Services.V21HmiDynamizationSynchronizer;
using static OpenTiaToolkit.Core.Services.V21HmiEventSynchronizer;
using static OpenTiaToolkit.Core.Services.V21HmiMappingTableSynchronizer;
using static OpenTiaToolkit.Core.Services.V21HmiItemPropertyAccessor;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class V21HmiCompositeAccess
    {
        private readonly V21HmiItemPropertyAccessor _properties;

        internal V21HmiCompositeAccess(V21HmiItemPropertyAccessor properties)
        {
            _properties = properties ?? throw new ArgumentNullException(nameof(properties));
        }

        public IReadOnlyList<HmiItemPropertyInfo> GetItemProperties(HmiScreenItemBase item, IEnumerable<string>? propertyNames)
        {
            var result = new Dictionary<string, HmiItemPropertyInfo>(StringComparer.OrdinalIgnoreCase);

            if (propertyNames != null)
            {
                foreach (var propertyName in propertyNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                {
                    if (_properties.TryReadPropertyOrAttribute(item, propertyName.Trim(), out var property))
                    {
                        result[property.Name] = property;
                    }
                }

                return result.Values.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }

            foreach (var property in _properties.ReadBrowsableProperties(item))
            {
                result[property.Name] = property;
            }

            foreach (var property in _properties.ReadAttributeProperties(item))
            {
                result[property.Name] = property;
            }

            return result.Values.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public bool TrySetItemProperty(HmiScreenItemBase item, string propertyName, string value, HmiSoftware software, out string? error)
        {
            error = null;

            try
            {
                if (propertyName.IndexOf("BackColor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TryEnsureSolidBackFillPattern(item);
                }

                if (_properties.TrySetPropertyPathValue(item, propertyName.Trim(), value, software))
                {
                    return true;
                }

                error = $"Property or attribute '{propertyName}' was not found or is not writable.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public bool TryValidateItemProperty(HmiScreenItemBase item, string propertyName, string value, HmiSoftware software, out string? error)
        {
            error = null;

            try
            {
                var propertyPath = propertyName.Trim();
                var resolvedProperty = ResolveProperty(item, propertyPath);
                if (resolvedProperty != null)
                {
                    if (V21HmiEngineeringPrimitives.IsMultilingualText(resolvedProperty.PropertyInfo.PropertyType))
                    {
                        if (resolvedProperty.PropertyInfo.GetValue(resolvedProperty.Target) == null)
                        {
                            error = $"Multilingual text property '{propertyPath}' is not initialized.";
                            return false;
                        }

                        return true;
                    }

                    if (!resolvedProperty.PropertyInfo.CanWrite)
                    {
                        error = $"Property '{propertyPath}' is read-only.";
                        return false;
                    }

                    if ((Nullable.GetUnderlyingType(resolvedProperty.PropertyInfo.PropertyType) ?? resolvedProperty.PropertyInfo.PropertyType) == typeof(string))
                    {
                        V21HmiEngineeringPrimitives.NormalizeStringPropertyValue(resolvedProperty.Target, resolvedProperty.PropertyInfo, value);
                        return true;
                    }

                    _properties.ConvertStringToType(value, resolvedProperty.PropertyInfo.PropertyType, software);
                    return true;
                }

                var resolvedAttribute = ResolveAttribute(item, propertyPath);
                if (resolvedAttribute == null)
                {
                    error = $"Property or attribute '{propertyName}' was not found or is not writable.";
                    return false;
                }

                if (resolvedAttribute.AttributeInfo.AccessMode != EngineeringAttributeAccessMode.Write &&
                    resolvedAttribute.AttributeInfo.AccessMode != EngineeringAttributeAccessMode.ReadWrite)
                {
                    error = $"Attribute '{propertyPath}' is not writable.";
                    return false;
                }

                _properties.ConvertStringToAttributeValue(
                    value,
                    resolvedAttribute.Target,
                    resolvedAttribute.AttributeInfo,
                    software);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public HmiFaceplateInterfaceProbeResult ProbeFaceplateInterface(
            string screenPath,
            HmiScreenItemBase item,
            string? propertyName,
            bool write,
            string? value,
            bool restore)
        {
            var targetProperty = NormalizeOptionalString(propertyName);
            var result = new HmiFaceplateInterfaceProbeResult
            {
                ScreenPath = screenPath,
                ItemName = TryReadStringProperty(item, "Name") ?? string.Empty,
                ItemType = item.GetType().FullName ?? item.GetType().Name,
                ContainedType = TryReadStringProperty(item, "ContainedType"),
                TargetProperty = targetProperty,
                WriteRequested = write,
                WriteValue = value,
                RestoreRequested = write && restore
            };

            if (!TryGetNamedComposition(item, "Interface", out var composition) || composition is not IEnumerable enumerable)
            {
                result.WriteError = "Interface is not available or is not enumerable.";
                return result;
            }

            object? targetEntry = null;
            object? originalRawValue = null;

            foreach (var entry in enumerable.Cast<object>())
            {
                var entryName = NormalizeOptionalString(TryReadStringProperty(entry, "PropertyName"));
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue;
                }

                var valueProperty = V21HmiEngineeringPrimitives.ResolvePublicInstanceProperty(entry.GetType(), "Value");
                var rawValue = valueProperty?.CanRead == true ? V21HmiEngineeringPrimitives.TryReadPropertyValue(entry, "Value") : null;
                var probeEntry = new HmiFaceplateInterfaceProbeEntry
                {
                    PropertyName = entryName!,
                    Value = NormalizeOptionalString(V21HmiEngineeringPrimitives.FormatValue(rawValue)),
                    EntryType = entry.GetType().FullName ?? entry.GetType().Name,
                    ValuePropertyType = valueProperty?.PropertyType.FullName,
                    ValueRuntimeType = rawValue?.GetType().FullName,
                    ValueDeclaringType = valueProperty?.DeclaringType?.FullName,
                    ValueCanRead = valueProperty?.CanRead == true,
                    ValueCanWrite = valueProperty?.CanWrite == true,
                    IsTarget = !string.IsNullOrWhiteSpace(targetProperty) &&
                        entryName!.Equals(targetProperty, StringComparison.OrdinalIgnoreCase)
                };
                result.Entries.Add(probeEntry);

                if (probeEntry.IsTarget)
                {
                    targetEntry = entry;
                    originalRawValue = rawValue;
                }
            }

            result.Entries = result.Entries
                .OrderBy(entry => entry.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!write)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(targetProperty))
            {
                result.WriteError = "A property name is required when write is true.";
                return result;
            }

            if (targetEntry == null)
            {
                result.WriteError = $"Interface property '{targetProperty}' was not found.";
                return result;
            }

            var targetProbeEntry = result.Entries.First(entry => entry.IsTarget);
            try
            {
                var binding = new HmiJsonInterfaceBinding
                {
                    PropertyName = targetProperty!,
                    ValueType = originalRawValue?.GetType().Name,
                    Value = value
                };
                if (!V21HmiInterfaceValueAccessor.TryWriteValue(targetEntry, binding, out var valueError))
                {
                    throw new InvalidOperationException(valueError);
                }
                result.WriteSucceeded = true;
                targetProbeEntry.ValueAfterWrite = NormalizeOptionalString(V21HmiEngineeringPrimitives.FormatValue(V21HmiEngineeringPrimitives.TryReadPropertyValue(targetEntry, "Value")));
            }
            catch (Exception ex)
            {
                result.WriteError = V21HmiEngineeringPrimitives.FormatExceptionMessage(ex);
                return result;
            }

            if (restore)
            {
                try
                {
                    if (!V21HmiInterfaceValueAccessor.TryWriteRawValue(targetEntry, originalRawValue, out var restoreError))
                    {
                        throw new InvalidOperationException(restoreError);
                    }
                    result.RestoreSucceeded = true;
                    targetProbeEntry.ValueAfterRestore = NormalizeOptionalString(V21HmiEngineeringPrimitives.FormatValue(V21HmiEngineeringPrimitives.TryReadPropertyValue(targetEntry, "Value")));
                }
                catch (Exception ex)
                {
                    result.RestoreError = V21HmiEngineeringPrimitives.FormatExceptionMessage(ex);
                }
            }

            return result;
        }

        public bool TryResolveItemClrType(string itemType, out Type resolvedType)
        {
            if (_properties.TryResolveItemType(itemType, out var descriptor))
            {
                resolvedType = descriptor.ItemType;
                return true;
            }

            resolvedType = null!;
            return false;
        }

        public bool TryReadEventHandlers(object owner, out List<HmiJsonEventHandler> eventHandlers, out string? error)
        {
            eventHandlers = new List<HmiJsonEventHandler>();
            error = null;

            if (!TryGetNamedComposition(owner, "EventHandlers", out var composition))
            {
                return false;
            }

            try
            {
                eventHandlers = ReadEventHandlersFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                eventHandlers = new List<HmiJsonEventHandler>();
                return false;
            }
        }

        public bool TryReadPropertyEventHandlers(object owner, out List<HmiJsonPropertyEventHandler> propertyEventHandlers, out string? error)
        {
            propertyEventHandlers = new List<HmiJsonPropertyEventHandler>();
            error = null;

            if (!TryGetNamedComposition(owner, "PropertyEventHandlers", out var composition))
            {
                return false;
            }

            try
            {
                propertyEventHandlers = ReadPropertyEventHandlersFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                propertyEventHandlers = new List<HmiJsonPropertyEventHandler>();
                return false;
            }
        }

        public bool TryReadDynamizations(object owner, out List<HmiJsonDynamization> dynamizations, out string? error)
        {
            dynamizations = new List<HmiJsonDynamization>();
            error = null;

            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                return false;
            }

            try
            {
                dynamizations = ReadSupportedDynamizationsFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                dynamizations = new List<HmiJsonDynamization>();
                return false;
            }
        }

        public bool TryReadInterfaceBindings(object owner, out List<HmiJsonInterfaceBinding> bindings, out string? error)
        {
            bindings = new List<HmiJsonInterfaceBinding>();
            error = null;

            if (!TryGetNamedComposition(owner, "Interface", out var composition))
            {
                return false;
            }

            try
            {
                bindings = ReadInterfaceBindingsFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                bindings = new List<HmiJsonInterfaceBinding>();
                return false;
            }
        }

        public bool TryValidateOrApplyEventHandlers(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonEventHandler>? desiredEventHandlers,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.CloneEventHandlers(desiredEventHandlers)?.ToList() ?? new List<HmiJsonEventHandler>();
            if (!TryValidateEventHandlerDefinitions(desired, out error))
            {
                return false;
            }

            if (!TryResolveEventHandlerEnumType(owner, ownerType, out var enumType))
            {
                error = $"EventHandlers is not available on type '{ownerType.Name}'.";
                return false;
            }

            foreach (var handler in desired)
            {
                if (!TryValidateEventHandler(handler, enumType, out error))
                {
                    return false;
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The event handler owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "EventHandlers", out var composition))
            {
                error = $"EventHandlers is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizeEventHandlers(composition, desired, enumType, out error);
        }

        public bool TryValidateOrApplyPropertyEventHandlers(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonPropertyEventHandler>? desiredPropertyEventHandlers,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.ClonePropertyEventHandlers(desiredPropertyEventHandlers)?.ToList()
                          ?? new List<HmiJsonPropertyEventHandler>();
            if (!TryValidatePropertyEventHandlerDefinitions(desired, out error))
            {
                return false;
            }

            if (!TryResolvePropertyEventHandlerEnumType(owner, ownerType, out var enumType))
            {
                error = $"PropertyEventHandlers is not available on type '{ownerType.Name}'.";
                return false;
            }

            foreach (var handler in desired)
            {
                if (!TryValidatePropertyEventHandler(handler, enumType, out error))
                {
                    return false;
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The property event handler owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "PropertyEventHandlers", out var composition))
            {
                error = $"PropertyEventHandlers is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizePropertyEventHandlers(composition, desired, enumType, out error);
        }

        public bool TryValidateOrApplyDynamizations(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonDynamization>? desiredDynamizations,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.CloneDynamizations(desiredDynamizations)?.ToList() ?? new List<HmiJsonDynamization>();
            if (!TryValidateDynamizationDefinitions(desired, out error))
            {
                return false;
            }

            foreach (var dynamization in desired)
            {
                if (!TryValidateDynamizationTarget(owner, ownerType, dynamization, out error) ||
                    !TryValidateDynamizationDefinition(dynamization, out error))
                {
                    return false;
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The dynamization owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                error = $"Dynamizations is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizeDynamizations(composition, desired, out error);
        }

        public bool TryValidateOrApplyInterfaceBindings(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonInterfaceBinding>? desiredBindings,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.CloneInterfaceBindings(desiredBindings)?.ToList()
                          ?? new List<HmiJsonInterfaceBinding>();
            if (!TryValidateInterfaceBindingDefinitions(desired, out error))
            {
                return false;
            }

            if (V21HmiEngineeringPrimitives.ResolvePublicInstanceProperty(ownerType, "Interface") == null)
            {
                error = $"Interface is not available on type '{ownerType.Name}'.";
                return false;
            }

            if (owner != null && TryGetNamedComposition(owner, "Interface", out var validationComposition))
            {
                var availableBindings = ReadRawInterfaceBindings(validationComposition);
                foreach (var binding in desired)
                {
                    var propertyName = NormalizeOptionalString(binding.PropertyName);
                    if (!string.IsNullOrWhiteSpace(propertyName) &&
                        !availableBindings.ContainsKey(propertyName!))
                    {
                        error = $"Interface property '{propertyName}' was not found.";
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(propertyName) &&
                        availableBindings.TryGetValue(propertyName!, out var currentEntry))
                    {
                        var valueChanged = !string.Equals(
                            NormalizeOptionalString(currentEntry.Export?.Value),
                            NormalizeOptionalString(binding.Value),
                            StringComparison.Ordinal);
                        if (valueChanged &&
                            !V21HmiInterfaceValueAccessor.TryConvertValue(
                                currentEntry.Raw,
                                binding,
                                out _,
                                out var valueError))
                        {
                            error = $"Interface.{propertyName}.Value: {valueError}";
                            return false;
                        }
                    }

                    if (!TryValidateDynamizationDefinitions(binding.Dynamizations ?? new List<HmiJsonDynamization>(), out error))
                    {
                        error = $"Interface.{propertyName}.Dynamizations: {error}";
                        return false;
                    }
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The interface owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "Interface", out var composition))
            {
                error = $"Interface is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizeInterfaceBindings(composition, desired, out error);
        }

    }
}
