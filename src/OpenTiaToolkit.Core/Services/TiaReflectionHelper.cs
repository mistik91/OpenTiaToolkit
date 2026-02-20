using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Helper class for accessing TIA Portal API properties via reflection.
    /// This encapsulates reflection logic for working with TIA Portal objects without direct type references.
    /// </summary>
    public static class TiaReflectionHelper
    {
        /// <summary>
        /// Gets a property value from an object via reflection.
        /// </summary>
        /// <param name="obj">The object to get the property from.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <returns>The property value, or null if not found.</returns>
        public static object? GetProperty(object obj, string propertyName)
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
        public static void SetProperty(object obj, string propertyName, object value)
        {
            if (obj == null) return;
            var property = obj.GetType().GetProperty(propertyName);
            property?.SetValue(obj, value);
        }

        /// <summary>
        /// Gets the TagTableGroup from a PlcSoftware object.
        /// </summary>
        /// <param name="plcSoftware">The PlcSoftware object.</param>
        /// <returns>The TagTableGroup object, or null if not found.</returns>
        public static object? GetTagTableGroup(object plcSoftware)
        {
            return GetProperty(plcSoftware, "TagTableGroup");
        }

        /// <summary>
        /// Gets the TagTables collection from a TagTableGroup.
        /// </summary>
        /// <param name="tagTableGroup">The TagTableGroup object.</param>
        /// <returns>The TagTables collection, or null if not found.</returns>
        public static IEnumerable<object>? GetTagTables(object tagTableGroup)
        {
            var tagTables = GetProperty(tagTableGroup, "TagTables");
            var enumerable = tagTables as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the tags collection from a tag table.
        /// </summary>
        /// <param name="tagTable">The tag table object.</param>
        /// <returns>The tags collection, or null if not found.</returns>
        public static IEnumerable<object>? GetTags(object tagTable)
        {
            var tags = GetProperty(tagTable, "Tags");
            var enumerable = tags as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the Structure property from a block (GlobalDB or UDT).
        /// </summary>
        /// <param name="block">The block object.</param>
        /// <returns>The Structure object, or null if not found.</returns>
        public static object? GetBlockStructure(object block)
        {
            return GetProperty(block, "Structure");
        }

        /// <summary>
        /// Gets the Composition from a Structure object (for nested struct support).
        /// </summary>
        /// <param name="structure">The Structure object.</param>
        /// <returns>The Composition object, or null if not found.</returns>
        public static object? GetStructureComposition(object structure)
        {
            return GetProperty(structure, "Composition");
        }

        /// <summary>
        /// Gets the members collection from a Composition object.
        /// </summary>
        /// <param name="composition">The Composition object.</param>
        /// <returns>The members collection, or null if not found.</returns>
        public static IEnumerable<object>? GetCompositionMembers(object composition)
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
        public static bool HasNestedComposition(object member)
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
        public static IEnumerable<object>? GetStructureMembers(object structure)
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
        public static object? GetBlockGroup(object plcSoftware)
        {
            return GetProperty(plcSoftware, "BlockGroup");
        }

        /// <summary>
        /// Gets the TypeGroup from a PlcSoftware object.
        /// </summary>
        /// <param name="plcSoftware">The PlcSoftware object.</param>
        /// <returns>The TypeGroup object, or null if not found.</returns>
        public static object? GetTypeGroup(object plcSoftware)
        {
            return GetProperty(plcSoftware, "TypeGroup");
        }

        /// <summary>
        /// Gets the Blocks collection from a BlockGroup.
        /// </summary>
        /// <param name="blockGroup">The BlockGroup object.</param>
        /// <returns>The Blocks collection, or null if not found.</returns>
        public static IEnumerable<object>? GetBlocks(object blockGroup)
        {
            var blocks = GetProperty(blockGroup, "Blocks");
            var enumerable = blocks as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the Types collection from a TypeGroup.
        /// </summary>
        /// <param name="typeGroup">The TypeGroup object.</param>
        /// <returns>The Types collection, or null if not found.</returns>
        public static IEnumerable<object>? GetTypes(object typeGroup)
        {
            var types = GetProperty(typeGroup, "Types");
            var enumerable = types as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the Groups collection from a BlockGroup or TypeGroup.
        /// </summary>
        /// <param name="group">The group object.</param>
        /// <returns>The Groups collection, or null if not found.</returns>
        public static IEnumerable<object>? GetGroups(object group)
        {
            var groups = GetProperty(group, "Groups");
            var enumerable = groups as System.Collections.IEnumerable;
            return enumerable?.Cast<object>();
        }

        /// <summary>
        /// Gets the Name property from an object.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>The name, or null if not found.</returns>
        public static string? GetName(object obj)
        {
            var name = GetProperty(obj, "Name");
            return name?.ToString();
        }

        /// <summary>
        /// Gets the type name of an object.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>The type name.</returns>
        public static string? GetTypeName(object obj)
        {
            return obj?.GetType().Name;
        }
    }
}
