using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Centralizes V21-specific Siemens type lookup and reflection-heavy operations.
    /// This keeps version-sensitive behavior in one place instead of spreading it across services.
    /// </summary>
    internal sealed class V21OpennessAdapter
    {
        private readonly ConcurrentDictionary<string, Type?> _typeByNameCache = new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Type?> _typeByFullNameCache = new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);

        public Type? FindTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return _typeByNameCache.GetOrAdd(typeName, static (name, @this) =>
            {
                foreach (var assembly in @this.EnumerateCandidateAssemblies())
                {
                    try
                    {
                        var type = assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == name);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be inspected.
                    }
                }

                return null;
            }, this);
        }

        public Type? FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            return _typeByFullNameCache.GetOrAdd(fullName, static (name, @this) =>
            {
                foreach (var assembly in @this.EnumerateCandidateAssemblies())
                {
                    try
                    {
                        var type = assembly.GetType(name, throwOnError: false, ignoreCase: false);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be inspected.
                    }
                }

                return null;
            }, this);
        }

        public object? GetService(object engineeringObject, string typeName)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            var serviceType = FindTypeInAssembly(engineeringObject.GetType().Assembly, typeName)
                ?? FindTypeByName(typeName)
                ?? FindTypeByFullName(typeName);
            return serviceType == null ? null : GetService(engineeringObject, serviceType);
        }

        public object? GetService(object engineeringObject, Type serviceType)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));

            var getServiceMethod = engineeringObject.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "GetService" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0);

            if (getServiceMethod == null)
            {
                return null;
            }

            try
            {
                var genericMethod = getServiceMethod.MakeGenericMethod(serviceType);
                return genericMethod.Invoke(engineeringObject, null);
            }
            catch
            {
                return null;
            }
        }

        public object? GetPlcSoftware(object deviceItem)
        {
            if (deviceItem == null) throw new ArgumentNullException(nameof(deviceItem));

            var directPlcSoftware = GetService(deviceItem, "PlcSoftware");
            if (directPlcSoftware != null)
            {
                return directPlcSoftware;
            }

            var softwareContainer = GetService(deviceItem, "SoftwareContainer");
            if (softwareContainer == null)
            {
                return null;
            }

            var software = TiaReflectionHelper.GetProperty(softwareContainer, "Software");
            if (software == null)
            {
                return null;
            }

            var plcSoftwareType = FindTypeInAssembly(software.GetType().Assembly, "PlcSoftware") ?? FindTypeByName("PlcSoftware");
            if (plcSoftwareType == null)
            {
                return software;
            }

            return plcSoftwareType.IsInstanceOfType(software) ? software : null;
        }

        public bool TryGetHmiDeviceClassification(object deviceItem, out string? typeInfo, out bool isConfirmedHmi)
        {
            if (deviceItem == null) throw new ArgumentNullException(nameof(deviceItem));

            if (GetService(deviceItem, "HmiTarget") != null)
            {
                typeInfo = "HMI_Regular";
                isConfirmedHmi = true;
                return true;
            }

            if (GetService(deviceItem, "HmiSoftware") != null)
            {
                typeInfo = "HMI_Unified";
                isConfirmedHmi = true;
                return true;
            }

            var typeIdentifier = GetTypeIdentifier(deviceItem);
            if (!string.IsNullOrWhiteSpace(typeIdentifier) &&
                (typeIdentifier.Contains("Comfort", StringComparison.OrdinalIgnoreCase) ||
                 typeIdentifier.Contains("Unified", StringComparison.OrdinalIgnoreCase) ||
                 typeIdentifier.Contains("Basic", StringComparison.OrdinalIgnoreCase) ||
                 typeIdentifier.Contains("Mobile Panel", StringComparison.OrdinalIgnoreCase) ||
                 typeIdentifier.Contains(" TP", StringComparison.OrdinalIgnoreCase) ||
                 typeIdentifier.Contains(" MP", StringComparison.OrdinalIgnoreCase) ||
                 typeIdentifier.Contains("WinCC", StringComparison.OrdinalIgnoreCase)))
            {
                typeInfo = "Device type: " + typeIdentifier;
                isConfirmedHmi = false;
                return true;
            }

            var itemName = TiaReflectionHelper.GetName(deviceItem);
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                var nonNullItemName = itemName!;
                if (nonNullItemName.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    typeInfo = "Detected by name pattern";
                    isConfirmedHmi = false;
                    return true;
                }
            }

            typeInfo = null;
            isConfirmedHmi = false;
            return false;
        }

        public string? GetTypeIdentifier(object engineeringObject)
        {
            return TiaReflectionHelper.GetProperty(engineeringObject, "TypeIdentifier")?.ToString();
        }

        public string? GetName(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetName(engineeringObject);
        }

        public string? GetRuntimeTypeName(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetTypeName(engineeringObject);
        }

        public IReadOnlyList<object>? GetTagTables(object plcSoftware)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var tagTableGroup = TiaReflectionHelper.GetTagTableGroup(plcSoftware);
            return TiaReflectionHelper.GetTagTables(tagTableGroup)?.ToList();
        }

        public IReadOnlyList<object>? GetTags(object tagTable)
        {
            if (tagTable == null) throw new ArgumentNullException(nameof(tagTable));

            return TiaReflectionHelper.GetTags(tagTable)?.ToList();
        }

        public object? FindTagTable(object plcSoftware, string tagTableName)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));
            if (string.IsNullOrWhiteSpace(tagTableName)) throw new ArgumentException("Tag table name cannot be empty.", nameof(tagTableName));

            var requestedName = tagTableName!;
            return GetTagTables(plcSoftware)?.FirstOrDefault(tagTable =>
                string.Equals(GetName(tagTable), requestedName, StringComparison.OrdinalIgnoreCase));
        }

        public bool TryCreateTag(
            object tagTable,
            string tagName,
            string dataType,
            string logicalAddress,
            out object? createdTag,
            out string? failureReason)
        {
            if (tagTable == null) throw new ArgumentNullException(nameof(tagTable));
            if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("Tag name cannot be empty.", nameof(tagName));
            if (string.IsNullOrWhiteSpace(dataType)) throw new ArgumentException("Data type cannot be empty.", nameof(dataType));

            createdTag = null;
            failureReason = null;

            var tagsCollection = TiaReflectionHelper.GetProperty(tagTable, "Tags");
            if (tagsCollection == null)
            {
                failureReason = "Tags collection is null or not accessible";
                return false;
            }

            var createMethod = tagsCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(string), typeof(string) });
            if (createMethod == null)
            {
                failureReason = "Tags.Create() method not found";
                return false;
            }

            createdTag = createMethod.Invoke(tagsCollection, new object[] { tagName, dataType, logicalAddress });
            if (createdTag == null)
            {
                failureReason = "The Tags.Create() method returned null";
                return false;
            }

            return true;
        }

        public string? GetDataType(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetProperty(engineeringObject, "DataType")?.ToString();
        }

        public string? GetLogicalAddress(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            return TiaReflectionHelper.GetProperty(engineeringObject, "LogicalAddress")?.ToString();
        }

        public void SetName(object engineeringObject, string value)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (value == null) throw new ArgumentNullException(nameof(value));

            TiaReflectionHelper.SetProperty(engineeringObject, "Name", value);
        }

        public void SetLogicalAddress(object engineeringObject, string value)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (value == null) throw new ArgumentNullException(nameof(value));

            TiaReflectionHelper.SetProperty(engineeringObject, "LogicalAddress", value);
        }

        public string? GetTagCommentText(object tag, string culture = "en-US")
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (string.IsNullOrWhiteSpace(culture)) throw new ArgumentException("Culture cannot be empty.", nameof(culture));

            try
            {
                var comment = TiaReflectionHelper.GetProperty(tag, "Comment");
                if (comment == null)
                {
                    return null;
                }

                foreach (var item in EnumerateCommentItems(comment))
                {
                    if (!string.Equals(GetCommentCultureName(item), culture, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return TiaReflectionHelper.GetProperty(item, "Text")?.ToString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public bool TrySetTagComment(object tag, string commentText, string culture = "en-US")
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (commentText == null) throw new ArgumentNullException(nameof(commentText));
            if (string.IsNullOrWhiteSpace(culture)) throw new ArgumentException("Culture cannot be empty.", nameof(culture));

            try
            {
                var comment = TiaReflectionHelper.GetProperty(tag, "Comment");
                if (comment == null)
                {
                    return false;
                }

                foreach (var item in EnumerateCommentItems(comment))
                {
                    if (!string.Equals(GetCommentCultureName(item), culture, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    TiaReflectionHelper.SetProperty(item, "Text", commentText);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public IReadOnlyList<object>? FindGlobalDbs(object plcSoftware, IEnumerable<string>? targetBlockNames = null)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
            if (blockGroup == null)
            {
                return null;
            }

            var results = new List<object>();
            var nameFilter = CreateNameFilter(targetBlockNames);
            CollectRecursive(
                blockGroup,
                TiaReflectionHelper.GetBlocks,
                block =>
                {
                    if (nameFilter != null)
                    {
                        return nameFilter(GetName(block));
                    }

                    return string.Equals(GetRuntimeTypeName(block), "GlobalDB", StringComparison.Ordinal);
                },
                results);

            return results;
        }

        public IReadOnlyList<object>? FindUdts(object plcSoftware, IEnumerable<string>? targetTypeNames = null)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var typeGroup = TiaReflectionHelper.GetTypeGroup(plcSoftware);
            if (typeGroup == null)
            {
                return null;
            }

            var results = new List<object>();
            var nameFilter = CreateNameFilter(targetTypeNames);
            CollectRecursive(
                typeGroup,
                TiaReflectionHelper.GetTypes,
                type =>
                {
                    if (nameFilter != null)
                    {
                        return nameFilter(GetName(type));
                    }

                    return string.Equals(GetRuntimeTypeName(type), "PlcStruct", StringComparison.Ordinal);
                },
                results);

            return results;
        }

        public IReadOnlyList<object>? GetDbMembers(object block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            var structure = TiaReflectionHelper.GetBlockStructure(block);
            var composition = TiaReflectionHelper.GetStructureComposition(structure);
            return TiaReflectionHelper.GetCompositionMembers(composition)?.ToList();
        }

        public IReadOnlyList<object>? GetUdtMembers(object type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            var structure = TiaReflectionHelper.GetBlockStructure(type);
            return TiaReflectionHelper.GetStructureMembers(structure)?.ToList();
        }

        public IReadOnlyList<object>? GetNestedMembers(object member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));

            var nestedComposition = TiaReflectionHelper.GetStructureComposition(member);
            return TiaReflectionHelper.GetCompositionMembers(nestedComposition)?.ToList();
        }

        public V21CompileResult? Compile(object plcSoftware)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var compileService = GetService(plcSoftware, "Siemens.Engineering.Compiler.ICompilable");
            if (compileService == null)
            {
                return null;
            }

            var compileMethod = compileService.GetType().GetMethod("Compile", Type.EmptyTypes);
            if (compileMethod == null)
            {
                return null;
            }

            var rawResult = compileMethod.Invoke(compileService, null);
            if (rawResult == null)
            {
                return null;
            }

            return new V21CompileResult(
                GetIntProperty(rawResult, "ErrorCount"),
                GetIntProperty(rawResult, "WarningCount"),
                TiaReflectionHelper.GetProperty(rawResult, "State")?.ToString() ?? "Unknown",
                TiaReflectionHelper.GetProperty(rawResult, "Message")?.ToString());
        }

        public bool TryDelete(object engineeringObject)
        {
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));

            var deleteMethod = engineeringObject.GetType().GetMethod("Delete", Type.EmptyTypes);
            if (deleteMethod == null)
            {
                return false;
            }

            deleteMethod.Invoke(engineeringObject, null);
            return true;
        }

        public object? GetProgrammingLanguageValue(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            var programmingLanguageType = FindTypeByName("ProgrammingLanguage");
            if (programmingLanguageType == null || !programmingLanguageType.IsEnum)
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

            foreach (var value in Enum.GetValues(programmingLanguageType))
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
            if (engineeringObject == null) throw new ArgumentNullException(nameof(engineeringObject));
            if (string.IsNullOrWhiteSpace(exportPath)) throw new ArgumentException("Export path cannot be empty.", nameof(exportPath));

            var exportOptionsType = FindTypeInAssembly(engineeringObject.GetType().Assembly, "ExportOptions") ?? FindTypeByName("ExportOptions");
            var preferredOptionValue = exportOptionsType?.GetField(preferredOptionName)?.GetValue(null);

            if (exportOptionsType != null && preferredOptionValue != null)
            {
                var exportWithOptions = engineeringObject.GetType().GetMethod("Export", new[] { typeof(FileInfo), exportOptionsType });
                if (TryInvoke(exportWithOptions, engineeringObject, new object[] { new FileInfo(exportPath), preferredOptionValue }))
                {
                    return true;
                }
            }

            var exportWithFileInfo = engineeringObject.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
            if (TryInvoke(exportWithFileInfo, engineeringObject, new object[] { new FileInfo(exportPath) }))
            {
                return true;
            }

            var exportWithString = engineeringObject.GetType().GetMethod("Export", new[] { typeof(string) });
            return TryInvoke(exportWithString, engineeringObject, new object[] { exportPath });
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
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (fileInfo == null) throw new ArgumentNullException(nameof(fileInfo));

            result = null;
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
                catch
                {
                    // Try the next compatible overload if invocation fails.
                }
            }

            return false;
        }

        public bool TryImportFromDocuments(object collection, DirectoryInfo importDirectory, string baseName, int importMode, out object? result)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (importDirectory == null) throw new ArgumentNullException(nameof(importDirectory));
            if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("Base name cannot be empty.", nameof(baseName));

            result = null;
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
                catch
                {
                    // Try the next compatible overload if invocation fails.
                }
            }

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

        public bool IsConsistent(object engineeringObject)
        {
            var value = TiaReflectionHelper.GetProperty(engineeringObject, "IsConsistent");
            return value is not bool consistent || consistent;
        }

        private static bool TryInvoke(MethodInfo? method, object target, object[] arguments)
        {
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(target, arguments);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int GetIntProperty(object source, string propertyName)
        {
            var value = TiaReflectionHelper.GetProperty(source, propertyName);
            if (value is int intValue)
            {
                return intValue;
            }

            return int.TryParse(value?.ToString(), out var parsedValue) ? parsedValue : 0;
        }

        private static IEnumerable<object> EnumerateCommentItems(object comment)
        {
            var items = TiaReflectionHelper.GetProperty(comment, "Items") as System.Collections.IEnumerable;
            if (items == null)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        private static string? GetCommentCultureName(object commentItem)
        {
            var directCulture = TiaReflectionHelper.GetProperty(commentItem, "Culture");
            var directCultureName = TiaReflectionHelper.GetProperty(directCulture, "Name")?.ToString();
            if (!string.IsNullOrWhiteSpace(directCultureName))
            {
                return directCultureName;
            }

            var language = TiaReflectionHelper.GetProperty(commentItem, "Language");
            var languageCulture = TiaReflectionHelper.GetProperty(language, "Culture");
            return TiaReflectionHelper.GetProperty(languageCulture, "Name")?.ToString();
        }

        private static Func<string?, bool>? CreateNameFilter(IEnumerable<string>? names)
        {
            if (names == null)
            {
                return null;
            }

            var normalizedNames = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            var nameSet = new HashSet<string>(normalizedNames, StringComparer.OrdinalIgnoreCase);

            if (nameSet.Count == 0)
            {
                return null;
            }

            return candidateName => candidateName != null && nameSet.Contains(candidateName);
        }

        private static void CollectRecursive(
            object group,
            Func<object?, IEnumerable<object>?> getItems,
            Func<object, bool> shouldInclude,
            List<object> results)
        {
            var items = getItems(group);
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (shouldInclude(item))
                    {
                        results.Add(item);
                    }
                }
            }

            var nestedGroups = TiaReflectionHelper.GetGroups(group);
            if (nestedGroups == null)
            {
                return;
            }

            foreach (var nestedGroup in nestedGroups)
            {
                CollectRecursive(nestedGroup, getItems, shouldInclude, results);
            }
        }

        private IEnumerable<Assembly> EnumerateCandidateAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies.Where(IsSiemensAssembly))
            {
                yield return assembly;
            }

            foreach (var assembly in assemblies.Where(assembly => !IsSiemensAssembly(assembly)))
            {
                yield return assembly;
            }
        }

        private static bool IsSiemensAssembly(Assembly assembly)
        {
            return assembly.FullName?.StartsWith("Siemens.Engineering", StringComparison.Ordinal) == true;
        }

        private static Type? FindTypeInAssembly(Assembly assembly, string typeNameOrFullName)
        {
            try
            {
                return assembly.GetType(typeNameOrFullName, throwOnError: false, ignoreCase: false)
                    ?? assembly.GetTypes().FirstOrDefault(type =>
                        type.Name == typeNameOrFullName ||
                        string.Equals(type.FullName, typeNameOrFullName, StringComparison.Ordinal));
            }
            catch
            {
                return null;
            }
        }
    }

    internal readonly struct V21CompileResult
    {
        public V21CompileResult(int errorCount, int warningCount, string state, string? message)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            State = state;
            Message = message;
        }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public string State { get; }

        public string? Message { get; }
    }
}
