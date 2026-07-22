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
    internal sealed class V21DocumentAccess
    {
        private readonly V21TypeServiceResolver _resolver;
        public V21DocumentAccess(V21TypeServiceResolver resolver)
        {
            _resolver = resolver;
        }
        public object? GetProgrammingLanguageValue(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SCL", "SCL" },
                { "FBD", "FBD" },
                { "LAD", "LD" },
                { "STL", "STL" },
                { "GRAPH", "GRAPH" },
                { "PRODIAG", "ProDiag" },
                { "S7_PDIAG", "ProDiag" }
            };

            var requestedLanguage = language!;
            var targetName = languageMapping.TryGetValue(requestedLanguage, out var mappedName)
                ? mappedName
                : requestedLanguage;

            foreach (var value in Enum.GetValues(typeof(ProgrammingLanguage)))
            {
                if (value?.ToString()?.Equals(targetName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return value;
                }
            }

            return null;
        }

        public object? CreateFb(object blocksCollection, string blockName, object? programmingLanguageValue)
        {
            if (blocksCollection == null) throw new ArgumentNullException(nameof(blocksCollection));
            if (string.IsNullOrWhiteSpace(blockName)) throw new ArgumentException("Block name cannot be empty.", nameof(blockName));

            var blocksCollectionType = blocksCollection.GetType();

            if (programmingLanguageValue != null)
            {
                var createWithLanguage = blocksCollectionType.GetMethod(
                    "CreateFB",
                    new[] { typeof(string), typeof(bool), typeof(int), programmingLanguageValue.GetType() });

                if (createWithLanguage != null)
                {
                    return createWithLanguage.Invoke(blocksCollection, new object[] { blockName, true, 0, programmingLanguageValue });
                }
            }

            var createSimple = blocksCollectionType.GetMethod(
                "CreateFB",
                new[] { typeof(string), typeof(bool), typeof(int) });

            return createSimple?.Invoke(blocksCollection, new object[] { blockName, true, 0 });
        }

        public bool TryExportEngineeringObject(object engineeringObject, string exportPath, string preferredOptionName)
        {
            return TryExportEngineeringObject(engineeringObject, exportPath, preferredOptionName, out _);
        }

        public bool TryExportEngineeringObject(object engineeringObject, string exportPath, string preferredOptionName, out string? failureReason)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (string.IsNullOrWhiteSpace(exportPath)) throw new ArgumentException("Export path cannot be empty.", nameof(exportPath));

            failureReason = null;
            var exportOptionsType = FindTypeInAssembly(engineeringObject.GetType().Assembly, "ExportOptions")
                ?? typeof(ExportOptions);
            var preferredOptionValue = exportOptionsType?.GetField(preferredOptionName)?.GetValue(null);

            if (exportOptionsType != null && preferredOptionValue != null)
            {
                var exportWithOptions = engineeringObject.GetType().GetMethod("Export", new[] { typeof(FileInfo), exportOptionsType });
                if (TryInvoke(exportWithOptions, engineeringObject, new object[] { new FileInfo(exportPath), preferredOptionValue }, out var attemptedFailureReason))
                {
                    return true;
                }

                failureReason ??= attemptedFailureReason;
            }

            var exportWithFileInfo = engineeringObject.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
            if (TryInvoke(exportWithFileInfo, engineeringObject, new object[] { new FileInfo(exportPath) }, out var fileInfoFailureReason))
            {
                return true;
            }

            failureReason ??= fileInfoFailureReason;

            var exportWithString = engineeringObject.GetType().GetMethod("Export", new[] { typeof(string) });
            if (TryInvoke(exportWithString, engineeringObject, new object[] { exportPath }, out var stringFailureReason))
            {
                return true;
            }

            failureReason ??= stringFailureReason;
            return false;
        }

        public bool TryExportAsDocuments(object engineeringObject, DirectoryInfo exportDirectory, string baseName, out string? state)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (exportDirectory == null) throw new ArgumentNullException(nameof(exportDirectory));
            if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("Base name cannot be empty.", nameof(baseName));

            state = null;
            var exportMethod = engineeringObject.GetType().GetMethod("ExportAsDocuments", new[] { typeof(DirectoryInfo), typeof(string) });
            if (exportMethod == null)
            {
                return false;
            }

            var result = exportMethod.Invoke(engineeringObject, new object[] { exportDirectory, baseName });
            state = TiaReflectionHelper.GetProperty(result, "State")?.ToString();
            return string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryImport(object collection, FileInfo fileInfo, int importMode, out object? result)
        {
            return TryImport(collection, fileInfo, importMode, out result, out _);
        }

        public bool TryImport(object collection, FileInfo fileInfo, int importMode, out object? result, out string? failureReason)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (fileInfo == null) throw new ArgumentNullException(nameof(fileInfo));

            result = null;
            failureReason = null;
            foreach (var importMethod in collection.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, "Import", StringComparison.Ordinal))
                .OrderBy(method => method.GetParameters().Length))
            {
                var parameters = importMethod.GetParameters();
                if (parameters.Length is < 2 or > 3 || parameters[0].ParameterType != typeof(FileInfo))
                {
                    continue;
                }

                if (!TryCreateImportArgument(parameters[1].ParameterType, importMode, out var importOptionValue))
                {
                    continue;
                }

                var arguments = new object?[parameters.Length];
                arguments[0] = fileInfo;
                arguments[1] = importOptionValue;

                if (parameters.Length == 3)
                {
                    if (!TryCreateDefaultArgument(parameters[2].ParameterType, out var extraArgument))
                    {
                        continue;
                    }

                    arguments[2] = extraArgument;
                }

                try
                {
                    result = importMethod.Invoke(collection, arguments);
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                    // Try the next compatible overload if invocation fails.
                }
            }

            failureReason ??= "No compatible Import(FileInfo, import option) overload succeeded.";
            return false;
        }

        public bool TryImportFromDocuments(object collection, DirectoryInfo importDirectory, string baseName, int importMode, out object? result)
        {
            return TryImportFromDocuments(collection, importDirectory, baseName, importMode, out result, out _);
        }

        public bool TryImportFromDocuments(object collection, DirectoryInfo importDirectory, string baseName, int importMode, out object? result, out string? failureReason)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (importDirectory == null) throw new ArgumentNullException(nameof(importDirectory));
            if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("Base name cannot be empty.", nameof(baseName));

            result = null;
            failureReason = null;
            foreach (var importMethod in collection.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, "ImportFromDocuments", StringComparison.Ordinal))
                .OrderBy(method => method.GetParameters().Length))
            {
                var parameters = importMethod.GetParameters();
                if (parameters.Length != 3 ||
                    parameters[0].ParameterType != typeof(DirectoryInfo) ||
                    parameters[1].ParameterType != typeof(string))
                {
                    continue;
                }

                if (!TryCreateImportArgument(parameters[2].ParameterType, importMode, out var importOptionValue))
                {
                    continue;
                }

                try
                {
                    result = importMethod.Invoke(collection, new[] { (object)importDirectory, baseName, importOptionValue });
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                    // Try the next compatible overload if invocation fails.
                }
            }

            failureReason ??= "No compatible ImportFromDocuments(DirectoryInfo, string, import option) overload succeeded.";
            return false;
        }

        private static bool TryCreateImportArgument(Type parameterType, int importMode, out object? value)
        {
            if (parameterType == typeof(int))
            {
                value = importMode;
                return true;
            }

            if (parameterType.IsEnum)
            {
                value = Enum.ToObject(parameterType, importMode);
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryCreateDefaultArgument(Type parameterType, out object? value)
        {
            if (!parameterType.IsValueType)
            {
                value = null;
                return true;
            }

            value = Activator.CreateInstance(parameterType);
            return true;
        }

        private static IReadOnlyList<string> GetInvocationNames(object engineeringObject)
        {
            if (engineeringObject is IEngineeringObject typedEngineeringObject)
            {
                try
                {
                    return typedEngineeringObject
                        .GetInvocationInfos()
                        .Select(info => info.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    // Fall back to reflective lookup below.
                }
            }

            var getInvocationInfos = engineeringObject.GetType().GetMethod("GetInvocationInfos", Type.EmptyTypes);
            if (getInvocationInfos == null)
            {
                return Array.Empty<string>();
            }

            try
            {
                var result = getInvocationInfos.Invoke(engineeringObject, null) as IEnumerable;
                if (result == null)
                {
                    return Array.Empty<string>();
                }

                return result
                    .Cast<object>()
                    .Select(info => TiaReflectionHelper.GetProperty(info, "Name")?.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
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

        private static Type? FindTypeInAssembly(Assembly assembly, string typeName)
        {
            try
            {
                return assembly.GetTypes().FirstOrDefault(type => string.Equals(type.Name, typeName, StringComparison.Ordinal));
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.FirstOrDefault(type => type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal));
            }
        }
    }
}
