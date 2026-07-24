using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static OpenTiaToolkit.Core.Services.V21HmiEngineeringPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiItemTypeDescriptor
    {
        public HmiItemTypeDescriptor(
            Type itemType,
            string typeName,
            string category,
            string description,
            int defaultWidth,
            int defaultHeight,
            params string[] aliases)
            : this(itemType, typeName, category, description, defaultWidth, defaultHeight, Array.Empty<string>(), aliases)
        {
        }

        public HmiItemTypeDescriptor(
            Type itemType,
            string typeName,
            string category,
            string description,
            int defaultWidth,
            int defaultHeight,
            IReadOnlyList<string> creationPropertyNames,
            params string[] aliases)
        {
            ItemType = itemType;
            TypeName = typeName;
            Category = category;
            Description = description;
            DefaultWidth = defaultWidth;
            DefaultHeight = defaultHeight;
            CreationPropertyNames = creationPropertyNames
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Aliases = aliases
                .Concat(new[] { typeName })
                .Select(NormalizeToken)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            TypeKey = NormalizeToken(typeName);
        }

        public Type ItemType { get; }

        public string TypeName { get; }

        public string Category { get; }

        public string Description { get; }

        public int DefaultWidth { get; }

        public int DefaultHeight { get; }

        public string[] CreationPropertyNames { get; }

        public string[] Aliases { get; }

        public string TypeKey { get; }

        public HmiItemTypeInfo ToPublicInfo()
        {
            return new HmiItemTypeInfo
            {
                Type = TypeName,
                Category = Category,
                Description = Description,
                DefaultWidth = DefaultWidth,
                DefaultHeight = DefaultHeight,
                Aliases = Aliases
                    .Where(alias => !string.Equals(alias, TypeKey, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
    }
}
