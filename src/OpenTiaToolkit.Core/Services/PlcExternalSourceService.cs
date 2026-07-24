using OpenTiaToolkit.Core.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Owns the existing external-source lifecycle and source-file compatibility fallbacks.
    /// </summary>
    internal sealed class PlcExternalSourceService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly PlcBrowseService _browseService;

        public PlcExternalSourceService(EngineeringSessionContext sessionContext, PlcBrowseService browseService)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _browseService = browseService ?? throw new ArgumentNullException(nameof(browseService));
        }

        public Task<PlcSourceOperationResult> ImportAsync(string sourcePath, bool dryRun, bool confirm) =>
            Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                    return new PlcSourceOperationResult(false, false, "source-path is required.");
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                    return new PlcSourceOperationResult(false, false, "No PLC software is currently open.");
                if (!File.Exists(sourcePath))
                    return new PlcSourceOperationResult(false, false, $"Source file not found: {sourcePath}");
                if (dryRun)
                    return new PlcSourceOperationResult(true, false, $"Dry-run: source file '{sourcePath}' can be imported if the PLC external source group supports file import.", Path.GetFileNameWithoutExtension(sourcePath));
                if (!confirm)
                    return new PlcSourceOperationResult(false, false, "Importing source files mutates the PLC project. Rerun with confirm=true.");

                var sourceGroup = TiaReflectionHelper.GetExternalSourceGroup(plcSoftware);
                if (sourceGroup == null)
                    return new PlcSourceOperationResult(false, false, "PLC software does not expose ExternalSourceGroup.");
                if (!TryCreateExternalSource(sourceGroup, sourcePath, out var created, out var failureReason))
                    return new PlcSourceOperationResult(false, false, failureReason ?? "ExternalSourceGroup does not expose a supported source import method.");
                return new PlcSourceOperationResult(true, true, "Source file imported.", GetName(created) ?? Path.GetFileNameWithoutExtension(sourcePath));
            });

        public Task<PlcSourceOperationResult> GenerateBlocksAsync(string sourceName, bool dryRun, bool confirm) =>
            Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                    return new PlcSourceOperationResult(false, false, "source-name is required.");
                var source = FindExternalSource(sourceName);
                if (source == null)
                    return new PlcSourceOperationResult(false, false, $"External source '{sourceName}' was not found.");
                if (dryRun)
                    return new PlcSourceOperationResult(true, false, $"Dry-run: source '{sourceName}' can be generated if it exposes a generate method.", sourceName);
                if (!confirm)
                    return new PlcSourceOperationResult(false, false, "Generating blocks mutates the PLC project. Rerun with confirm=true.");
                if (!TryInvokeNoArgMethod(source, new[] { "GenerateBlocksFromSource", "GenerateBlocks", "Generate" }, out _, out var failureReason))
                    return new PlcSourceOperationResult(false, false, failureReason ?? "External source does not expose a supported block generation method.", sourceName);
                return new PlcSourceOperationResult(true, true, $"Generated blocks from source '{sourceName}'.", sourceName);
            });

        public Task<PlcSourceOperationResult> DeleteAsync(string sourceName, bool confirm) =>
            Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                    return new PlcSourceOperationResult(false, false, "source-name is required.");
                var source = FindExternalSource(sourceName);
                if (source == null)
                    return new PlcSourceOperationResult(false, false, $"External source '{sourceName}' was not found.", sourceName);
                if (!confirm)
                    return new PlcSourceOperationResult(false, false, "Deleting an external source requires confirm=true.", sourceName);
                if (!TryInvokeNoArgMethod(source, new[] { "Delete" }, out _, out var failureReason))
                    return new PlcSourceOperationResult(false, false, failureReason ?? "External source does not expose Delete().", sourceName);
                return new PlcSourceOperationResult(true, true, $"Deleted external source '{sourceName}'.", sourceName);
            });

        public Task<PlcSourceOperationResult> ExportBlockAsync(string blockName, string outputPath) =>
            Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(blockName))
                    return new PlcSourceOperationResult(false, false, "block-name is required.");
                if (string.IsNullOrWhiteSpace(outputPath))
                    return new PlcSourceOperationResult(false, false, "output-path is required.", blockName);
                if (await _browseService.GetBlockInfoAsync(blockName).ConfigureAwait(false) == null)
                    return new PlcSourceOperationResult(false, false, $"Block '{blockName}' was not found.", blockName);
                var block = FindBlock(blockName);
                if (block == null)
                    return new PlcSourceOperationResult(false, false, $"Block '{blockName}' was not found.", blockName);

                var fullPath = Path.GetFullPath(outputPath);
                if (!Path.HasExtension(fullPath)) fullPath = Path.Combine(fullPath, blockName + ".scl");
                var sourceGroup = TiaReflectionHelper.GetExternalSourceGroup(_sessionContext.PlcSoftware);
                if (sourceGroup == null)
                    return new PlcSourceOperationResult(false, false, "PLC software does not expose ExternalSourceGroup.", blockName, fullPath);
                if (!TryGenerateSourceFile(sourceGroup, block, fullPath, out var failureReason))
                    return new PlcSourceOperationResult(false, false, failureReason ?? "ExternalSourceGroup does not expose a supported source export method.", blockName, fullPath);
                return new PlcSourceOperationResult(true, true, $"Exported block '{blockName}' as source.", blockName, fullPath);
            });

        private object? FindExternalSource(string sourceName)
        {
            var group = TiaReflectionHelper.GetExternalSourceGroup(_sessionContext.PlcSoftware);
            return group == null
                ? null
                : EnumerateRecursive(group, "ExternalSources")
                    .FirstOrDefault(source => string.Equals(GetName(source), sourceName, StringComparison.OrdinalIgnoreCase));
        }

        private object? FindBlock(string blockName)
        {
            var group = TiaReflectionHelper.GetBlockGroup(_sessionContext.PlcSoftware);
            return group == null
                ? null
                : EnumerateRecursive(group, "Blocks")
                    .FirstOrDefault(block => string.Equals(GetName(block), blockName, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<object> EnumerateRecursive(object group, string collectionName)
        {
            var items = TiaReflectionHelper.GetEnumerableProperty(group, collectionName);
            if (items != null)
                foreach (var item in items)
                    yield return item;
            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null) yield break;
            foreach (var nested in groups)
                foreach (var item in EnumerateRecursive(nested, collectionName))
                    yield return item;
        }

        private static bool TryCreateExternalSource(object sourceGroup, string sourcePath, out object? created, out string? failureReason)
        {
            created = null;
            failureReason = null;
            var sources = TiaReflectionHelper.GetProperty(sourceGroup, "ExternalSources");
            if (sources != null)
            {
                var create = sources.GetType().GetMethod("CreateFromFile", new[] { typeof(string), typeof(string) });
                if (create != null)
                {
                    try
                    {
                        created = create.Invoke(sources, new object[] { Path.GetFileNameWithoutExtension(sourcePath), Path.GetFullPath(sourcePath) });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        failureReason = FormatException(ex);
                        return false;
                    }
                }
            }
            return TryInvokeFileMethod(sourceGroup, new[] { "CreateFromFile", "Import", "Create" }, sourcePath, out created, out failureReason);
        }

        private static bool TryInvokeFileMethod(
            object target,
            IReadOnlyList<string> methodNames,
            string path,
            out object? result,
            out string? failureReason)
        {
            result = null;
            failureReason = null;
            var fileInfo = new FileInfo(path);
            foreach (var methodName in methodNames)
            {
                foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(item => item.Name == methodName))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1) continue;
                    var argument = parameters[0].ParameterType == typeof(FileInfo) ? (object)fileInfo : path;
                    if (parameters[0].ParameterType != typeof(FileInfo) && parameters[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        EnsureParentDirectory(path);
                        result = method.Invoke(target, new[] { argument });
                        return true;
                    }
                    catch (Exception ex) { failureReason = FormatException(ex); }
                }
            }
            return false;
        }

        private static bool TryGenerateSourceFile(object sourceGroup, object sourceObject, string outputPath, out string? failureReason)
        {
            failureReason = null;
            foreach (var method in sourceGroup.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(item => item.Name == "GenerateSource"))
            {
                var parameters = method.GetParameters();
                if (parameters.Length < 2 || parameters[1].ParameterType != typeof(FileInfo)) continue;
                if (!TryCreateSingleItemEnumerable(parameters[0].ParameterType, sourceObject, out var enumerable, out failureReason)) continue;
                try
                {
                    EnsureParentDirectory(outputPath);
                    method.Invoke(sourceGroup, new[] { enumerable, new FileInfo(outputPath) });
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatException(ex);
                    return false;
                }
            }
            failureReason ??= "GenerateSource(IEnumerable<IGenerateSource>, FileInfo) method was not found or the object is not source-generatable.";
            return false;
        }

        private static bool TryCreateSingleItemEnumerable(Type enumerableType, object item, out object? enumerable, out string? failureReason)
        {
            enumerable = null;
            failureReason = null;
            var itemType = enumerableType.IsGenericType ? enumerableType.GetGenericArguments().FirstOrDefault() : null;
            if (itemType == null)
            {
                failureReason = "GenerateSource first parameter is not a generic enumerable.";
                return false;
            }
            if (!itemType.IsAssignableFrom(item.GetType()))
            {
                failureReason = $"Object type '{item.GetType().FullName}' is not assignable to '{itemType.FullName}'.";
                return false;
            }
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            list.Add(item);
            enumerable = list;
            return true;
        }

        private static bool TryInvokeNoArgMethod(
            object target,
            IReadOnlyList<string> methodNames,
            out object? result,
            out string? failureReason)
        {
            result = null;
            failureReason = null;
            foreach (var methodName in methodNames)
            {
                var method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
                if (method == null) continue;
                try
                {
                    result = method.Invoke(target, null);
                    return true;
                }
                catch (Exception ex) { failureReason = FormatException(ex); }
            }
            return false;
        }

        private static string? GetName(object? value) =>
            value == null ? null : TiaReflectionHelper.GetProperty(value, "Name")?.ToString() ?? (value as string);

        private static string FormatException(Exception ex) =>
            ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;

        private static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        }
    }
}
