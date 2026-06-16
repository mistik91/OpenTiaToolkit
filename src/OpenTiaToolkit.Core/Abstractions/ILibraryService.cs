using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions;

/// <summary>
/// Defines TIA Portal project and global library operations.
/// </summary>
public interface ILibraryService
{
    Task<IReadOnlyList<GlobalLibraryInfoModel>> ListGlobalLibrariesAsync();

    Task<GlobalLibraryInfoModel?> OpenGlobalLibraryAsync(string path, bool readOnly = true, bool upgrade = false);

    Task<bool> CloseGlobalLibraryAsync(string libraryNameOrPath);

    Task<IReadOnlyList<LibraryMasterCopyInfo>> ListMasterCopiesAsync(string libraryScope, string? globalLibraryNameOrPath = null, string? folderPath = null, bool recursive = true);

    Task<CopyFromMasterCopyResult> CopyFromMasterCopyAsync(
        string libraryScope,
        string masterCopyPath,
        string plcName,
        string? targetGroupPath = null,
        string? globalLibraryNameOrPath = null,
        bool dryRun = true,
        bool confirm = false);

    Task<IReadOnlyList<LibraryTypeInfo>> ListLibraryTypesAsync(string libraryScope, string? globalLibraryNameOrPath = null, string? folderPath = null, bool recursive = true);

    Task<IReadOnlyList<LibraryTypeVersionInfo>> GetLibraryTypeVersionsAsync(string libraryScope, string typePath, string? globalLibraryNameOrPath = null);

    Task<LibraryUpdateCheckResult> CheckLibraryUpdatesAsync(string libraryScope, string? globalLibraryNameOrPath = null, bool includeUpToDate = false);
}
