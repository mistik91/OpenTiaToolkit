using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Abstractions;

public interface IMultiuserService
{
    Task<IReadOnlyList<MultiuserServerInfo>> ListServersAsync();
    Task<MultiuserServerInfo> EnsureServerAsync(string alias, string protocol, string host, int port);
    Task<IReadOnlyList<MultiuserServerProjectInfo>> ListServerProjectsAsync(string serverAlias);
    Task<MultiuserServerProjectInfo> AddProjectToServerAsync(string serverAlias, string projectPath);
    Task<IReadOnlyList<MultiuserLocalSessionInfo>> ListLocalSessionsAsync(string serverAlias, string projectName);
    Task<MultiuserLocalSessionInfo> CreateLocalSessionAsync(string serverAlias, string projectName, string sessionName, string directory, string mode, ProjectAuthenticationOptions? authentication);
    Task<MultiuserSessionStatus> GetStatusAsync();
    Task<MultiuserSessionStatus> SaveSessionAsync();
    Task<MultiuserCommitResult> CommitSessionAsync(string comment, bool requireUpToDate, bool requireNoConflicts);
}
