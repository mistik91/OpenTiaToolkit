using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services;

/// <summary>
/// Provides TIA project and global library workflows.
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private readonly IPortalService _portalService;
    private readonly IPlcService _plcService;
    private readonly EngineeringSessionContext _sessionContext;
    private readonly ILogger<LibraryService>? _logger;

    internal LibraryService(
        IPortalService portalService,
        IPlcService plcService,
        EngineeringSessionContext sessionContext,
        ILogger<LibraryService>? logger = null)
    {
        _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _logger = logger;
    }

    public Task<IReadOnlyList<GlobalLibraryInfoModel>> ListGlobalLibrariesAsync()
    {
        return Task.Run<IReadOnlyList<GlobalLibraryInfoModel>>(() =>
        {
            var portal = RequirePortal();
            var results = new List<GlobalLibraryInfoModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var info in portal.GlobalLibraries.GetGlobalLibraryInfos())
            {
                var model = FromGlobalLibraryInfo(info);
                AddUnique(results, seen, model);
            }

            foreach (var library in portal.GlobalLibraries)
            {
                AddUnique(results, seen, FromGlobalLibrary(library, isOpen: true));
            }

            return results
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public Task<GlobalLibraryInfoModel?> OpenGlobalLibraryAsync(string path, bool readOnly = true, bool upgrade = false)
    {
        return Task.Run<GlobalLibraryInfoModel?>(() =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Global library path is required.", nameof(path));
            }

            var portal = RequirePortal();
            var libraryInfo = FindGlobalLibraryInfo(portal, path);
            if (libraryInfo != null)
            {
                if (upgrade)
                {
                    throw new InvalidOperationException("Open-with-upgrade is only supported for user global libraries opened by file path.");
                }

                var listedLibrary = portal.GlobalLibraries.Open(libraryInfo);
                return FromGlobalLibrary(listedLibrary, isOpen: true);
            }

            var file = new FileInfo(path);
            var library = upgrade
                ? portal.GlobalLibraries.OpenWithUpgrade(file)
                : portal.GlobalLibraries.Open(file, readOnly ? OpenMode.ReadOnly : OpenMode.ReadWrite);

            return FromGlobalLibrary(library, isOpen: true);
        });
    }

    public Task<bool> CloseGlobalLibraryAsync(string libraryNameOrPath)
    {
        return Task.Run(() =>
        {
            var library = FindOpenGlobalLibrary(libraryNameOrPath);
            if (library == null)
            {
                return false;
            }

            var closeMethod = library.GetType().GetMethod("Close", Type.EmptyTypes);
            if (closeMethod == null)
            {
                _logger?.LogInformation("Global library {Library} does not expose Close(); treating it as host-managed.", libraryNameOrPath);
                return true;
            }

            closeMethod.Invoke(library, null);
            return true;
        });
    }

    public Task<IReadOnlyList<LibraryMasterCopyInfo>> ListMasterCopiesAsync(
        string libraryScope,
        string? globalLibraryNameOrPath = null,
        string? folderPath = null,
        bool recursive = true)
    {
        return Task.Run<IReadOnlyList<LibraryMasterCopyInfo>>(() =>
        {
            var library = ResolveLibrary(libraryScope, globalLibraryNameOrPath);
            var root = GetRequiredProperty(library, "MasterCopyFolder");
            var folder = NavigateFolder(root, folderPath, "MasterCopyFolder");
            return EnumerateMasterCopies(folder, NormalizePath(folderPath), recursive).ToList();
        });
    }

    public async Task<CopyFromMasterCopyResult> CopyFromMasterCopyAsync(
        string libraryScope,
        string masterCopyPath,
        string plcName,
        string? targetGroupPath = null,
        string? globalLibraryNameOrPath = null,
        bool dryRun = true,
        bool confirm = false)
    {
        if (string.IsNullOrWhiteSpace(masterCopyPath))
        {
            throw new ArgumentException("Master copy path is required.", nameof(masterCopyPath));
        }

        if (string.IsNullOrWhiteSpace(plcName))
        {
            throw new ArgumentException("PLC name is required.", nameof(plcName));
        }

        return await Task.Run(async () =>
        {
            var result = new CopyFromMasterCopyResult
            {
                DryRun = dryRun,
                LibraryScope = NormalizeLibraryScope(libraryScope),
                GlobalLibrary = globalLibraryNameOrPath,
                MasterCopyPath = masterCopyPath,
                PlcName = plcName,
                TargetGroupPath = targetGroupPath
            };

            var library = ResolveLibrary(libraryScope, globalLibraryNameOrPath);
            var root = GetRequiredProperty(library, "MasterCopyFolder");
            var masterCopy = FindByPath(root, masterCopyPath, "MasterCopies", "MasterCopyFolder");
            if (masterCopy == null)
            {
                result.Message = $"Master copy was not found: {masterCopyPath}";
                return result;
            }

            if (!_plcService.IsSoftwareOpen || !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
            {
                await _plcService.OpenPlcAsync(plcName).ConfigureAwait(false);
            }

            if (_sessionContext.PlcSoftware == null)
            {
                result.Message = $"PLC software is not open: {plcName}";
                return result;
            }

            var blockGroup = TiaReflectionHelper.GetBlockGroup(_sessionContext.PlcSoftware);
            if (blockGroup == null)
            {
                result.Message = $"PLC block group is not available: {plcName}";
                return result;
            }

            var targetGroup = TiaReflectionHelper.GetBlockGroupByPath(blockGroup, targetGroupPath);
            if (targetGroup == null)
            {
                result.Message = $"Target block group was not found: {targetGroupPath}";
                return result;
            }

            if (dryRun)
            {
                result.Success = true;
                result.Message = $"Dry-run successful. Master copy '{masterCopyPath}' can be copied to PLC '{plcName}'.";
                return result;
            }

            if (!confirm)
            {
                result.Message = "Copying from a master copy requires confirm=true when dry-run is false.";
                return result;
            }

            var targetBlocks = GetRequiredProperty(targetGroup, "Blocks");
            var created = InvokeCreateFrom(targetBlocks, masterCopy);
            result.Success = created != null;
            result.Applied = created != null;
            result.CreatedName = TiaReflectionHelper.GetName(created);
            result.CreatedType = TiaReflectionHelper.GetTypeName(created);
            result.Message = created != null
                ? $"Copied master copy '{masterCopyPath}' to PLC '{plcName}'."
                : $"Failed to copy master copy '{masterCopyPath}' to PLC '{plcName}'.";
            return result;
        }).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<LibraryTypeInfo>> ListLibraryTypesAsync(
        string libraryScope,
        string? globalLibraryNameOrPath = null,
        string? folderPath = null,
        bool recursive = true)
    {
        return Task.Run<IReadOnlyList<LibraryTypeInfo>>(() =>
        {
            var library = ResolveLibrary(libraryScope, globalLibraryNameOrPath);
            var root = TiaReflectionHelper.GetProperty(library, "TypeFolder");
            if (root == null)
            {
                return Array.Empty<LibraryTypeInfo>();
            }

            var folder = NavigateFolder(root, folderPath, "TypeFolder");
            return EnumerateLibraryTypes(folder, NormalizePath(folderPath), recursive).ToList();
        });
    }

    public Task<IReadOnlyList<LibraryTypeVersionInfo>> GetLibraryTypeVersionsAsync(
        string libraryScope,
        string typePath,
        string? globalLibraryNameOrPath = null)
    {
        return Task.Run<IReadOnlyList<LibraryTypeVersionInfo>>(() =>
        {
            if (string.IsNullOrWhiteSpace(typePath))
            {
                throw new ArgumentException("Library type path is required.", nameof(typePath));
            }

            var library = ResolveLibrary(libraryScope, globalLibraryNameOrPath);
            var root = TiaReflectionHelper.GetProperty(library, "TypeFolder");
            if (root == null)
            {
                return Array.Empty<LibraryTypeVersionInfo>();
            }

            var type = FindByPath(root, typePath, "Types", "TypeFolder");
            if (type == null)
            {
                return Array.Empty<LibraryTypeVersionInfo>();
            }

            var normalizedTypePath = NormalizePath(typePath);
            return Enumerate(GetRequiredProperty(type, "Versions"))
                .Select(version => ToLibraryTypeVersionInfo(version, type, normalizedTypePath))
                .ToList();
        });
    }

    public Task<LibraryUpdateCheckResult> CheckLibraryUpdatesAsync(
        string libraryScope,
        string? globalLibraryNameOrPath = null,
        bool includeUpToDate = false)
    {
        return Task.Run(() =>
        {
            var project = _portalService.Project ?? throw new InvalidOperationException("No project is currently open.");
            var normalizedScope = NormalizeLibraryScope(libraryScope);
            var library = ResolveLibrary(normalizedScope, globalLibraryNameOrPath);
            var mode = CreateEnumValue(
                "Siemens.Engineering.Library.Types.UpdateCheckMode",
                includeUpToDate ? "ReportOutOfDateAndUpToDate" : "ReportOutOfDateOnly");

            var updateCheck = InvokeBestMethod(library, "UpdateCheck", new object[] { project, mode });
            var messages = FlattenUpdateMessages(TiaReflectionHelper.GetProperty(updateCheck, "Messages"), string.Empty).ToList();
            return new LibraryUpdateCheckResult
            {
                Success = true,
                LibraryScope = normalizedScope,
                GlobalLibrary = globalLibraryNameOrPath,
                IncludeUpToDate = includeUpToDate,
                Messages = messages,
                Message = $"Library update check found {messages.Count} message(s)."
            };
        });
    }

    private TiaPortal RequirePortal()
    {
        return _sessionContext.Portal ?? throw new InvalidOperationException("No TIA Portal is currently connected.");
    }

    private object ResolveLibrary(string libraryScope, string? globalLibraryNameOrPath)
    {
        var normalizedScope = NormalizeLibraryScope(libraryScope);
        if (normalizedScope == "project")
        {
            var project = _portalService.Project ?? throw new InvalidOperationException("No project is currently open.");
            return project.ProjectLibrary;
        }

        return FindOpenGlobalLibrary(globalLibraryNameOrPath)
            ?? throw new InvalidOperationException("Global library is not open. Use open-global-library first or pass a matching global-library name/path.");
    }

    private GlobalLibrary? FindOpenGlobalLibrary(string? libraryNameOrPath)
    {
        var portal = RequirePortal();
        foreach (var library in portal.GlobalLibraries)
        {
            if (string.IsNullOrWhiteSpace(libraryNameOrPath))
            {
                return library;
            }

            var path = library.Path?.FullName ?? library.Path?.ToString();
            if (string.Equals(library.Name, libraryNameOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, libraryNameOrPath, StringComparison.OrdinalIgnoreCase))
            {
                return library;
            }
        }

        return null;
    }

    private static GlobalLibraryInfo? FindGlobalLibraryInfo(TiaPortal portal, string libraryNameOrPath)
    {
        foreach (var info in portal.GlobalLibraries.GetGlobalLibraryInfos())
        {
            var path = info.Path?.FullName ?? info.Path?.ToString();
            if (string.Equals(info.Name, libraryNameOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, libraryNameOrPath, StringComparison.OrdinalIgnoreCase))
            {
                return info;
            }
        }

        return null;
    }

    private static string NormalizeLibraryScope(string libraryScope)
    {
        var normalized = (libraryScope ?? string.Empty).Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        if (normalized.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            return "project";
        }

        if (normalized.Equals("global", StringComparison.OrdinalIgnoreCase))
        {
            return "global";
        }

        throw new ArgumentException("library-scope must be project or global.");
    }

    private static object GetRequiredProperty(object source, string propertyName)
    {
        return TiaReflectionHelper.GetProperty(source, propertyName)
            ?? throw new InvalidOperationException($"{source.GetType().Name}.{propertyName} is not available.");
    }

    private static object NavigateFolder(object rootFolder, string? folderPath, string targetKind)
    {
        var current = rootFolder;
        foreach (var segment in SplitPath(folderPath))
        {
            current = Enumerate(GetRequiredProperty(current, "Folders"))
                .FirstOrDefault(folder => string.Equals(TiaReflectionHelper.GetName(folder), segment, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{targetKind} was not found: {folderPath}");
        }

        return current;
    }

    private static object? FindByPath(object rootFolder, string itemPath, string itemCollectionPropertyName, string targetKind)
    {
        var segments = SplitPath(itemPath).ToArray();
        if (segments.Length == 0)
        {
            return null;
        }

        var current = rootFolder;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            current = Enumerate(GetRequiredProperty(current, "Folders"))
                .FirstOrDefault(folder => string.Equals(TiaReflectionHelper.GetName(folder), segment, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                return null;
            }
        }

        var itemName = segments[segments.Length - 1];
        return Enumerate(GetRequiredProperty(current, itemCollectionPropertyName))
            .FirstOrDefault(item => string.Equals(TiaReflectionHelper.GetName(item), itemName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<LibraryMasterCopyInfo> EnumerateMasterCopies(object folder, string folderPath, bool recursive)
    {
        foreach (var masterCopy in Enumerate(GetRequiredProperty(folder, "MasterCopies")))
        {
            var name = TiaReflectionHelper.GetName(masterCopy) ?? string.Empty;
            yield return new LibraryMasterCopyInfo
            {
                Name = name,
                FolderPath = folderPath,
                Path = CombinePath(folderPath, name),
                Author = TiaReflectionHelper.GetProperty(masterCopy, "Author")?.ToString(),
                CreationDate = TiaReflectionHelper.GetProperty(masterCopy, "CreationDate") as DateTime?,
                ObjectType = TiaReflectionHelper.GetTypeName(masterCopy)
            };
        }

        if (!recursive)
        {
            yield break;
        }

        foreach (var childFolder in Enumerate(GetRequiredProperty(folder, "Folders")))
        {
            var childName = TiaReflectionHelper.GetName(childFolder) ?? string.Empty;
            var childPath = CombinePath(folderPath, childName);
            foreach (var item in EnumerateMasterCopies(childFolder, childPath, recursive))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<LibraryTypeInfo> EnumerateLibraryTypes(object folder, string folderPath, bool recursive)
    {
        foreach (var libraryType in Enumerate(GetRequiredProperty(folder, "Types")))
        {
            var name = TiaReflectionHelper.GetName(libraryType) ?? string.Empty;
            yield return new LibraryTypeInfo
            {
                Name = name,
                FolderPath = folderPath,
                Path = CombinePath(folderPath, name),
                Guid = TiaReflectionHelper.GetProperty(libraryType, "Guid") as Guid?,
                Namespace = TiaReflectionHelper.GetProperty(libraryType, "Namespace")?.ToString(),
                Author = TiaReflectionHelper.GetProperty(libraryType, "Author")?.ToString(),
                DoNotUse = TiaReflectionHelper.GetProperty(libraryType, "DoNotUse") as bool?,
                SetForUpdate = TiaReflectionHelper.GetProperty(libraryType, "SetForUpdate") as bool?,
                Status = TiaReflectionHelper.GetProperty(libraryType, "Status")?.ToString(),
                VersionCount = Enumerate(TiaReflectionHelper.GetProperty(libraryType, "Versions")).Count()
            };
        }

        if (!recursive)
        {
            yield break;
        }

        foreach (var childFolder in Enumerate(GetRequiredProperty(folder, "Folders")))
        {
            var childName = TiaReflectionHelper.GetName(childFolder) ?? string.Empty;
            var childPath = CombinePath(folderPath, childName);
            foreach (var item in EnumerateLibraryTypes(childFolder, childPath, recursive))
            {
                yield return item;
            }
        }
    }

    private static LibraryTypeVersionInfo ToLibraryTypeVersionInfo(object version, object libraryType, string typePath)
    {
        return new LibraryTypeVersionInfo
        {
            TypeName = TiaReflectionHelper.GetName(libraryType) ?? string.Empty,
            TypePath = typePath,
            Guid = TiaReflectionHelper.GetProperty(version, "Guid") as Guid?,
            VersionNumber = TiaReflectionHelper.GetProperty(version, "VersionNumber")?.ToString(),
            IsDefault = TiaReflectionHelper.GetProperty(version, "IsDefault") as bool?,
            State = TiaReflectionHelper.GetProperty(version, "State")?.ToString(),
            OriginalLibrary = TiaReflectionHelper.GetProperty(version, "OriginalLibrary")?.ToString(),
            Author = TiaReflectionHelper.GetProperty(version, "Author")?.ToString(),
            ModifiedDate = TiaReflectionHelper.GetProperty(version, "ModifiedDate") as DateTime?
        };
    }

    private static IEnumerable<LibraryUpdateCheckMessage> FlattenUpdateMessages(object? messageComposition, string parentPath)
    {
        foreach (var message in Enumerate(messageComposition))
        {
            var description = TiaReflectionHelper.GetProperty(message, "Description")?.ToString();
            var path = string.IsNullOrEmpty(parentPath) ? description ?? string.Empty : $"{parentPath}/{description}";
            yield return new LibraryUpdateCheckMessage
            {
                Path = path,
                Description = description,
                MessageParts = ToStringDictionary(TiaReflectionHelper.GetProperty(message, "MessageParts")),
                Messages = FlattenUpdateMessages(TiaReflectionHelper.GetProperty(message, "Messages"), path).ToList()
            };
        }
    }

    private static IReadOnlyDictionary<string, string?> ToStringDictionary(object? dictionary)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (dictionary is IDictionary values)
        {
            foreach (DictionaryEntry entry in values)
            {
                result[entry.Key?.ToString() ?? string.Empty] = entry.Value?.ToString();
            }
        }

        return result;
    }

    private static object? InvokeCreateFrom(object targetComposition, object sourceMasterCopy)
    {
        return InvokeBestMethod(targetComposition, "CreateFrom", new[] { sourceMasterCopy });
    }

    private static object? InvokeBestMethod(object target, string methodName, object?[] args)
    {
        var method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(candidate => candidate.Name == methodName)
            .Where(candidate => candidate.GetParameters().Length == args.Length)
            .FirstOrDefault(candidate => ParametersAccept(candidate.GetParameters(), args));

        return method?.Invoke(target, args);
    }

    private static bool ParametersAccept(ParameterInfo[] parameters, object?[] args)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            var arg = args[index];
            if (arg != null && !parameters[index].ParameterType.IsInstanceOfType(arg))
            {
                return false;
            }
        }

        return true;
    }

    private static object CreateEnumValue(string typeName, string valueName)
    {
        var type = typeof(TiaPortal).Assembly.GetType(typeName, throwOnError: true)!;
        return Enum.Parse(type, valueName);
    }

    private static IEnumerable<object> Enumerate(object? source)
    {
        if (source is IEnumerable enumerable)
        {
            return enumerable.Cast<object>();
        }

        return Array.Empty<object>();
    }

    private static IEnumerable<string> SplitPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Array.Empty<string>()
            : path!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static segment => segment.Trim())
                .Where(static segment => segment.Length > 0);
    }

    private static string NormalizePath(string? path)
    {
        return string.Join("/", SplitPath(path));
    }

    private static string CombinePath(string folderPath, string name)
    {
        return string.IsNullOrWhiteSpace(folderPath) ? name : $"{folderPath}/{name}";
    }

    private static GlobalLibraryInfoModel FromGlobalLibraryInfo(GlobalLibraryInfo info)
    {
        return new GlobalLibraryInfoModel
        {
            Name = info.Name,
            Path = info.Path?.FullName ?? info.Path?.ToString(),
            LibraryType = info.LibraryType.ToString(),
            IsOpen = info.IsOpen,
            IsReadOnly = info.IsReadOnly
        };
    }

    private static GlobalLibraryInfoModel FromGlobalLibrary(GlobalLibrary library, bool isOpen)
    {
        return new GlobalLibraryInfoModel
        {
            Name = library.Name,
            Path = library.Path?.FullName ?? library.Path?.ToString(),
            LibraryType = library.GetType().Name,
            IsOpen = isOpen,
            IsReadOnly = library.IsReadOnly,
            IsModified = library.IsModified,
            Version = library.Version
        };
    }

    private static void AddUnique(List<GlobalLibraryInfoModel> target, HashSet<string> seen, GlobalLibraryInfoModel model)
    {
        var key = model.Path ?? model.Name ?? string.Empty;
        if (seen.Add(key))
        {
            target.Add(model);
        }
    }
}
