using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class PlcAncillarySurfaceReader
    {
        private readonly EngineeringSessionContext _sessionContext;

        public PlcAncillarySurfaceReader(EngineeringSessionContext sessionContext)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        }

        public Task<IReadOnlyList<PlcSurfaceItem>> ListExternalSourcesAsync() =>
            Task.Run<IReadOnlyList<PlcSurfaceItem>>(() =>
                Enumerate("ExternalSourceGroup", new[] { "ExternalSources" }, "ExternalSource", "Source Files"));

        public Task<IReadOnlyList<PlcSurfaceItem>> ListWatchTablesAsync() =>
            Task.Run<IReadOnlyList<PlcSurfaceItem>>(() =>
                Enumerate("WatchAndForceTableGroup", new[] { "WatchTables", "Tables" }, "WatchTable", "Watch & Force Tables"));

        public Task<IReadOnlyList<PlcSurfaceItem>> ListTechnologyObjectsAsync() =>
            Task.Run<IReadOnlyList<PlcSurfaceItem>>(() =>
                Enumerate("TechnologicalObjectGroup", new[] { "TechnologicalObjects", "TechnologicalInstances", "TechnologyObjects" }, "TechnologyObject", "Technology Objects"));

        public Task<IReadOnlyList<PlcSurfaceItem>> ListPlcAlarmTextListsAsync() =>
            Task.Run<IReadOnlyList<PlcSurfaceItem>>(() =>
                Enumerate("PlcAlarmTextlistGroup", new[] { "TextLists", "AlarmTextLists", "PlcAlarms", "Alarms" }, "AlarmTextList", "PLC Alarms & Text Lists"));

        private List<PlcSurfaceItem> Enumerate(
            string groupProperty,
            IReadOnlyList<string> collectionNames,
            string itemType,
            string rootName)
        {
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null) return new List<PlcSurfaceItem>();
            var group = TiaReflectionHelper.GetProperty(plcSoftware, groupProperty);
            if (group == null) return new List<PlcSurfaceItem>();

            var results = new List<PlcSurfaceItem>();
            Collect(group, collectionNames, itemType, rootName, results);
            return results;
        }

        private static void Collect(
            object group,
            IReadOnlyList<string> collectionNames,
            string itemType,
            string path,
            List<PlcSurfaceItem> results)
        {
            foreach (var collectionName in collectionNames)
            {
                var items = TiaReflectionHelper.GetEnumerableProperty(group, collectionName);
                if (items == null) continue;
                foreach (var item in items)
                {
                    var name = GetString(item, "Name") ?? string.Empty;
                    results.Add(new PlcSurfaceItem(
                        name,
                        GetString(item, "Type", "ObjectType") ?? itemType,
                        string.IsNullOrEmpty(path) ? name : path + "/" + name,
                        GetString(item, "ProgrammingLanguage", "Language", "Comment", "Description")));
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null) return;
            foreach (var nestedGroup in groups)
            {
                var nestedName = GetString(nestedGroup, "Name") ?? "Group";
                Collect(nestedGroup, collectionNames, itemType, path + "/" + nestedName, results);
            }
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
    }
}
