using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions
{
    /// <summary>
    /// Provides TIA Portal Version Control Interface workspace and synchronization operations.
    /// </summary>
    public interface IVciService
    {
        Task<IReadOnlyList<VciWorkspaceInfo>> ListWorkspacesAsync();
        Task<VciWorkspaceInfo?> GetWorkspaceAsync(string workspaceName);
        Task<VciWorkspaceInfo> CreateWorkspaceAsync(string workspaceName, string rootPath, string? workspaceLanguage = null);
        Task<bool> DeleteWorkspaceAsync(string workspaceName);
        Task<IReadOnlyList<VciMappingInfo>> ListMappingsAsync(string workspaceName);
        Task<IReadOnlyList<string>> GetSupportedBlockFormatsAsync(string plcName, string blockName, string? blockType = null, string? groupPath = null);
        Task<VciMappingInfo> ExportBlockAsync(string workspaceName, string plcName, string blockName, string relativeDirectory, string? fileName, string fileFormat, string? blockType = null, string? groupPath = null);
        Task<VciMappingInfo> ConnectBlockAsync(string workspaceName, string plcName, string blockName, string relativeDirectory, string fileName, string fileFormat, string? blockType = null, string? groupPath = null);
        Task<VciSyncItemResult> GetBlockStatusAsync(string workspaceName, string plcName, string blockName, string? blockType = null, string? groupPath = null);
        Task<VciSyncItemResult> SynchronizeBlockAsync(string workspaceName, string plcName, string blockName, VciSyncDirection direction, bool createMapping = false, string relativeDirectory = "", string? fileName = null, string? fileFormat = null, string? blockType = null, string? groupPath = null);
        Task<VciBulkSyncResult> SynchronizeBlocksAsync(string workspaceName, string plcName, VciSyncDirection direction, string? filterRegex = null, string[]? blockTypes = null, bool createMappings = false, string relativeDirectory = "", string? fileFormat = null, bool includeSkipped = false);
        Task<VciBulkSyncResult> SynchronizePlcAsync(string workspaceName, string plcName, VciSyncDirection direction, VciPlcScope scope, bool createMappings = false, string relativeDirectory = "", string? fileFormat = null, bool includeSkipped = false);
    }
}
