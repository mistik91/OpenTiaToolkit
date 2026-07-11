using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Siemens.Engineering.SW;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Helper class for accessing TIA Portal API properties via reflection.
    /// This encapsulates reflection logic for working with TIA Portal objects without direct type references.
    /// </summary>
    public static class TiaReflectionHelper
    {
        static TiaReflectionHelper()
        {
            TiaPortalAssemblyResolver.Register();
        }

        /// <summary>
        /// Gets a property value from an object via reflection.
        /// </summary>
        /// <param name="obj">The object to get the property from.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <returns>The property value, or null if not found.</returns>
        public static object? GetProperty(object? obj, string propertyName)
        {
            if (obj == null) return null;
            var property = obj.GetType().GetProperty(propertyName);
            return property?.GetValue(obj);
        }

        /// <summary>
        /// Sets a property value on an object via reflection.
        /// </summary>
        /// <param name="obj">The object to set the property on.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="value">The value to set.</param>
        public static void SetProperty(object? obj, string propertyName, object value)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));
            }

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null)
            {
                throw new MissingMemberException(obj.GetType().FullName, propertyName);
            }

            if (!property.CanWrite)
            {
                throw new InvalidOperationException(
                    $"Property '{obj.GetType().FullName}.{propertyName}' is not writable.");
            }

            try
            {
                property.SetValue(obj, value);
                if (property.CanRead)
                {
                    var actual = property.GetValue(obj);
                    if (!PropertyValuesEqual(value, actual))
                    {
                        throw new InvalidOperationException(
                            $"Property '{obj.GetType().FullName}.{propertyName}' did not retain the requested value. " +
                            $"Requested '{value}', read back '{actual}'.");
                    }
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Failed to set property '{obj.GetType().FullName}.{propertyName}': {ex.InnerException.Message}",
                    ex.InnerException);
            }
        }

        /// <summary>
        /// Reads all non-empty localized text values from a Siemens multilingual object.
        /// </summary>
        public static string? GetMultilingualText(object? value)
        {
            if (value == null)
            {
                return null;
            }

            var directText = GetProperty(value, "Text")?.ToString();
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }

            var enumerable = value as IEnumerable ?? GetProperty(value, "Items") as IEnumerable;
            if (enumerable == null)
            {
                return null;
            }

            var texts = new List<string>();
            foreach (var item in enumerable)
            {
                var text = GetProperty(item, "Text")?.ToString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    !texts.Contains(text!, StringComparer.Ordinal))
                {
                    texts.Add(text!);
                }
            }

            return texts.Count == 0 ? null : string.Join(" | ", texts);
        }

        private static bool PropertyValuesEqual(object? expected, object? actual)
        {
            if (ReferenceEquals(expected, actual) || Equals(expected, actual))
            {
                return true;
            }

            return string.Equals(expected?.ToString(), actual?.ToString(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the TagTableGroup from a PlcSoftware object.
        /// </summary>
        /// <param name="plcSoftware">The PlcSoftware object.</param>
        /// <returns>The TagTableGroup object, or null if not found.</returns>
        public static object? GetTagTableGroup(object? plcSoftware)
        {
            if (plcSoftware is PlcSoftware typedPlcSoftware)
            {
                return typedPlcSoftware.TagTableGroup;
            }

            return GetProperty(plcSoftware, "TagTableGroup");
        }

        /// <summary>
        /// Gets the ExternalSourceGroup from a PlcSoftware object.
        /// </summary>
        /// <param name="plcSoftware">The PlcSoftware object.</param>
        /// <returns>The ExternalSourceGroup object, or null if not found.</returns>
        public static object? GetExternalSourceGroup(object? plcSoftware)
        {
            if (plcSoftware is PlcSoftware typedPlcSoftware)
            {
                return typedPlcSoftware.ExternalSourceGroup;
            }

            return GetProperty(plcSoftware, "ExternalSourceGroup");
        }

        /// <summary>
        /// Gets an enumerable property from an object.
        /// </summary>
        /// <param name="obj">The source object.</param>
        /// <param name="propertyName">The enumerable property name.</param>
        /// <returns>The enumerable items, or null if the property is missing.</returns>
        public static IEnumerable<object>? GetEnumerableProperty(object? obj, string propertyName)
        {
            var value = GetProperty(obj, propertyName);
            var enumerable = value as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the TagTables collection from a TagTableGroup.
        /// </summary>
        /// <param name="tagTableGroup">The TagTableGroup object.</param>
        /// <returns>The TagTables collection, or null if not found.</returns>
        public static IEnumerable<object>? GetTagTables(object? tagTableGroup)
        {
            return GetEnumerableProperty(tagTableGroup, "TagTables");
        }

        /// <summary>
        /// Gets the tags collection from a tag table.
        /// </summary>
        /// <param name="tagTable">The tag table object.</param>
        /// <returns>The tags collection, or null if not found.</returns>
        public static IEnumerable<object>? GetTags(object? tagTable)
        {
            return GetEnumerableProperty(tagTable, "Tags");
        }

        /// <summary>
        /// Gets the external sources collection from an external source group.
        /// </summary>
        /// <param name="externalSourceGroup">The external source group object.</param>
        /// <returns>The external sources collection, or null if not found.</returns>
        public static IEnumerable<object>? GetExternalSources(object? externalSourceGroup)
        {
            return GetEnumerableProperty(externalSourceGroup, "ExternalSources");
        }

        /// <summary>
        /// Gets the documents collection from a group.
        /// </summary>
        /// <param name="group">The source group object.</param>
        /// <returns>The documents collection, or null if not found.</returns>
        public static IEnumerable<object>? GetDocuments(object? group)
        {
            return GetEnumerableProperty(group, "Documents");
        }

        /// <summary>
        /// Gets the Structure property from a block (GlobalDB or UDT).
        /// </summary>
        /// <param name="block">The block object.</param>
        /// <returns>The Structure object, or null if not found.</returns>
        public static object? GetBlockStructure(object? block)
        {
            return GetProperty(block, "Structure");
        }

        /// <summary>
        /// Gets the Composition from a Structure object (for nested struct support).
        /// </summary>
        /// <param name="structure">The Structure object.</param>
        /// <returns>The Composition object, or null if not found.</returns>
        public static object? GetStructureComposition(object? structure)
        {
            return GetProperty(structure, "Composition");
        }

        /// <summary>
        /// Gets the members collection from a Composition object.
        /// </summary>
        /// <param name="composition">The Composition object.</param>
        /// <returns>The members collection, or null if not found.</returns>
        public static IEnumerable<object>? GetCompositionMembers(object? composition)
        {
            var members = GetProperty(composition, "Members");
            if (members != null)
            {
                var membersEnumerable = members as System.Collections.IEnumerable;
                return membersEnumerable?.Cast<object>();
            }

            // If Members property doesn't exist, try to enumerate directly
            var enumerable = composition as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Checks if a member has a nested Composition property (for recursive struct traversal).
        /// </summary>
        /// <param name="member">The member object.</param>
        /// <returns>True if the member has a Composition property.</returns>
        public static bool HasNestedComposition(object? member)
        {
            if (member == null) return false;
            var compositionProperty = member.GetType().GetProperty("Composition");
            var composition = compositionProperty?.GetValue(member);
            return composition != null;
        }

        /// <summary>
        /// Gets the Members collection from a Structure object (for UDTs).
        /// </summary>
        /// <param name="structure">The Structure object.</param>
        /// <returns>The Members collection, or null if not found.</returns>
        public static IEnumerable<object>? GetStructureMembers(object? structure)
        {
            var members = GetProperty(structure, "Members");
            var enumerable = members as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the BlockGroup from a PlcSoftware object.
        /// </summary>
        /// <param name="plcSoftware">The PlcSoftware object.</param>
        /// <returns>The BlockGroup object, or null if not found.</returns>
        public static object? GetBlockGroup(object? plcSoftware)
        {
            if (plcSoftware is PlcSoftware typedPlcSoftware)
            {
                return typedPlcSoftware.BlockGroup;
            }

            return GetProperty(plcSoftware, "BlockGroup");
        }

        /// <summary>
        /// Gets the TypeGroup from a PlcSoftware object.
        /// </summary>
        /// <param name="plcSoftware">The PlcSoftware object.</param>
        /// <returns>The TypeGroup object, or null if not found.</returns>
        public static object? GetTypeGroup(object? plcSoftware)
        {
            if (plcSoftware is PlcSoftware typedPlcSoftware)
            {
                return typedPlcSoftware.TypeGroup;
            }

            return GetProperty(plcSoftware, "TypeGroup");
        }

        /// <summary>
        /// Gets the Blocks collection from a BlockGroup.
        /// </summary>
        /// <param name="blockGroup">The BlockGroup object.</param>
        /// <returns>The Blocks collection, or null if not found.</returns>
        public static IEnumerable<object>? GetBlocks(object? blockGroup)
        {
            return GetEnumerableProperty(blockGroup, "Blocks");
        }

        /// <summary>
        /// Gets the Types collection from a TypeGroup.
        /// </summary>
        /// <param name="typeGroup">The TypeGroup object.</param>
        /// <returns>The Types collection, or null if not found.</returns>
        public static IEnumerable<object>? GetTypes(object? typeGroup)
        {
            return GetEnumerableProperty(typeGroup, "Types");
        }

        /// <summary>
        /// Gets the Groups collection from a BlockGroup or TypeGroup.
        /// </summary>
        /// <param name="group">The group object.</param>
        /// <returns>The Groups collection, or null if not found.</returns>
        public static IEnumerable<object>? GetGroups(object? group)
        {
            return GetEnumerableProperty(group, "Groups");
        }

        /// <summary>
        /// Gets the Name property from an object.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>The name, or null if not found.</returns>
        public static string? GetName(object? obj)
        {
            var name = GetProperty(obj, "Name");
            return name?.ToString();
        }

        /// <summary>
        /// Gets the type name of an object.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>The type name.</returns>
        public static string? GetTypeName(object? obj)
        {
            return obj?.GetType().Name;
        }

        /// <summary>
        /// Parses TIA Portal version string (e.g., "V21", "V22") to System.Version.
        /// </summary>
        /// <param name="versionString">The version string to parse.</param>
        /// <returns>The parsed version, or null if parsing fails.</returns>
        public static Version? ParseTiaVersion(string? versionString)
        {
            if (string.IsNullOrEmpty(versionString)) return null;
            // Extract number from "V21", "V22", etc.
            var match = Regex.Match(versionString, @"V(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int major))
                return new Version(major, 0);
            return null;
        }

        /// <summary>
        /// Navigates to a specific block group by path (e.g., "Program Blocks/Motors").
        /// </summary>
        /// <param name="blockGroup">The root BlockGroup.</param>
        /// <param name="groupPath">The path to navigate (empty or null returns root).</param>
        /// <returns>The target BlockGroup, or null if not found.</returns>
        public static object? GetBlockGroupByPath(object blockGroup, string? groupPath)
        {
            if (blockGroup == null) return null;
            if (string.IsNullOrEmpty(groupPath)) return blockGroup;

            var groupNames = groupPath!.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            object? currentGroup = blockGroup;

            foreach (var groupName in groupNames)
            {
                var groups = GetGroups(currentGroup);
                currentGroup = groups?.FirstOrDefault(g =>
                {
                    var name = GetName(g);
                    return name?.Equals(groupName, StringComparison.OrdinalIgnoreCase) ?? false;
                });

                if (currentGroup == null) return null;
            }

            return currentGroup;
        }

        /// <summary>
        /// Navigates to a specific type group by path (e.g., "Types/Subgroups").
        /// </summary>
        /// <param name="typeGroup">The root TypeGroup.</param>
        /// <param name="groupPath">The path to navigate (empty or null returns root).</param>
        /// <returns>The target TypeGroup, or null if not found.</returns>
        public static object? GetTypeGroupByPath(object typeGroup, string? groupPath)
        {
            if (typeGroup == null) return null;
            if (string.IsNullOrEmpty(groupPath)) return typeGroup;

            var groupNames = groupPath!.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            object? currentGroup = typeGroup;

            foreach (var groupName in groupNames)
            {
                var groups = GetGroups(currentGroup);
                currentGroup = groups?.FirstOrDefault(g =>
                {
                    var name = GetName(g);
                    return name?.Equals(groupName, StringComparison.OrdinalIgnoreCase) ?? false;
                });

                if (currentGroup == null) return null;
            }

            return currentGroup;
        }
    }
}
