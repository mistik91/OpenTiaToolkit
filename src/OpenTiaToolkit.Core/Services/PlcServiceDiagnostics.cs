using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Diagnostic helper for exploring TIA Portal Openness API
    /// </summary>
    public static class PlcServiceDiagnostics
    {
        /// <summary>
        /// Lists all available methods on the blocks collection for diagnostic purposes
        /// </summary>
        public static void ListBlocksCollectionMethods(object blocksCollection, ILogger? logger)
        {
            if (blocksCollection == null)
            {
                logger?.LogError("blocksCollection is null");
                return;
            }

            var blocksCollectionType = blocksCollection.GetType();
            logger?.LogInformation("=== Blocks Collection Type ===");
            logger?.LogInformation("Full Name: {TypeName}", blocksCollectionType.FullName);
            logger?.LogInformation("Base Type: {BaseTypeName}", blocksCollectionType.BaseType?.FullName);
            logger?.LogInformation("");

            logger?.LogInformation("=== Available Methods ===");
            var methods = blocksCollectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods.OrderBy(m => m.Name))
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                logger?.LogInformation("  {MethodName}({Parameters})", method.Name, parameters);
            }
            logger?.LogInformation("");

            logger?.LogInformation("=== Available Properties ===");
            var properties = blocksCollectionType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var property in properties.OrderBy(p => p.Name))
            {
                logger?.LogInformation("  {PropertyName}: {PropertyType}", property.Name, property.PropertyType.Name);
            }
            logger?.LogInformation("");

            logger?.LogInformation("=== All Methods (Including Inherited) ===");
            var allMethods = blocksCollectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            var methodGroups = allMethods.GroupBy(m => m.Name).OrderBy(g => g.Key);
            foreach (var group in methodGroups)
            {
                foreach (var method in group)
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    logger?.LogInformation("  {MethodName}({Parameters})", method.Name, parameters);
                }
            }
        }

        /// <summary>
        /// Lists all available types in Siemens.Engineering assemblies that contain "Block" in their name
        /// </summary>
        public static void ListBlockRelatedTypes(ILogger? logger)
        {
            logger?.LogInformation("=== Block-Related Types in Siemens.Engineering Assemblies ===");

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName != null && a.FullName.StartsWith("Siemens.Engineering"));

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.Name.Contains("Block") || t.Name.Contains("Composition") || t.Name.Contains("Create"))
                        .OrderBy(t => t.Name)
                        .Take(50); // Limit output

                    foreach (var type in types)
                    {
                        logger?.LogInformation("  {TypeName} ({Namespace})", type.Name, type.Namespace);
                    }
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }
        }
    }
}
