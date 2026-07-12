using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Siemens.Engineering.CrossReference;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class PlcSurfaceService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly PlcBrowseService _browseService;
        private readonly V21OpennessAdapter _v21Adapter;
        private readonly ILogger? _logger;

        public PlcSurfaceService(
            EngineeringSessionContext sessionContext,
            PlcBrowseService browseService,
            ILogger? logger,
            V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _browseService = browseService ?? throw new ArgumentNullException(nameof(browseService));
            _logger = logger;
            _v21Adapter = v21Adapter ?? new V21OpennessAdapter();
        }

        public async Task<PlcTagExportResult> ExportTagsAsync(string culture = "en-US")
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return new PlcTagExportResult(false, "No PLC software is currently open");
                }

                var tagTables = _v21Adapter.GetTagTables(plcSoftware);
                if (tagTables == null)
                {
                    return new PlcTagExportResult(false, "PLC software does not expose accessible tag tables");
                }

                var rows = new List<PlcTagExportRow>();
                foreach (var tagTable in tagTables)
                {
                    var tagTableName = _v21Adapter.GetName(tagTable) ?? string.Empty;
                    var tags = _v21Adapter.GetTags(tagTable);
                    if (tags == null)
                    {
                        continue;
                    }

                    foreach (var tag in tags)
                    {
                        rows.Add(new PlcTagExportRow(
                            tagTableName,
                            _v21Adapter.GetName(tag) ?? string.Empty,
                            _v21Adapter.GetDataType(tag),
                            _v21Adapter.GetLogicalAddress(tag),
                            _v21Adapter.GetTagCommentText(tag, culture)));
                    }
                }

                return new PlcTagExportResult(true, $"Exported {rows.Count} PLC tag(s).", rows);
            });
        }

        public async Task<PlcCrossReferenceResult> GetCrossReferencesAsync(string name)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return new PlcCrossReferenceResult(false, string.Empty, "name is required");
                }

                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return new PlcCrossReferenceResult(false, name, "No PLC software is currently open");
                }

                var targets = FindCrossReferenceTargets(name).ToList();
                if (targets.Count == 0)
                {
                    return new PlcCrossReferenceResult(false, name, $"No PLC object named '{name}' was found.");
                }

                var entries = new List<PlcCrossReferenceEntry>();
                var unsupportedTargets = new List<string>();
                string? lastFailureReason = null;

                foreach (var target in targets)
                {
                    var service = TryGetCrossReferenceService(target.Item);
                    if (service == null)
                    {
                        unsupportedTargets.Add(target.Name);
                        continue;
                    }

                    if (!TryInvokeCrossReference(service, name, out var rawEntries, out var failureReason))
                    {
                        lastFailureReason = failureReason;
                        unsupportedTargets.Add(target.Name);
                        continue;
                    }

                    entries.AddRange(rawEntries
                        .SelectMany(entry => FlattenCrossReferenceEntry(entry))
                        .Select(entry => WithTargetDetail(entry, target)));
                }

                if (entries.Count == 0 && unsupportedTargets.Count > 0)
                {
                    return new PlcCrossReferenceResult(
                        false,
                        name,
                        lastFailureReason ?? $"CrossReference service is not available for matched PLC object(s): {string.Join(", ", unsupportedTargets.Distinct(StringComparer.OrdinalIgnoreCase))}.");
                }

                return new PlcCrossReferenceResult(true, name, $"Found {entries.Count} cross-reference(s).", entries);
            });
        }

        public async Task<IReadOnlyList<PlcSurfaceItem>> ListExternalSourcesAsync()
        {
            return await Task.Run(() => EnumerateGroupItems("ExternalSourceGroup", new[] { "ExternalSources" }, "ExternalSource", "Source Files"));
        }

        public async Task<IReadOnlyList<PlcSurfaceItem>> ListWatchTablesAsync()
        {
            return await Task.Run(() => EnumerateGroupItems("WatchAndForceTableGroup", new[] { "WatchTables", "Tables" }, "WatchTable", "Watch & Force Tables"));
        }

        public async Task<IReadOnlyList<PlcSurfaceItem>> ListTechnologyObjectsAsync()
        {
            return await Task.Run(() => EnumerateGroupItems("TechnologicalObjectGroup", new[] { "TechnologicalObjects", "TechnologicalInstances", "TechnologyObjects" }, "TechnologyObject", "Technology Objects"));
        }

        public async Task<IReadOnlyList<PlcSurfaceItem>> ListPlcAlarmTextListsAsync()
        {
            return await Task.Run(() => EnumerateGroupItems("PlcAlarmTextlistGroup", new[] { "TextLists", "AlarmTextLists", "PlcAlarms", "Alarms" }, "AlarmTextList", "PLC Alarms & Text Lists"));
        }

        public async Task<PlcSourceOperationResult> ImportSourceAsync(string sourcePath, bool dryRun = true, bool confirm = false)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return new PlcSourceOperationResult(false, false, "source-path is required.");
                }

                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return new PlcSourceOperationResult(false, false, "No PLC software is currently open.");
                }

                if (!File.Exists(sourcePath))
                {
                    return new PlcSourceOperationResult(false, false, $"Source file not found: {sourcePath}");
                }

                if (dryRun)
                {
                    return new PlcSourceOperationResult(true, false, $"Dry-run: source file '{sourcePath}' can be imported if the PLC external source group supports file import.", Path.GetFileNameWithoutExtension(sourcePath));
                }

                if (!confirm)
                {
                    return new PlcSourceOperationResult(false, false, "Importing source files mutates the PLC project. Rerun with confirm=true.");
                }

                var sourceGroup = TiaReflectionHelper.GetExternalSourceGroup(plcSoftware);
                if (sourceGroup == null)
                {
                    return new PlcSourceOperationResult(false, false, "PLC software does not expose ExternalSourceGroup.");
                }

                if (!TryCreateExternalSource(sourceGroup, sourcePath, out var created, out var failureReason))
                {
                    return new PlcSourceOperationResult(false, false, failureReason ?? "ExternalSourceGroup does not expose a supported source import method.");
                }

                return new PlcSourceOperationResult(true, true, "Source file imported.", GetName(created) ?? Path.GetFileNameWithoutExtension(sourcePath));
            });
        }

        public async Task<PlcSourceOperationResult> GenerateBlocksFromSourceAsync(string sourceName, bool dryRun = true, bool confirm = false)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    return new PlcSourceOperationResult(false, false, "source-name is required.");
                }

                var source = FindExternalSource(sourceName);
                if (source == null)
                {
                    return new PlcSourceOperationResult(false, false, $"External source '{sourceName}' was not found.");
                }

                if (dryRun)
                {
                    return new PlcSourceOperationResult(true, false, $"Dry-run: source '{sourceName}' can be generated if it exposes a generate method.", sourceName);
                }

                if (!confirm)
                {
                    return new PlcSourceOperationResult(false, false, "Generating blocks mutates the PLC project. Rerun with confirm=true.");
                }

                if (!TryInvokeNoArgMethod(source, new[] { "GenerateBlocksFromSource", "GenerateBlocks", "Generate" }, out _, out var failureReason))
                {
                    return new PlcSourceOperationResult(false, false, failureReason ?? "External source does not expose a supported block generation method.", sourceName);
                }

                return new PlcSourceOperationResult(true, true, $"Generated blocks from source '{sourceName}'.", sourceName);
            });
        }

        public async Task<PlcSourceOperationResult> DeleteExternalSourceAsync(string sourceName, bool confirm = false)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                    return new PlcSourceOperationResult(false, false, "source-name is required.");
                object? source = FindExternalSource(sourceName);
                if (source == null)
                    return new PlcSourceOperationResult(false, false, $"External source '{sourceName}' was not found.", sourceName);
                if (!confirm)
                    return new PlcSourceOperationResult(false, false, "Deleting an external source requires confirm=true.", sourceName);
                if (!TryInvokeNoArgMethod(source, new[] { "Delete" }, out _, out string? failureReason))
                    return new PlcSourceOperationResult(false, false, failureReason ?? "External source does not expose Delete().", sourceName);
                return new PlcSourceOperationResult(true, true, $"Deleted external source '{sourceName}'.", sourceName);
            });
        }

        public async Task<PlcSourceOperationResult> ExportBlockAsSourceAsync(string blockName, string outputPath)
        {
            return await Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(blockName))
                {
                    return new PlcSourceOperationResult(false, false, "block-name is required.");
                }

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return new PlcSourceOperationResult(false, false, "output-path is required.", blockName);
                }

                var blockInfo = await _browseService.GetBlockInfoAsync(blockName).ConfigureAwait(false);
                if (blockInfo == null)
                {
                    return new PlcSourceOperationResult(false, false, $"Block '{blockName}' was not found.", blockName);
                }

                var block = FindBlock(blockName);
                if (block == null)
                {
                    return new PlcSourceOperationResult(false, false, $"Block '{blockName}' was not found.", blockName);
                }

                var fullPath = Path.GetFullPath(outputPath);
                if (!Path.HasExtension(fullPath))
                {
                    fullPath = Path.Combine(fullPath, blockName + ".scl");
                }

                var sourceGroup = TiaReflectionHelper.GetExternalSourceGroup(_sessionContext.PlcSoftware);
                if (sourceGroup == null)
                {
                    return new PlcSourceOperationResult(false, false, "PLC software does not expose ExternalSourceGroup.", blockName, fullPath);
                }

                if (!TryGenerateSourceFile(sourceGroup, block, fullPath, out var failureReason))
                {
                    return new PlcSourceOperationResult(false, false, failureReason ?? "ExternalSourceGroup does not expose a supported source export method.", blockName, fullPath);
                }

                return new PlcSourceOperationResult(true, true, $"Exported block '{blockName}' as source.", blockName, fullPath);
            });
        }

        private IEnumerable<CrossReferenceTarget> FindCrossReferenceTargets(string name)
        {
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null)
            {
                yield break;
            }

            var exact = FindPotentialCrossReferenceTargets(plcSoftware)
                .Where(target => string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targets = exact.Count > 0
                ? exact
                : FindPotentialCrossReferenceTargets(plcSoftware)
                    .Where(target => target.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            foreach (var target in targets)
            {
                yield return target;
            }
        }

        private object? TryGetCrossReferenceService(object? engineeringObject)
        {
            return engineeringObject == null
                ? null
                : _v21Adapter.GetService(engineeringObject, typeof(CrossReferenceService))
                    ?? _v21Adapter.GetService(engineeringObject, "Siemens.Engineering.CrossReference.CrossReferenceService")
                    ?? _v21Adapter.GetService(engineeringObject, "CrossReferenceService");
        }

        private IEnumerable<CrossReferenceTarget> FindPotentialCrossReferenceTargets(object plcSoftware)
        {
            var tagTables = _v21Adapter.GetTagTables(plcSoftware);
            if (tagTables != null)
            {
                foreach (var tagTable in tagTables)
                {
                    var tagTableName = _v21Adapter.GetName(tagTable) ?? string.Empty;
                    var tags = _v21Adapter.GetTags(tagTable);
                    if (tags == null)
                    {
                        continue;
                    }

                    foreach (var tag in tags)
                    {
                        var tagName = _v21Adapter.GetName(tag);
                        if (!string.IsNullOrWhiteSpace(tagName))
                        {
                            yield return new CrossReferenceTarget(tagName!, "Tag", "Tag Tables/" + tagTableName + "/" + tagName, tag);
                        }
                    }
                }
            }

            foreach (var block in EnumerateRecursive(TiaReflectionHelper.GetBlockGroup(plcSoftware), new[] { "Blocks" }))
            {
                var blockName = GetName(block);
                if (!string.IsNullOrWhiteSpace(blockName))
                {
                    yield return new CrossReferenceTarget(blockName!, "Block", "Program Blocks/" + blockName, block);
                }
            }

            foreach (var type in EnumerateRecursive(TiaReflectionHelper.GetTypeGroup(plcSoftware), new[] { "Types" }))
            {
                var typeName = GetName(type);
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    yield return new CrossReferenceTarget(typeName!, "Type", "PLC Data Types/" + typeName, type);
                }
            }

            foreach (var source in EnumerateRecursive(TiaReflectionHelper.GetExternalSourceGroup(plcSoftware), new[] { "ExternalSources" }))
            {
                var sourceName = GetName(source);
                if (!string.IsNullOrWhiteSpace(sourceName))
                {
                    yield return new CrossReferenceTarget(sourceName!, "ExternalSource", "Source Files/" + sourceName, source);
                }
            }
        }

        private static bool TryInvokeCrossReference(object service, string name, out IReadOnlyList<object> entries, out string? failureReason)
        {
            entries = Array.Empty<object>();
            failureReason = null;

            var serviceType = service.GetType();
            var filterType = serviceType.Assembly.GetType("Siemens.Engineering.CrossReference.CrossReferenceFilter", throwOnError: false, ignoreCase: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("Siemens.Engineering.CrossReference.CrossReferenceFilter", throwOnError: false, ignoreCase: false))
                    .FirstOrDefault(type => type != null);

            if (filterType != null)
            {
                var method = serviceType.GetMethod("GetCrossReferences", new[] { filterType });
                if (method != null)
                {
                    try
                    {
                        var filter = Enum.Parse(filterType, "AllObjects");
                        var result = method.Invoke(service, new[] { filter });
                        entries = ToObjectList(result);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        failureReason = FormatInvocationException(ex);
                    }
                }
            }

            foreach (var method in serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsCrossReferenceLookupMethod(method))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }

                try
                {
                    var result = method.Invoke(service, new object[] { name });
                    entries = ToObjectList(result);
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                }
            }

            return false;
        }

        private static IEnumerable<PlcCrossReferenceEntry> FlattenCrossReferenceEntry(object entry)
        {
            var sources = TiaReflectionHelper.GetEnumerableProperty(entry, "Sources");
            if (sources != null)
            {
                foreach (var source in sources)
                {
                    foreach (var flattened in FlattenSourceObject(source))
                    {
                        yield return flattened;
                    }
                }

                yield break;
            }

            foreach (var flattened in FlattenSourceObject(entry))
            {
                yield return flattened;
            }
        }

        private static IEnumerable<PlcCrossReferenceEntry> FlattenSourceObject(object source)
        {
            var sourceName = GetString(source, "Name") ?? string.Empty;
            var sourcePath = GetString(source, "Path");
            var sourceType = GetString(source, "TypeName", "ItemType", "ObjectType", "Type");

            var references = TiaReflectionHelper.GetEnumerableProperty(source, "References");
            if (references != null)
            {
                foreach (var reference in references)
                {
                    yield return new PlcCrossReferenceEntry(
                        GetString(reference, "Name") ?? sourceName,
                        GetString(reference, "Path") ?? sourcePath,
                        GetString(reference, "ReferenceType", "Access"),
                        GetString(reference, "TypeName", "ItemType", "ObjectType", "Type") ?? sourceType,
                        FormatCrossReferenceDetail(source, reference));
                }
            }
            else
            {
                yield return new PlcCrossReferenceEntry(
                    sourceName,
                    sourcePath,
                    null,
                    sourceType,
                    GetString(source, "Address", "Device"));
            }

            var children = TiaReflectionHelper.GetEnumerableProperty(source, "Children");
            if (children == null)
            {
                yield break;
            }

            foreach (var child in children)
            {
                foreach (var flattened in FlattenSourceObject(child))
                {
                    yield return flattened;
                }
            }
        }

        private static string? FormatCrossReferenceDetail(object source, object reference)
        {
            var parts = new[]
            {
                "source=" + (GetString(source, "Name") ?? string.Empty),
                "sourcePath=" + (GetString(source, "Path") ?? string.Empty),
                "access=" + (GetString(reference, "Access") ?? string.Empty),
                "address=" + (GetString(reference, "Address") ?? string.Empty),
                "device=" + (GetString(reference, "Device") ?? string.Empty)
            };

            return string.Join("; ", parts.Where(part => !part.EndsWith("=", StringComparison.Ordinal)));
        }

        private static PlcCrossReferenceEntry WithTargetDetail(PlcCrossReferenceEntry entry, CrossReferenceTarget target)
        {
            var targetDetail = $"target={target.Name}; targetType={target.ItemType}; targetPath={target.Path}";
            var detail = string.IsNullOrWhiteSpace(entry.Detail)
                ? targetDetail
                : entry.Detail + "; " + targetDetail;

            return new PlcCrossReferenceEntry(
                entry.Name,
                entry.Path,
                entry.Usage,
                entry.ItemType,
                detail);
        }

        private static bool IsCrossReferenceLookupMethod(MethodInfo method)
        {
            var name = method.Name;
            return name.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Usage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Find", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<PlcSurfaceItem> EnumerateGroupItems(string groupProperty, IReadOnlyList<string> collectionNames, string itemType, string rootName)
        {
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null)
            {
                return new List<PlcSurfaceItem>();
            }

            var group = TiaReflectionHelper.GetProperty(plcSoftware, groupProperty);
            if (group == null)
            {
                return new List<PlcSurfaceItem>();
            }

            var results = new List<PlcSurfaceItem>();
            CollectGroupItems(group, collectionNames, itemType, rootName, results);
            return results;
        }

        private static void CollectGroupItems(object group, IReadOnlyList<string> collectionNames, string itemType, string path, List<PlcSurfaceItem> results)
        {
            foreach (var collectionName in collectionNames)
            {
                var items = TiaReflectionHelper.GetEnumerableProperty(group, collectionName);
                if (items == null)
                {
                    continue;
                }

                foreach (var item in items)
                {
                    var name = GetName(item) ?? string.Empty;
                    results.Add(new PlcSurfaceItem(
                        name,
                        GetString(item, "Type", "ObjectType") ?? itemType,
                        string.IsNullOrEmpty(path) ? name : path + "/" + name,
                        GetString(item, "ProgrammingLanguage", "Language", "Comment", "Description")));
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null)
            {
                return;
            }

            foreach (var nestedGroup in groups)
            {
                var nestedName = GetName(nestedGroup) ?? "Group";
                CollectGroupItems(nestedGroup, collectionNames, itemType, path + "/" + nestedName, results);
            }
        }

        private object? FindExternalSource(string sourceName)
        {
            var plcSoftware = _sessionContext.PlcSoftware;
            var sourceGroup = TiaReflectionHelper.GetExternalSourceGroup(plcSoftware);
            if (sourceGroup == null)
            {
                return null;
            }

            return EnumerateRecursive(sourceGroup, new[] { "ExternalSources" })
                .FirstOrDefault(source => string.Equals(GetName(source), sourceName, StringComparison.OrdinalIgnoreCase));
        }

        private object? FindBlock(string blockName)
        {
            var blockGroup = TiaReflectionHelper.GetBlockGroup(_sessionContext.PlcSoftware);
            if (blockGroup == null)
            {
                return null;
            }

            return EnumerateRecursive(blockGroup, new[] { "Blocks" })
                .FirstOrDefault(block => string.Equals(GetName(block), blockName, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<object> EnumerateRecursive(object? group, IReadOnlyList<string> collectionNames)
        {
            if (group == null)
            {
                yield break;
            }

            foreach (var collectionName in collectionNames)
            {
                var items = TiaReflectionHelper.GetEnumerableProperty(group, collectionName);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        yield return item;
                    }
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null)
            {
                yield break;
            }

            foreach (var nestedGroup in groups)
            {
                foreach (var item in EnumerateRecursive(nestedGroup, collectionNames))
                {
                    yield return item;
                }
            }
        }

        private static bool TryInvokeFileMethod(object target, IReadOnlyList<string> methodNames, string path, out object? result, out string? failureReason)
        {
            result = null;
            failureReason = null;

            var fileInfo = new FileInfo(path);
            foreach (var methodName in methodNames)
            {
                foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(method => method.Name == methodName))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1)
                    {
                        continue;
                    }

                    var argument = parameters[0].ParameterType == typeof(FileInfo) ? (object)fileInfo : path;
                    if (parameters[0].ParameterType != typeof(FileInfo) && parameters[0].ParameterType != typeof(string))
                    {
                        continue;
                    }

                    try
                    {
                        EnsureParentDirectory(path);
                        result = method.Invoke(target, new[] { argument });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        failureReason = FormatInvocationException(ex);
                    }
                }
            }

            return false;
        }

        private static bool TryCreateExternalSource(object sourceGroup, string sourcePath, out object? created, out string? failureReason)
        {
            created = null;
            failureReason = null;

            var sources = TiaReflectionHelper.GetProperty(sourceGroup, "ExternalSources");
            if (sources != null)
            {
                var createFromFile = sources.GetType().GetMethod("CreateFromFile", new[] { typeof(string), typeof(string) });
                if (createFromFile != null)
                {
                    try
                    {
                        created = createFromFile.Invoke(
                            sources,
                            new object[] { Path.GetFileNameWithoutExtension(sourcePath), Path.GetFullPath(sourcePath) });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        failureReason = FormatInvocationException(ex);
                        return false;
                    }
                }
            }

            return TryInvokeFileMethod(sourceGroup, new[] { "CreateFromFile", "Import", "Create" }, sourcePath, out created, out failureReason);
        }

        private static bool TryGenerateSourceFile(object sourceGroup, object sourceObject, string outputPath, out string? failureReason)
        {
            failureReason = null;

            foreach (var method in sourceGroup.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(method => method.Name == "GenerateSource"))
            {
                var parameters = method.GetParameters();
                if (parameters.Length < 2 || parameters[1].ParameterType != typeof(FileInfo))
                {
                    continue;
                }

                if (!TryCreateSingleItemEnumerable(parameters[0].ParameterType, sourceObject, out var enumerable, out failureReason))
                {
                    continue;
                }

                try
                {
                    EnsureParentDirectory(outputPath);
                    method.Invoke(sourceGroup, new[] { enumerable, new FileInfo(outputPath) });
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                    return false;
                }
            }

            failureReason = failureReason ?? "GenerateSource(IEnumerable<IGenerateSource>, FileInfo) method was not found or the object is not source-generatable.";
            return false;
        }

        private static bool TryCreateSingleItemEnumerable(Type enumerableType, object item, out object? enumerable, out string? failureReason)
        {
            enumerable = null;
            failureReason = null;

            var itemType = enumerableType.IsGenericType
                ? enumerableType.GetGenericArguments().FirstOrDefault()
                : null;

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

            var listType = typeof(List<>).MakeGenericType(itemType);
            var list = (IList)Activator.CreateInstance(listType)!;
            list.Add(item);
            enumerable = list;
            return true;
        }

        private static bool TryInvokeNoArgMethod(object target, IReadOnlyList<string> methodNames, out object? result, out string? failureReason)
        {
            result = null;
            failureReason = null;

            foreach (var methodName in methodNames)
            {
                var method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
                if (method == null)
                {
                    continue;
                }

                try
                {
                    result = method.Invoke(target, null);
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = FormatInvocationException(ex);
                }
            }

            return false;
        }

        private static IReadOnlyList<object> ToObjectList(object? value)
        {
            if (value == null)
            {
                return Array.Empty<object>();
            }

            if (value is string)
            {
                return new[] { value };
            }

            if (value is IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Where(item => item != null).ToList();
            }

            return new[] { value };
        }

        private static string? GetName(object? value)
        {
            return GetString(value, "Name");
        }

        private static string? GetString(object? value, params string[] propertyNames)
        {
            if (value == null)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                var propertyValue = TiaReflectionHelper.GetProperty(value, propertyName);
                if (propertyValue != null)
                {
                    return propertyValue.ToString();
                }
            }

            return value is string text ? text : null;
        }

        private static string FormatInvocationException(Exception ex)
        {
            return ex is TargetInvocationException { InnerException: { } innerException }
                ? innerException.Message
                : ex.Message;
        }

        private static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private sealed class CrossReferenceTarget
        {
            public CrossReferenceTarget(string name, string itemType, string path, object item)
            {
                Name = name;
                ItemType = itemType;
                Path = path;
                Item = item;
            }

            public string Name { get; }

            public string ItemType { get; }

            public string Path { get; }

            public object Item { get; }
        }
    }
}
