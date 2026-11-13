using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using Siemens.Engineering.CrossReference;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrossReferenceTarget = OpenTiaToolkit.Core.Services.PlcCrossReferenceTargetInventory.Target;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Owns PLC cross-reference target discovery, V21 compatibility lookup, traversal, and result projection.
    /// </summary>
    internal sealed class PlcCrossReferenceService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21TypeServiceResolver _types;
        private readonly V21PlcTagStructureAccess _tags;
        private readonly V21EngineeringObjectAccess _objects;

        public PlcCrossReferenceService(
            EngineeringSessionContext sessionContext,
            V21TypeServiceResolver types,
            V21PlcTagStructureAccess tags,
            V21EngineeringObjectAccess objects)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        }

        public Task<PlcCrossReferenceResult> GetAsync(string name) =>
            Task.Run(() => Get(name));

        internal Func<string, Task<PlcCrossReferenceResult>> CreateLookup()
        {
            var software = _sessionContext.PlcSoftware;
            var project = _sessionContext.Project;
            var inventory = new Lazy<IReadOnlyList<CrossReferenceTarget>>(() =>
                software == null ? Array.Empty<CrossReferenceTarget>() : PlcCrossReferenceTargetInventory.Read(software, _tags, _objects));
            return name => Task.Run(() =>
            {
                if (!ReferenceEquals(software, _sessionContext.PlcSoftware) || !ReferenceEquals(project, _sessionContext.Project))
                    return new PlcCrossReferenceResult(false, name, "PLC selection changed during cross-reference analysis; start a fresh analysis.");
                return Get(name, inventory);
            });
        }

        private PlcCrossReferenceResult Get(string name, Lazy<IReadOnlyList<CrossReferenceTarget>>? inventory = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new PlcCrossReferenceResult(false, string.Empty, "name is required");
            if (_sessionContext.PlcSoftware == null)
                return new PlcCrossReferenceResult(false, name, "No PLC software is currently open");

            var all = inventory?.Value ?? PlcCrossReferenceTargetInventory.Read(_sessionContext.PlcSoftware, _tags, _objects);
            var targets = PlcCrossReferenceTargetInventory.Find(name, all).ToList();
            if (targets.Count == 0)
                return new PlcCrossReferenceResult(false, name, $"No PLC object named '{name}' was found.");

            var entries = new List<PlcCrossReferenceEntry>();
            var unsupportedTargets = new List<string>();
            string? lastFailureReason = null;
            foreach (var target in targets)
            {
                var service = GetService(target.Item);
                if (service == null)
                {
                    unsupportedTargets.Add(target.Name);
                    continue;
                }

                if (!TryInvoke(service, name, out var rawEntries, out var failureReason))
                {
                    lastFailureReason = failureReason;
                    unsupportedTargets.Add(target.Name);
                    continue;
                }

                entries.AddRange(rawEntries
                    .SelectMany(FlattenEntry)
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
        }

        private object? GetService(object engineeringObject) =>
            _types.GetService(engineeringObject, typeof(CrossReferenceService))
            ?? _types.GetService(engineeringObject, "Siemens.Engineering.CrossReference.CrossReferenceService")
            ?? _types.GetService(engineeringObject, "CrossReferenceService");

        private static bool TryInvoke(object service, string name, out IReadOnlyList<object> entries, out string? failureReason)
        {
            entries = Array.Empty<object>();
            failureReason = null;
            var serviceType = service.GetType();
            var filterType = serviceType.Assembly.GetType("Siemens.Engineering.CrossReference.CrossReferenceFilter", false, false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("Siemens.Engineering.CrossReference.CrossReferenceFilter", false, false))
                    .FirstOrDefault(type => type != null);

            if (filterType != null)
            {
                var method = serviceType.GetMethod("GetCrossReferences", new[] { filterType });
                if (method != null)
                {
                    try
                    {
                        entries = ToObjectList(method.Invoke(service, new[] { Enum.Parse(filterType, "AllObjects") }));
                        return true;
                    }
                    catch (Exception ex) { failureReason = FormatException(ex); }
                }
            }

            foreach (var method in serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var parameters = method.GetParameters();
                if (!IsLookupMethod(method) || parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;
                try
                {
                    entries = ToObjectList(method.Invoke(service, new object[] { name }));
                    return true;
                }
                catch (Exception ex) { failureReason = FormatException(ex); }
            }
            return false;
        }

        private static IEnumerable<PlcCrossReferenceEntry> FlattenEntry(object entry)
        {
            var sources = TiaReflectionHelper.GetEnumerableProperty(entry, "Sources");
            if (sources != null)
            {
                foreach (var source in sources)
                    foreach (var flattened in FlattenSource(source))
                        yield return flattened;
                yield break;
            }
            foreach (var flattened in FlattenSource(entry)) yield return flattened;
        }

        private static IEnumerable<PlcCrossReferenceEntry> FlattenSource(object source)
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
                        FormatDetail(source, reference));
                }
            }
            else
            {
                yield return new PlcCrossReferenceEntry(sourceName, sourcePath, null, sourceType, GetString(source, "Address", "Device"));
            }

            var children = TiaReflectionHelper.GetEnumerableProperty(source, "Children");
            if (children == null) yield break;
            foreach (var child in children)
                foreach (var flattened in FlattenSource(child))
                    yield return flattened;
        }

        private static string? FormatDetail(object source, object reference)
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
            return new PlcCrossReferenceEntry(
                entry.Name,
                entry.Path,
                entry.Usage,
                entry.ItemType,
                string.IsNullOrWhiteSpace(entry.Detail) ? targetDetail : entry.Detail + "; " + targetDetail);
        }

        private static bool IsLookupMethod(MethodInfo method) =>
            method.Name.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0
            || method.Name.IndexOf("Usage", StringComparison.OrdinalIgnoreCase) >= 0
            || method.Name.IndexOf("Find", StringComparison.OrdinalIgnoreCase) >= 0;

        private static IReadOnlyList<object> ToObjectList(object? value)
        {
            if (value == null) return Array.Empty<object>();
            if (value is string text) return new object[] { text };
            return value is IEnumerable enumerable
                ? enumerable.Cast<object>().Where(item => item != null).ToList()
                : new[] { value };
        }

        private static string? GetString(object? value, params string[] propertyNames)
        {
            if (value == null) return null;
            foreach (var propertyName in propertyNames)
            {
                var propertyValue = TiaReflectionHelper.GetProperty(value, propertyName);
                if (propertyValue != null) return propertyValue.ToString();
            }
            return value is string text ? text : null;
        }

        private static string FormatException(Exception ex) =>
            ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;

    }
}
