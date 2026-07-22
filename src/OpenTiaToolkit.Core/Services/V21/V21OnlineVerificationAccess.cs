using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Download;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System.Linq.Expressions;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal sealed class V21OnlineVerificationAccess
    {
        private readonly V21TypeServiceResolver _resolver;
        public V21OnlineVerificationAccess(V21TypeServiceResolver resolver)
        {
            _resolver = resolver;
        }
        public bool TryCompareToOnline(object plcSoftware, out object? compareResult, out string? failureReason)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            compareResult = null;
            failureReason = null;

            var compareMethod = plcSoftware.GetType().GetMethod("CompareToOnline", Type.EmptyTypes);
            if (compareMethod == null)
            {
                failureReason = "PLC software does not expose CompareToOnline in this TIA Portal version.";
                return false;
            }

            try
            {
                compareResult = compareMethod.Invoke(plcSoftware, null);
                if (compareResult == null)
                {
                    failureReason = "CompareToOnline returned no result.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public IReadOnlyList<PlcSoftwareCompareEntry> ExtractCompareEntries(object compareResult)
        {
            if (compareResult == null) throw new ArgumentNullException(nameof(compareResult));

            var entries = new List<PlcSoftwareCompareEntry>();
            var rootElement = TiaReflectionHelper.GetProperty(compareResult, "RootElement");
            if (rootElement != null)
            {
                CollectCompareEntries(rootElement, string.Empty, entries);
            }

            return entries;
        }

        public bool TrySelectPlcSimulationAdvanced(object downloadProvider, out string? message)
        {
            if (downloadProvider == null) throw new ArgumentNullException(nameof(downloadProvider));

            message = null;
            var configuration = TiaReflectionHelper.GetProperty(downloadProvider, "Configuration");
            if (configuration == null)
            {
                message = "DownloadProvider.Configuration is not available.";
                return false;
            }

            var targetForSoftware = FindObjectByTypeName(configuration, "TargetForSoftware", maxDepth: 8);
            if (targetForSoftware == null)
            {
                message = "TargetForSoftware configuration was not found.";
                return false;
            }

            var currentSelectionProperty = targetForSoftware.GetType().GetProperty("CurrentSelection");
            if (currentSelectionProperty == null || !currentSelectionProperty.CanWrite)
            {
                message = "TargetForSoftware.CurrentSelection is not writable.";
                return false;
            }

            if (!TryCreateEnumValue(currentSelectionProperty.PropertyType, "PlcSimulationAdvanced", out var simulatorSelection))
            {
                message = "TargetForSoftwareSelections.PlcSimulationAdvanced is not available.";
                return false;
            }

            currentSelectionProperty.SetValue(targetForSoftware, simulatorSelection);
            return true;
        }

        public bool IsDownloadConfigurationReady(object downloadProvider)
        {
            if (downloadProvider == null) throw new ArgumentNullException(nameof(downloadProvider));

            var configuration = TiaReflectionHelper.GetProperty(downloadProvider, "Configuration");
            return TiaReflectionHelper.GetProperty(configuration, "IsConfigured") is bool configured && configured;
        }

        public bool TryDownloadSoftware(object downloadProvider, bool onlyChanges, out string? state, out string? failureReason)
        {
            if (downloadProvider == null) throw new ArgumentNullException(nameof(downloadProvider));

            state = null;
            failureReason = null;

            var connectionConfiguration = TiaReflectionHelper.GetProperty(downloadProvider, "Configuration");
            if (connectionConfiguration == null)
            {
                failureReason = "DownloadProvider.Configuration is not available.";
                return false;
            }

            var downloadMethod = downloadProvider.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "Download")
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 4 &&
                           parameters[0].ParameterType.Name == "IConfiguration" &&
                           parameters[3].ParameterType.IsEnum;
                });

            if (downloadMethod == null)
            {
                failureReason = "No compatible Download(IConfiguration, ..., DownloadOptions) overload was found.";
                return false;
            }

            var targetConfigurationType = downloadMethod.GetParameters()[0].ParameterType;
            var targetConfiguration = FindObjectAssignableToType(connectionConfiguration, targetConfigurationType, maxDepth: 8);
            if (targetConfiguration == null)
            {
                failureReason = "No configured PLCSIM target interface was found for the download.";
                return false;
            }

            var optionsType = downloadMethod.GetParameters()[3].ParameterType;
            var optionName = onlyChanges ? "SoftwareOnlyChanges" : "Software";
            if (!TryCreateEnumValue(optionsType, optionName, out var softwareOption))
            {
                failureReason = $"DownloadOptions.{optionName} is not available.";
                return false;
            }

            try
            {
                var downloadConfigurationDelegateType = downloadMethod.GetParameters()[1].ParameterType;
                var configureDelegate = CreateDownloadConfigurationDelegate(downloadConfigurationDelegateType);
                var postDownloadConfigurationDelegateType = downloadMethod.GetParameters()[2].ParameterType;
                var postConfigureDelegate = CreateDownloadConfigurationDelegate(postDownloadConfigurationDelegateType);
                var result = downloadMethod.Invoke(downloadProvider, new[] { targetConfiguration, configureDelegate, postConfigureDelegate, softwareOption });
                state = TiaReflectionHelper.GetProperty(result, "State")?.ToString()
                    ?? TiaReflectionHelper.GetProperty(result, "Result")?.ToString()
                    ?? result?.ToString();

                return !string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public string GetOnlineState(object onlineProvider)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            return TiaReflectionHelper.GetProperty(onlineProvider, "State")?.ToString() ?? "Unknown";
        }

        public bool TryGoOnline(object onlineProvider, out string? state, out string? failureReason)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            state = GetOnlineState(onlineProvider);
            failureReason = null;
            if (string.Equals(state, "Online", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var goOnline = onlineProvider.GetType().GetMethod("GoOnline", Type.EmptyTypes);
            if (goOnline == null)
            {
                failureReason = "OnlineProvider does not expose GoOnline.";
                return false;
            }

            try
            {
                var result = goOnline.Invoke(onlineProvider, null);
                state = result?.ToString() ?? GetOnlineState(onlineProvider);
                return string.Equals(state, "Online", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                state = GetOnlineState(onlineProvider);
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public bool TryGoOffline(object onlineProvider, out string? failureReason)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            failureReason = null;
            var state = GetOnlineState(onlineProvider);
            if (string.Equals(state, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var goOffline = onlineProvider.GetType().GetMethod("GoOffline", Type.EmptyTypes);
            if (goOffline == null)
            {
                failureReason = "OnlineProvider does not expose GoOffline.";
                return false;
            }

            try
            {
                goOffline.Invoke(onlineProvider, null);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        public bool TryControlCpu(object onlineProvider, string action, out string? state, out string? failureReason)
        {
            if (onlineProvider == null) throw new ArgumentNullException(nameof(onlineProvider));

            state = GetOnlineState(onlineProvider);
            failureReason = null;

            var candidateNames = string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase)
                ? new[] { "StopCpu", "StopPlc", "Stop", "SetCpuToStop" }
                : new[] { "StartCpu", "StartPlc", "Start", "Run", "SetCpuToRun" };

            var method = onlineProvider.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.GetParameters().Length == 0 &&
                                             candidateNames.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase));

            if (method == null)
            {
                failureReason = $"TIA Openness OnlineProvider exposes no supported CPU {action} operation. Available invocations: " +
                                string.Join(", ", GetInvocationNames(onlineProvider));
                return false;
            }

            try
            {
                method.Invoke(onlineProvider, null);
                state = GetOnlineState(onlineProvider);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                state = GetOnlineState(onlineProvider);
                return false;
            }
        }

        private static Delegate CreateDownloadConfigurationDelegate(Type delegateType)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var parameter = invoke?.GetParameters().SingleOrDefault();
            if (invoke == null || parameter == null)
            {
                throw new InvalidOperationException("Unsupported download configuration delegate shape.");
            }

            var parameterExpression = Expression.Parameter(parameter.ParameterType, "downloadConfiguration");
            var body = Expression.Call(
                typeof(V21OnlineVerificationAccess).GetMethod(nameof(ConfigurePlcSimulationAdvanced), BindingFlags.NonPublic | BindingFlags.Static)!,
                Expression.Convert(parameterExpression, typeof(object)));

            return Expression.Lambda(delegateType, body, parameterExpression).Compile();
        }

        private static void ConfigurePlcSimulationAdvanced(object downloadConfiguration)
        {
            var targetForSoftware = FindObjectByTypeName(downloadConfiguration, "TargetForSoftware", maxDepth: 8);
            if (targetForSoftware == null)
            {
                return;
            }

            var currentSelectionProperty = targetForSoftware.GetType().GetProperty("CurrentSelection");
            if (currentSelectionProperty == null || !currentSelectionProperty.CanWrite)
            {
                return;
            }

            if (TryCreateEnumValue(currentSelectionProperty.PropertyType, "PlcSimulationAdvanced", out var simulatorSelection))
            {
                currentSelectionProperty.SetValue(targetForSoftware, simulatorSelection);
            }
        }

        private static void CollectCompareEntries(object element, string parentPath, List<PlcSoftwareCompareEntry> entries)
        {
            var leftName = TiaReflectionHelper.GetProperty(element, "LeftName")?.ToString();
            var rightName = TiaReflectionHelper.GetProperty(element, "RightName")?.ToString();
            var state = TiaReflectionHelper.GetProperty(element, "ComparisonResult")?.ToString()
                ?? TiaReflectionHelper.GetProperty(element, "State")?.ToString()
                ?? "Unknown";
            var detail = TiaReflectionHelper.GetProperty(element, "DetailedInformation")?.ToString();
            var name = !string.IsNullOrWhiteSpace(leftName) ? leftName : rightName;
            var path = string.IsNullOrWhiteSpace(parentPath)
                ? name ?? "<root>"
                : string.IsNullOrWhiteSpace(name) ? parentPath : parentPath + "/" + name;

            entries.Add(new PlcSoftwareCompareEntry(
                path,
                state,
                leftName,
                rightName,
                detail,
                IsCompareDifferenceState(state)));

            var children = TiaReflectionHelper.GetProperty(element, "Elements") as IEnumerable;
            if (children == null)
            {
                return;
            }

            foreach (var child in children)
            {
                if (child != null)
                {
                    CollectCompareEntries(child, path, entries);
                }
            }
        }

        private static bool IsCompareDifferenceState(string state)
        {
            return state.IndexOf("Different", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   state.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryCreateEnumValue(Type enumType, string name, out object? value)
        {
            value = null;
            if (!enumType.IsEnum || !Enum.IsDefined(enumType, name))
            {
                return false;
            }

            value = Enum.Parse(enumType, name);
            return true;
        }

        private static object? FindObjectByTypeName(object root, string typeName, int maxDepth)
        {
            return FindObjectByTypeName(root, typeName, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static object? FindObjectByTypeName(object? current, string typeName, int remainingDepth, HashSet<object> visited)
        {
            if (current == null || remainingDepth < 0 || !visited.Add(current))
            {
                return null;
            }

            if (string.Equals(current.GetType().Name, typeName, StringComparison.Ordinal))
            {
                return current;
            }

            if (current is string)
            {
                return null;
            }

            if (current is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var match = FindObjectByTypeName(item, typeName, remainingDepth - 1, visited);
                    if (match != null)
                    {
                        return match;
                    }
                }

                return null;
            }

            foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                var match = FindObjectByTypeName(value, typeName, remainingDepth - 1, visited);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static object? FindObjectAssignableToType(object root, Type targetType, int maxDepth)
        {
            return FindObjectAssignableToType(root, targetType, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static object? FindObjectAssignableToType(object? current, Type targetType, int remainingDepth, HashSet<object> visited)
        {
            if (current == null || remainingDepth < 0 || !visited.Add(current))
            {
                return null;
            }

            if (targetType.IsInstanceOfType(current) && !string.Equals(current.GetType().Name, "ConnectionConfiguration", StringComparison.Ordinal))
            {
                return current;
            }

            if (current is string)
            {
                return null;
            }

            if (current is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var match = FindObjectAssignableToType(item, targetType, remainingDepth - 1, visited);
                    if (match != null)
                    {
                        return match;
                    }
                }

                return null;
            }

            foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                var match = FindObjectAssignableToType(value, targetType, remainingDepth - 1, visited);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static bool TryInvoke(MethodInfo? method, object target, object[] arguments, out string? failureReason)
        {
            failureReason = null;
            if (method == null)
            {
                failureReason = "Method overload was not found.";
                return false;
            }

            try
            {
                method.Invoke(target, arguments);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = FormatInvocationException(ex);
                return false;
            }
        }

        private static string FormatInvocationException(Exception ex)
        {
            return ex is TargetInvocationException { InnerException: { } innerException }
                ? innerException.Message
                : ex.Message;
        }

        private static IReadOnlyList<string> GetInvocationNames(object engineeringObject)
        {
            if (engineeringObject is IEngineeringObject typed)
            {
                try
                {
                    return typed.GetInvocationInfos().Select(info => info.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch { }
            }

            return Array.Empty<string>();
        }
    }
}
