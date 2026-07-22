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
    internal sealed class V21TypeServiceResolver
    {
        private readonly ConcurrentDictionary<string, TypeLookupResult> _typeByNameCache = new ConcurrentDictionary<string, TypeLookupResult>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TypeLookupResult> _typeByFullNameCache = new ConcurrentDictionary<string, TypeLookupResult>(StringComparer.Ordinal);
        static V21TypeServiceResolver()
        {
            TiaPortalAssemblyResolver.Register();
        }
        public Type? FindTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (TryFindKnownSiemensType(typeName, out var knownType))
            {
                return knownType;
            }

            return _typeByNameCache.GetOrAdd(typeName, static (name, @this) =>
            {
                var matches = new List<Type>();
                foreach (var assembly in @this.EnumerateCandidateAssemblies())
                {
                    try
                    {
                        matches.AddRange(assembly.GetTypes().Where(candidate => candidate.Name == name));
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        matches.AddRange(ex.Types.Where(candidate => candidate != null && candidate.Name == name).Cast<Type>());
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be inspected.
                    }
                }

                var distinct = matches.Distinct().OrderBy(candidate => candidate.AssemblyQualifiedName, StringComparer.Ordinal).ToList();
                return new TypeLookupResult(distinct.Count == 1 ? distinct[0] : null);
            }, this).Type;
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
                            return new TypeLookupResult(type);
                        }
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be inspected.
                    }
                }

                return new TypeLookupResult(null);
            }, this).Type;
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

        private static bool TryFindKnownSiemensType(string typeName, out Type type)
        {
            switch (typeName)
            {
                case nameof(PlcSoftware):
                    type = typeof(PlcSoftware);
                    return true;
                case nameof(ProgrammingLanguage):
                    type = typeof(ProgrammingLanguage);
                    return true;
                case nameof(ExportOptions):
                    type = typeof(ExportOptions);
                    return true;
                case nameof(SoftwareContainer):
                    type = typeof(SoftwareContainer);
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private sealed class TypeLookupResult
        {
            public TypeLookupResult(Type? type) => Type = type;
            public Type? Type { get; }
        }
    }
}
