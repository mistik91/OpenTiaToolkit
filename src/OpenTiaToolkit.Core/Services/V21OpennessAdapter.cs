using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using Siemens.Engineering.HmiUnified;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade for the focused TIA Portal V21 capabilities.
    /// New production code should depend on the narrow component it uses.
    /// </summary>
    internal sealed class V21OpennessAdapter
    {
        private readonly V21TypeServiceResolver _types;
        private readonly V21SoftwareDiscovery _software;
        private readonly V21EngineeringObjectAccess _objects;
        private readonly V21PlcTagStructureAccess _tags;
        private readonly V21CompilationAccess _compilation;
        private readonly V21OnlineVerificationAccess _online;
        private readonly V21DocumentAccess _documents;

        public V21OpennessAdapter()
        {
            _types = new V21TypeServiceResolver();
            _objects = new V21EngineeringObjectAccess();
            _software = new V21SoftwareDiscovery(_types);
            _tags = new V21PlcTagStructureAccess(_objects);
            _compilation = new V21CompilationAccess(_types);
            _online = new V21OnlineVerificationAccess(_types);
            _documents = new V21DocumentAccess(_types);
        }

        internal V21TypeServiceResolver Types => _types;
        internal V21SoftwareDiscovery Software => _software;
        internal V21EngineeringObjectAccess Objects => _objects;
        internal V21PlcTagStructureAccess Tags => _tags;
        internal V21CompilationAccess Compilation => _compilation;
        internal V21OnlineVerificationAccess Online => _online;
        internal V21DocumentAccess Documents => _documents;

        public Type? FindTypeByName(string name) => _types.FindTypeByName(name);
        public Type? FindTypeByFullName(string name) => _types.FindTypeByFullName(name);
        public object? GetService(object value, string typeName) => _types.GetService(value, typeName);
        public object? GetService(object value, Type type) => _types.GetService(value, type);
        public object? GetPlcSoftware(object item) => _software.GetPlcSoftware(item);
        public HmiSoftware? GetUnifiedHmiSoftware(object item) => _software.GetUnifiedHmiSoftware(item);
        public SafetyLoginResult LoginToSafetyOfflineProgram(object item, string password) => _software.LoginToSafetyOfflineProgram(item, password);
        public bool TryGetHmiDeviceClassification(object item, out string? typeInfo, out bool confirmed) => _software.TryGetHmiDeviceClassification(item, out typeInfo, out confirmed);
        public string? GetTypeIdentifier(object value) => _objects.GetTypeIdentifier(value);
        public string? GetName(object value) => _objects.GetName(value);
        public string? GetRuntimeTypeName(object value) => _objects.GetRuntimeTypeName(value);
        public string? GetDataType(object value) => _objects.GetDataType(value);
        public string? GetLogicalAddress(object value) => _objects.GetLogicalAddress(value);
        public void SetName(object value, string name) => _objects.SetName(value, name);
        public void SetLogicalAddress(object value, string address) => _objects.SetLogicalAddress(value, address);
        public void SetDataType(object value, string dataType) => _objects.SetDataType(value, dataType);
        public string? GetTagCommentText(object tag, string culture = "en-US") => _objects.GetTagCommentText(tag, culture);
        public bool TrySetTagComment(object tag, string text, string culture = "en-US") => _objects.TrySetTagComment(tag, text, culture);
        public bool TryDelete(object value) => _objects.TryDelete(value);
        public bool IsConsistent(object value) => _objects.IsConsistent(value);
        public IReadOnlyList<object>? GetTagTables(object software) => _tags.GetTagTables(software);
        public IReadOnlyList<object>? GetTags(object table) => _tags.GetTags(table);
        public object? FindTagTable(object software, string name) => _tags.FindTagTable(software, name);
        public bool TryCreateTag(object table, string name, string dataType, string address, out object? tag, out string? failure) => _tags.TryCreateTag(table, name, dataType, address, out tag, out failure);
        public IReadOnlyList<object>? FindGlobalDbs(object software, IEnumerable<string>? names = null) => _tags.FindGlobalDbs(software, names);
        public IReadOnlyList<object>? FindUdts(object software, IEnumerable<string>? names = null) => _tags.FindUdts(software, names);
        public IReadOnlyList<object>? GetDbMembers(object block) => _tags.GetDbMembers(block);
        public IReadOnlyList<object>? GetUdtMembers(object type) => _tags.GetUdtMembers(type);
        public IReadOnlyList<object>? GetNestedMembers(object member) => _tags.GetNestedMembers(member);
        public V21CompileResult? Compile(object software) => _compilation.Compile(software);
        public bool TryCompareToOnline(object software, out object? result, out string? failure) => _online.TryCompareToOnline(software, out result, out failure);
        public IReadOnlyList<PlcSoftwareCompareEntry> ExtractCompareEntries(object result) => _online.ExtractCompareEntries(result);
        public bool TrySelectPlcSimulationAdvanced(object provider, out string? message) => _online.TrySelectPlcSimulationAdvanced(provider, out message);
        public bool IsDownloadConfigurationReady(object provider) => _online.IsDownloadConfigurationReady(provider);
        public bool TryDownloadSoftware(object provider, bool onlyChanges, out string? state, out string? failure) => _online.TryDownloadSoftware(provider, onlyChanges, out state, out failure);
        public string GetOnlineState(object provider) => _online.GetOnlineState(provider);
        public bool TryGoOnline(object provider, out string? state, out string? failure) => _online.TryGoOnline(provider, out state, out failure);
        public bool TryGoOffline(object provider, out string? failure) => _online.TryGoOffline(provider, out failure);
        public bool TryControlCpu(object provider, string action, out string? state, out string? failure) => _online.TryControlCpu(provider, action, out state, out failure);
        public object? GetProgrammingLanguageValue(string? language) => _documents.GetProgrammingLanguageValue(language);
        public object? CreateFb(object blocks, string name, object? language) => _documents.CreateFb(blocks, name, language);
        public bool TryExportEngineeringObject(object value, string path, string option) => _documents.TryExportEngineeringObject(value, path, option);
        public bool TryExportEngineeringObject(object value, string path, string option, out string? failure) => _documents.TryExportEngineeringObject(value, path, option, out failure);
        public bool TryExportAsDocuments(object value, DirectoryInfo directory, string baseName, out string? state) => _documents.TryExportAsDocuments(value, directory, baseName, out state);
        public bool TryImport(object collection, FileInfo file, int mode, out object? result) => _documents.TryImport(collection, file, mode, out result);
        public bool TryImport(object collection, FileInfo file, int mode, out object? result, out string? failure) => _documents.TryImport(collection, file, mode, out result, out failure);
        public bool TryImportFromDocuments(object collection, DirectoryInfo directory, string baseName, int mode, out object? result) => _documents.TryImportFromDocuments(collection, directory, baseName, mode, out result);
        public bool TryImportFromDocuments(object collection, DirectoryInfo directory, string baseName, int mode, out object? result, out string? failure) => _documents.TryImportFromDocuments(collection, directory, baseName, mode, out result, out failure);
    }
}
