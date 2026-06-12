using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Implements project-level VCI workflows while resolving PLC objects from the selected PLC.
    /// </summary>
    public sealed class VciService : IVciService
    {
        private readonly IPortalService _portalService;
        private readonly IPlcService _plcService;
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger<VciService>? _logger;

        internal VciService(
            IPortalService portalService,
            IPlcService plcService,
            EngineeringSessionContext sessionContext,
            ILogger<VciService>? logger = null)
        {
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
        }

        public Task<IReadOnlyList<VciWorkspaceInfo>> ListWorkspacesAsync()
        {
            return Task.Run<IReadOnlyList<VciWorkspaceInfo>>(() =>
                GetVci().WorkspaceGroup.Workspaces.Select(ToWorkspaceInfo).ToList());
        }

        public Task<VciWorkspaceInfo?> GetWorkspaceAsync(string workspaceName)
        {
            return Task.Run(() =>
            {
                var workspace = FindWorkspace(workspaceName);
                return workspace == null ? null : ToWorkspaceInfo(workspace);
            });
        }

        public Task<VciWorkspaceInfo> CreateWorkspaceAsync(string workspaceName, string rootPath, string? workspaceLanguage = null)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(workspaceName)) throw new ArgumentException("Workspace name is required.", nameof(workspaceName));
                if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Workspace root path is required.", nameof(rootPath));
                if (FindWorkspace(workspaceName) != null) throw new InvalidOperationException($"VCI workspace already exists: {workspaceName}");

                Directory.CreateDirectory(rootPath);
                var composition = GetVci().WorkspaceGroup.Workspaces;
                var workspace = string.IsNullOrWhiteSpace(workspaceLanguage)
                    ? composition.Create(workspaceName, new DirectoryInfo(rootPath))
                    : composition.Create(workspaceName, new DirectoryInfo(rootPath), CultureInfo.GetCultureInfo(workspaceLanguage));
                return ToWorkspaceInfo(workspace);
            });
        }

        public Task<bool> DeleteWorkspaceAsync(string workspaceName)
        {
            return Task.Run(() =>
            {
                var workspace = FindWorkspace(workspaceName);
                if (workspace == null)
                {
                    return false;
                }

                workspace.Delete();
                return true;
            });
        }

        public Task<IReadOnlyList<VciMappingInfo>> ListMappingsAsync(string workspaceName)
        {
            return Task.Run<IReadOnlyList<VciMappingInfo>>(() =>
            {
                var workspace = RequireWorkspace(workspaceName);
                return workspace.MappedObjects.Select(mappedObject => ToMappingInfo(workspace, mappedObject)).ToList();
            });
        }

        public async Task<IReadOnlyList<string>> GetSupportedBlockFormatsAsync(
            string plcName,
            string blockName,
            string? blockType = null,
            string? groupPath = null)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run<IReadOnlyList<string>>(() =>
            {
                var target = RequireSingleBlock(blockName, blockType, groupPath);
                return GetVci().WorkspaceGroup.Workspaces
                    .SelectMany(workspace => workspace.GetSupportedFileFormats(target.EngineeringObject))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });
        }

        public async Task<VciMappingInfo> ExportBlockAsync(
            string workspaceName,
            string plcName,
            string blockName,
            string relativeDirectory,
            string? fileName,
            string fileFormat,
            string? blockType = null,
            string? groupPath = null)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run(() =>
            {
                var workspace = RequireWorkspace(workspaceName);
                var target = RequireSingleBlock(blockName, blockType, groupPath);
                EnsureSupportedFormat(workspace, target.EngineeringObject, fileFormat);
                var mappedObject = workspace.ExportObject(
                    target.EngineeringObject,
                    WorkspaceDirectory(workspace, relativeDirectory),
                    string.IsNullOrWhiteSpace(fileName) ? target.Name : fileName,
                    fileFormat);
                return ToMappingInfo(workspace, mappedObject);
            });
        }

        public async Task<VciMappingInfo> ConnectBlockAsync(
            string workspaceName,
            string plcName,
            string blockName,
            string relativeDirectory,
            string fileName,
            string fileFormat,
            string? blockType = null,
            string? groupPath = null)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run(() =>
            {
                var workspace = RequireWorkspace(workspaceName);
                var target = RequireSingleBlock(blockName, blockType, groupPath);
                EnsureSupportedFormat(workspace, target.EngineeringObject, fileFormat);
                var mappedObject = workspace.ConnectObject(
                    target.EngineeringObject,
                    WorkspaceDirectory(workspace, relativeDirectory),
                    fileName,
                    fileFormat);
                return ToMappingInfo(workspace, mappedObject);
            });
        }

        public async Task<VciSyncItemResult> GetBlockStatusAsync(
            string workspaceName,
            string plcName,
            string blockName,
            string? blockType = null,
            string? groupPath = null)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run(() =>
            {
                var workspace = RequireWorkspace(workspaceName);
                var target = RequireSingleBlock(blockName, blockType, groupPath);
                var mappedObject = workspace.MappedObjects.Find(target.EngineeringObject);
                if (mappedObject == null)
                {
                    return Skipped(target, "Object is not mapped in this workspace.");
                }

                var result = mappedObject.GetStatus();
                return Succeeded(target, result.CompareState.ToString(), "Status retrieved.");
            });
        }

        public async Task<VciSyncItemResult> SynchronizeBlockAsync(
            string workspaceName,
            string plcName,
            string blockName,
            VciSyncDirection direction,
            bool createMapping = false,
            string relativeDirectory = "",
            string? fileName = null,
            string? fileFormat = null,
            string? blockType = null,
            string? groupPath = null)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run(() =>
            {
                var workspace = RequireWorkspace(workspaceName);
                return SynchronizeTarget(
                    workspace,
                    RequireSingleBlock(blockName, blockType, groupPath),
                    direction,
                    createMapping,
                    relativeDirectory,
                    fileName,
                    fileFormat);
            });
        }

        public async Task<VciBulkSyncResult> SynchronizeBlocksAsync(
            string workspaceName,
            string plcName,
            VciSyncDirection direction,
            string? filterRegex = null,
            string[]? blockTypes = null,
            bool createMappings = false,
            string relativeDirectory = "",
            string? fileFormat = null,
            bool includeSkipped = false)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run(() =>
            {
                Regex? filter = string.IsNullOrWhiteSpace(filterRegex)
                    ? null
                    : new Regex(filterRegex, RegexOptions.IgnoreCase);
                var typeSet = blockTypes == null || blockTypes.Length == 0
                    ? null
                    : new HashSet<string>(blockTypes, StringComparer.OrdinalIgnoreCase);
                var targets = EnumerateBlockTargets()
                    .Where(target => filter == null || filter.IsMatch(target.Name))
                    .Where(target => typeSet == null || typeSet.Contains(target.Type))
                    .ToList();

                return SynchronizeTargets(RequireWorkspace(workspaceName), targets, direction, createMappings, relativeDirectory, fileFormat, includeSkipped);
            });
        }

        public async Task<VciBulkSyncResult> SynchronizePlcAsync(
            string workspaceName,
            string plcName,
            VciSyncDirection direction,
            VciPlcScope scope,
            bool createMappings = false,
            string relativeDirectory = "",
            string? fileFormat = null,
            bool includeSkipped = false)
        {
            await EnsurePlcAsync(plcName);
            return await Task.Run(() =>
            {
                var targets = EnumerateBlockTargets().ToList();
                if (scope != VciPlcScope.Blocks)
                {
                    targets.AddRange(EnumerateContainerTargets(TiaReflectionHelper.GetTypeGroup(_sessionContext.PlcSoftware), "Types", "Types"));
                }

                if (scope == VciPlcScope.AllSupported)
                {
                    targets.AddRange(EnumerateContainerTargets(TiaReflectionHelper.GetTagTableGroup(_sessionContext.PlcSoftware), "TagTables", "Tag Tables"));
                    targets.AddRange(EnumerateContainerTargets(TiaReflectionHelper.GetProperty(_sessionContext.PlcSoftware, "TechnologicalObjectGroup"), "TechnologicalObjects", "Technology Objects"));
                }

                return SynchronizeTargets(RequireWorkspace(workspaceName), targets, direction, createMappings, relativeDirectory, fileFormat, includeSkipped);
            });
        }

        private async Task EnsurePlcAsync(string plcName)
        {
            if (_plcService.IsSoftwareOpen && string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!await _plcService.OpenPlcAsync(plcName))
            {
                throw new InvalidOperationException($"Could not open PLC software: {plcName}");
            }
        }

        private VersionControlInterface GetVci()
        {
            var project = _portalService.Project ?? throw new InvalidOperationException("No project is currently open.");
            return project.GetService<VersionControlInterface>()
                ?? throw new InvalidOperationException("Version Control Interface service is not available in the current project.");
        }

        private Workspace? FindWorkspace(string workspaceName)
        {
            return GetVci().WorkspaceGroup.Workspaces.Find(workspaceName);
        }

        private Workspace RequireWorkspace(string workspaceName)
        {
            return FindWorkspace(workspaceName)
                ?? throw new InvalidOperationException($"VCI workspace was not found: {workspaceName}");
        }

        private VciTarget RequireSingleBlock(string blockName, string? blockType, string? groupPath)
        {
            var matches = EnumerateBlockTargets()
                .Where(target => string.Equals(target.Name, blockName, StringComparison.OrdinalIgnoreCase))
                .Where(target => string.IsNullOrWhiteSpace(blockType) || string.Equals(target.Type, blockType, StringComparison.OrdinalIgnoreCase))
                .Where(target => string.IsNullOrWhiteSpace(groupPath) || string.Equals(target.GroupPath, NormalizePath(groupPath), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0) throw new InvalidOperationException($"PLC block was not found: {blockName}");
            if (matches.Count > 1) throw new InvalidOperationException($"PLC block name is ambiguous; provide block-type and group-path: {blockName}");
            return matches[0];
        }

        private VciBulkSyncResult SynchronizeTargets(
            Workspace workspace,
            IReadOnlyList<VciTarget> targets,
            VciSyncDirection direction,
            bool createMappings,
            string relativeDirectory,
            string? fileFormat,
            bool includeSkipped)
        {
            var result = new VciBulkSyncResult { CandidateCount = targets.Count };
            foreach (var target in targets)
            {
                try
                {
                    var item = SynchronizeTarget(workspace, target, direction, createMappings, relativeDirectory, null, fileFormat);
                    if (item.MappingCreated) result.MappedCount++;
                    if (item.Skipped) result.SkippedCount++;
                    else if (item.Success) result.SynchronizedCount++;
                    else result.FailedCount++;
                    if (!item.Skipped || includeSkipped)
                    {
                        result.Items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add($"{target.ObjectPath}: {ex.Message}");
                    result.Items.Add(Failed(target, ex.Message));
                    _logger?.LogWarning(ex, "VCI synchronization failed for {ObjectPath}", target.ObjectPath);
                }
            }
            return result;
        }

        private VciSyncItemResult SynchronizeTarget(
            Workspace workspace,
            VciTarget target,
            VciSyncDirection direction,
            bool createMapping,
            string relativeDirectory,
            string? fileName,
            string? requestedFormat)
        {
            var mappedObject = workspace.MappedObjects.Find(target.EngineeringObject);
            var mappingCreated = false;
            if (mappedObject == null)
            {
                if (!createMapping)
                {
                    return Skipped(target, "Object is not mapped in this workspace.");
                }
                if (direction == VciSyncDirection.WorkspaceToProject)
                {
                    return Skipped(target, "Workspace-to-project synchronization requires an existing mapping created with connect-vci-block.");
                }

                var format = ChooseFormat(workspace, target.EngineeringObject, requestedFormat);
                if (format == null)
                {
                    return Skipped(target, "No VCI-supported file format is available for this object.");
                }

                mappedObject = workspace.ExportObject(target.EngineeringObject, WorkspaceDirectory(workspace, relativeDirectory), fileName ?? target.Name, format);
                mappingCreated = true;
            }

            var currentStatus = mappedObject.GetStatus();
            if (string.Equals(currentStatus.CompareState.ToString(), "Equal", StringComparison.OrdinalIgnoreCase))
            {
                var equalItem = Succeeded(target, currentStatus.CompareState.ToString(), "Already synchronized.");
                equalItem.MappingCreated = mappingCreated;
                return equalItem;
            }

            mappedObject.Synchronize(ToSiemensDirection(direction));
            var status = mappedObject.GetStatus();
            var item = Succeeded(target, status.CompareState.ToString(), "Synchronized.");
            item.MappingCreated = mappingCreated;
            return item;
        }

        private static SynchronizationMode ToSiemensDirection(VciSyncDirection direction)
        {
            return direction == VciSyncDirection.ProjectToWorkspace
                ? SynchronizationMode.ProjectToWorkspace
                : SynchronizationMode.WorkspaceToProject;
        }

        private static VciWorkspaceInfo ToWorkspaceInfo(Workspace workspace)
        {
            var comment = workspace.Comment?.Items
                .Select(item => item.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            return new VciWorkspaceInfo
            {
                Name = workspace.Name,
                RootPath = workspace.RootPath?.ToString() ?? string.Empty,
                Comment = comment,
                WorkspaceLanguage = workspace.WorkspaceLanguage?.ToString(),
                MappingCount = workspace.MappedObjects.Count
            };
        }

        private static VciMappingInfo ToMappingInfo(Workspace workspace, MappedObject mappedObject)
        {
            return new VciMappingInfo
            {
                WorkspaceName = workspace.Name,
                ObjectName = TiaReflectionHelper.GetName(mappedObject.EngineeringObject) ?? string.Empty,
                ObjectType = mappedObject.EngineeringObject?.GetType().Name ?? string.Empty,
                DirectoryPath = mappedObject.DirectoryPath?.ToString(),
                FileNameWithoutExtension = mappedObject.FileNameWithoutExtension,
                FileFormat = mappedObject.FileFormat,
                Status = mappedObject.Status?.CompareState.ToString()
            };
        }

        private static void EnsureSupportedFormat(Workspace workspace, IEngineeringObject engineeringObject, string fileFormat)
        {
            var formats = workspace.GetSupportedFileFormats(engineeringObject).ToList();
            if (!formats.Any(format => string.Equals(format, fileFormat, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"VCI file format '{fileFormat}' is not supported. Supported formats: {string.Join(", ", formats)}");
            }
        }

        private static string? ChooseFormat(Workspace workspace, IEngineeringObject engineeringObject, string? requestedFormat)
        {
            var formats = workspace.GetSupportedFileFormats(engineeringObject).ToList();
            if (!string.IsNullOrWhiteSpace(requestedFormat))
            {
                return formats.FirstOrDefault(format => string.Equals(format, requestedFormat, StringComparison.OrdinalIgnoreCase));
            }

            return formats.FirstOrDefault(format => format.IndexOf("sd", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? formats.FirstOrDefault();
        }

        private IEnumerable<VciTarget> EnumerateBlockTargets()
        {
            var group = TiaReflectionHelper.GetBlockGroup(_sessionContext.PlcSoftware);
            return EnumerateContainerTargets(group, "Blocks", "Blocks");
        }

        private static IEnumerable<VciTarget> EnumerateContainerTargets(object? group, string collectionPropertyName, string rootPath)
        {
            if (group == null) yield break;
            foreach (var item in TiaReflectionHelper.GetEnumerableProperty(group, collectionPropertyName) ?? Enumerable.Empty<object>())
            {
                if (item is IEngineeringObject engineeringObject)
                {
                    yield return new VciTarget(
                        engineeringObject,
                        TiaReflectionHelper.GetName(item) ?? item.GetType().Name,
                        InferType(item),
                        rootPath);
                }
            }

            foreach (var childGroup in TiaReflectionHelper.GetGroups(group) ?? Enumerable.Empty<object>())
            {
                var groupName = TiaReflectionHelper.GetName(childGroup) ?? "Group";
                foreach (var item in EnumerateContainerTargets(childGroup, collectionPropertyName, CombineRelativeDirectory(rootPath, groupName)))
                {
                    yield return item;
                }
            }
        }

        private static string InferType(object item)
        {
            return TiaReflectionHelper.GetProperty(item, "Type")?.ToString() ?? item.GetType().Name;
        }

        private static DirectoryInfo WorkspaceDirectory(Workspace workspace, string relativeDirectory)
        {
            if (Path.IsPathRooted(relativeDirectory))
            {
                throw new ArgumentException("VCI mapping directory must be relative to the workspace root.", nameof(relativeDirectory));
            }

            var rootPath = Path.GetFullPath(workspace.RootPath.ToString());
            var directoryPath = Path.GetFullPath(Path.Combine(rootPath, NormalizePath(relativeDirectory)));
            if (!string.Equals(directoryPath, rootPath, StringComparison.OrdinalIgnoreCase)
                && !directoryPath.StartsWith(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("VCI mapping directory must remain within the workspace root.", nameof(relativeDirectory));
            }

            Directory.CreateDirectory(directoryPath);
            return new DirectoryInfo(directoryPath);
        }

        private static string NormalizePath(string? path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim('/');
        }

        private static string CombineRelativeDirectory(string left, string right)
        {
            var first = NormalizePath(left);
            var second = NormalizePath(right);
            return string.IsNullOrEmpty(first) ? second : string.IsNullOrEmpty(second) ? first : $"{first}/{second}";
        }

        private static VciSyncItemResult Succeeded(VciTarget target, string? status, string message)
        {
            return new VciSyncItemResult
            {
                ObjectName = target.Name,
                ObjectType = target.Type,
                ObjectPath = target.ObjectPath,
                Success = true,
                Status = status,
                Message = message
            };
        }

        private static VciSyncItemResult Skipped(VciTarget target, string message)
        {
            return new VciSyncItemResult
            {
                ObjectName = target.Name,
                ObjectType = target.Type,
                ObjectPath = target.ObjectPath,
                Success = false,
                Skipped = true,
                Message = message
            };
        }

        private static VciSyncItemResult Failed(VciTarget target, string message)
        {
            return new VciSyncItemResult
            {
                ObjectName = target.Name,
                ObjectType = target.Type,
                ObjectPath = target.ObjectPath,
                Success = false,
                Message = message
            };
        }

        private sealed class VciTarget
        {
            public VciTarget(IEngineeringObject engineeringObject, string name, string type, string groupPath)
            {
                EngineeringObject = engineeringObject;
                Name = name;
                Type = type;
                GroupPath = NormalizePath(groupPath);
            }

            public IEngineeringObject EngineeringObject { get; }
            public string Name { get; }
            public string Type { get; }
            public string GroupPath { get; }
            public string ObjectPath => string.IsNullOrWhiteSpace(GroupPath) ? Name : $"{GroupPath}/{Name}";
        }
    }
}
