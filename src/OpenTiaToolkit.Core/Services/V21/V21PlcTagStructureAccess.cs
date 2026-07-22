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
    internal sealed class V21PlcTagStructureAccess
    {
        private readonly V21EngineeringObjectAccess _objects;
        public V21PlcTagStructureAccess(V21EngineeringObjectAccess objects)
        {
            _objects = objects;
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
                string.Equals(_objects.GetName(tagTable), requestedName, StringComparison.OrdinalIgnoreCase));
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

            try
            {
                createdTag = createMethod.Invoke(tagsCollection, new object[] { tagName, dataType, logicalAddress });
            }
            catch (TargetInvocationException ex)
            {
                failureReason = ex.InnerException?.Message ?? ex.Message;
                return false;
            }

            if (createdTag == null)
            {
                failureReason = "The Tags.Create() method returned null";
                return false;
            }

            return true;
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
                        return nameFilter(_objects.GetName(block));
                    }

                    return string.Equals(_objects.GetRuntimeTypeName(block), "GlobalDB", StringComparison.Ordinal);
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
                        return nameFilter(_objects.GetName(type));
                    }

                    return string.Equals(_objects.GetRuntimeTypeName(type), "PlcStruct", StringComparison.Ordinal);
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

        private static Func<string?, bool>? CreateNameFilter(IEnumerable<string>? names)
        {
            if (names == null) return null;
            var nameSet = new HashSet<string>(names.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
            return nameSet.Count == 0 ? null : new Func<string?, bool>(name => name != null && nameSet.Contains(name));
        }
    }
}
