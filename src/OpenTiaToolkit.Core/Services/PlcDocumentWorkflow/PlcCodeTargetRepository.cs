using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeTargetRepository
{
    private readonly EngineeringSessionContext _context;
    private readonly V21DocumentAccess _documents;
    private readonly V21EngineeringObjectAccess _objects;

    internal PlcCodeTargetRepository(EngineeringSessionContext context, V21OpennessAdapter? adapter = null)
    {
        _context = context;
        var facade = adapter ?? new V21OpennessAdapter();
        _documents = facade.Documents;
        _objects = facade.Objects;
    }

    internal object RequireBlockRoot() => TiaReflectionHelper.GetBlockGroup(_context.PlcSoftware) ?? throw new InvalidOperationException("PLC block root is unavailable.");
    internal object RequireTypeRoot() => TiaReflectionHelper.GetTypeGroup(_context.PlcSoftware) ?? throw new InvalidOperationException("PLC type root is unavailable.");
    internal object? ResolveBlockGroup(string path) => TiaReflectionHelper.GetBlockGroupByPath(RequireBlockRoot(), path);
    internal object? ResolveTypeGroup(string path) => TiaReflectionHelper.GetTypeGroupByPath(RequireTypeRoot(), path);

    internal IReadOnlyList<ResolvedBlock> FindBlocks(string name)
    {
        var results = new List<ResolvedBlock>();
        CollectBlocks(RequireBlockRoot(), string.Empty, name, results);
        return results;
    }

    internal IReadOnlyList<ResolvedType> FindTypes(string name)
    {
        var results = new List<ResolvedType>();
        CollectTypes(RequireTypeRoot(), string.Empty, name, results);
        return results;
    }

    internal ResolvedBlock? ResolveExact(PlcBlockIdentity identity, bool allowMissing)
    {
        IReadOnlyList<ResolvedBlock> matches = FindBlocks(identity.Name).Where(item => PlcWorkflowPrimitives.GroupEquals(item.Identity.GroupPath, identity.GroupPath) && BlockTypeEquals(item.Identity.BlockType, identity.BlockType)).ToArray();
        if (matches.Count > 1) throw Ambiguous(identity.Name, matches);
        if (matches.Count == 0) { if (allowMissing) return null; throw new InvalidOperationException($"Exact target '{identity}' was not found."); }
        if (!string.IsNullOrWhiteSpace(identity.Number) && !string.Equals(identity.Number, matches[0].Identity.Number, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Exact target '{identity}' resolved with block number '{matches[0].Identity.Number ?? "<none>"}' instead of planned number '{identity.Number}'.");
        return matches[0];
    }

    internal ResolvedType? ResolveExactType(PlcBlockIdentity identity, bool allowMissing)
    {
        IReadOnlyList<ResolvedType> matches = FindTypes(identity.Name).Where(item => PlcWorkflowPrimitives.GroupEquals(item.Identity.GroupPath, identity.GroupPath)).ToArray();
        if (matches.Count > 1) throw new InvalidOperationException($"PLC type target '{identity.Name}' is ambiguous in group '{PlcWorkflowPrimitives.DisplayGroup(identity.GroupPath)}'.");
        if (matches.Count == 0) { if (allowMissing) return null; throw new InvalidOperationException($"Exact PLC type target '{identity}' was not found."); }
        return matches[0];
    }

    internal bool ExportExact(ResolvedBlock block, string directory)
    {
        Directory.CreateDirectory(directory);
        return _documents.TryExportAsDocuments(block.EngineeringObject, new DirectoryInfo(directory), block.Identity.Name, out string? state) && string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase);
    }

    internal bool ExportExactType(ResolvedType type, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return _documents.TryExportEngineeringObject(type.EngineeringObject, path, "None", out _);
    }

    internal bool Delete(ResolvedBlock block) => _objects.TryDelete(block.EngineeringObject);
    internal bool Delete(ResolvedType type) => _objects.TryDelete(type.EngineeringObject);
    internal bool IdentityMatches(PlcBlockIdentity expected, PlcBlockIdentity actual, bool requireNumber) => PlcWorkflowPrimitives.GroupEquals(expected.GroupPath, actual.GroupPath) && BlockTypeEquals(expected.BlockType, actual.BlockType) && string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase) && (!requireNumber || string.Equals(expected.Number, actual.Number, StringComparison.OrdinalIgnoreCase));
    internal bool BlockTypeEquals(string left, string right) => string.Equals(NormalizeRuntimeBlockType(left, left), NormalizeRuntimeBlockType(right, right), StringComparison.OrdinalIgnoreCase);
    internal InvalidOperationException Ambiguous(string name, IEnumerable<ResolvedBlock> matches) => new InvalidOperationException($"Block target '{name}' is ambiguous. Supply an exact group-path and block type. Candidates: {string.Join(", ", matches.Select(item => item.Identity.ToString()))}");

    private void CollectBlocks(object group, string path, string name, List<ResolvedBlock> results)
    {
        IEnumerable<object>? blocks = TiaReflectionHelper.GetBlocks(group);
        if (blocks != null) foreach (object block in blocks) if (string.Equals(TiaReflectionHelper.GetName(block), name, StringComparison.OrdinalIgnoreCase)) results.Add(ToResolved(block, path));
        IEnumerable<object>? groups = TiaReflectionHelper.GetGroups(group);
        if (groups == null) return;
        foreach (object child in groups) CollectBlocks(child, CombinePath(path, TiaReflectionHelper.GetName(child) ?? "<unnamed>"), name, results);
    }

    private void CollectTypes(object group, string path, string name, List<ResolvedType> results)
    {
        IEnumerable<object>? types = TiaReflectionHelper.GetTypes(group);
        if (types != null) foreach (object type in types) if (string.Equals(TiaReflectionHelper.GetName(type), name, StringComparison.OrdinalIgnoreCase)) results.Add(new ResolvedType(type, new PlcBlockIdentity { PlcName = _context.CurrentPlcName ?? string.Empty, GroupPath = path, Name = name, BlockType = "TYPE" }));
        IEnumerable<object>? groups = TiaReflectionHelper.GetGroups(group);
        if (groups == null) return;
        foreach (object child in groups) CollectTypes(child, CombinePath(path, TiaReflectionHelper.GetName(child) ?? "<unnamed>"), name, results);
    }

    private ResolvedBlock ToResolved(object block, string groupPath)
    {
        string name = TiaReflectionHelper.GetName(block) ?? string.Empty;
        string type = NormalizeRuntimeBlockType(TiaReflectionHelper.GetProperty(block, "Type")?.ToString(), block.GetType().Name);
        string? number = TiaReflectionHelper.GetProperty(block, "Number")?.ToString();
        string? language = TiaReflectionHelper.GetProperty(block, "ProgrammingLanguage")?.ToString();
        bool readOnly = (bool?)TiaReflectionHelper.GetProperty(block, "IsReadOnly") ?? false;
        var info = new BlockInfoDetail(name, type, language, number, readOnly, TiaReflectionHelper.GetMultilingualText(TiaReflectionHelper.GetProperty(block, "Comment")), TiaReflectionHelper.GetProperty(block, "Author")?.ToString(), null, null, 0, language);
        return new ResolvedBlock(block, new PlcBlockIdentity { PlcName = _context.CurrentPlcName ?? string.Empty, GroupPath = groupPath, Name = name, BlockType = type, Number = number }, info);
    }

    private static string NormalizeRuntimeBlockType(string? type, string runtimeType)
    {
        string value = (type ?? runtimeType).Replace("PlcBlock", string.Empty);
        if (value.IndexOf("Instance", StringComparison.OrdinalIgnoreCase) >= 0 && value.IndexOf("DB", StringComparison.OrdinalIgnoreCase) >= 0) return "InstanceDB";
        if (value.Equals("DB", StringComparison.OrdinalIgnoreCase) || value.IndexOf("GlobalDB", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("DataBlock", StringComparison.OrdinalIgnoreCase) >= 0) return "GlobalDB";
        if (value.IndexOf("FunctionBlock", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("FB", StringComparison.OrdinalIgnoreCase)) return "FB";
        if (value.IndexOf("Function", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("FC", StringComparison.OrdinalIgnoreCase)) return "FC";
        if (value.IndexOf("Organization", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("OB", StringComparison.OrdinalIgnoreCase)) return "OB";
        return value;
    }

    private static string CombinePath(string path, string child) => string.IsNullOrEmpty(path) ? child : path + "/" + child;

    internal sealed class ResolvedBlock
    {
        internal ResolvedBlock(object engineeringObject, PlcBlockIdentity identity, BlockInfoDetail info) { EngineeringObject = engineeringObject; Identity = identity; Info = info; }
        internal object EngineeringObject { get; }
        internal PlcBlockIdentity Identity { get; }
        internal BlockInfoDetail Info { get; }
    }

    internal sealed class ResolvedType
    {
        internal ResolvedType(object engineeringObject, PlcBlockIdentity identity) { EngineeringObject = engineeringObject; Identity = identity; }
        internal object EngineeringObject { get; }
        internal PlcBlockIdentity Identity { get; }
    }
}
