using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;

namespace OpenTiaToolkit.Core.Services;

internal sealed class MultiuserService : IMultiuserService
{
    private readonly EngineeringSessionContext _context;
    public MultiuserService(EngineeringSessionContext context) => _context = context;

    public Task<IReadOnlyList<MultiuserServerInfo>> ListServersAsync() => Run(() =>
        (IReadOnlyList<MultiuserServerInfo>)RequirePortal().ProjectServers.Select(ToInfo).ToList());

    public Task<MultiuserServerInfo> EnsureServerAsync(string alias, string protocol, string host, int port) => Run(() =>
    {
        var portal = RequirePortal();
        var existing = portal.ProjectServers.FirstOrDefault(x => string.Equals(x.ServerName, alias, StringComparison.OrdinalIgnoreCase));
        var parsed = (Protocol)Enum.Parse(typeof(Protocol), protocol, true);
        var server = existing ?? portal.ProjectServers.Create(alias, parsed, host, port);
        if (existing != null && !(SameHost(existing.Host, host) && existing.Port == port))
        {
            server.SetProtocol(parsed);
            server.SetHostName(host);
            server.SetPort(port);
        }
        return ToInfo(server);
    });

    public Task<IReadOnlyList<MultiuserServerProjectInfo>> ListServerProjectsAsync(string serverAlias) => Run(() =>
        (IReadOnlyList<MultiuserServerProjectInfo>)FindServer(serverAlias).GetServerProjects()
            .Select(x => new MultiuserServerProjectInfo { ProjectName = x.ProjectName, ServerAlias = x.ServerAlias }).ToList());

    public Task<MultiuserServerProjectInfo> AddProjectToServerAsync(string serverAlias, string projectPath) => Run(() =>
    {
        var server = FindServer(serverAlias);
        server.AddProjectToServer(new FileInfo(projectPath));
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var added = server.GetServerProjects().FirstOrDefault(x => string.Equals(x.ProjectName, name, StringComparison.OrdinalIgnoreCase));
        return added == null
            ? new MultiuserServerProjectInfo { ProjectName = name, ServerAlias = serverAlias }
            : new MultiuserServerProjectInfo { ProjectName = added.ProjectName, ServerAlias = added.ServerAlias };
    });

    public Task<IReadOnlyList<MultiuserLocalSessionInfo>> ListLocalSessionsAsync(string serverAlias, string projectName) => Run(() =>
    {
        var server = FindServer(serverAlias);
        var project = FindProject(server, projectName);
        return (IReadOnlyList<MultiuserLocalSessionInfo>)server.GetLocalSessions(project).Select(ToInfo).ToList();
    });

    public Task<MultiuserLocalSessionInfo> CreateLocalSessionAsync(string serverAlias, string projectName, string sessionName, string directory, string mode, ProjectAuthenticationOptions? authentication) => Run(() =>
    {
        var portal = RequirePortal();
        EventHandler<AuthenticationEventArgs>? handler = authentication == null ? null : (_, args) =>
        {
            args.AuthenticationTypeProvider = AuthenticationTypeProvider.Credentials;
            args.UmacCredentials.SetAttribute("Name", authentication.UserName);
            args.UmacCredentials.SetAttribute("Type", ParseUserType(authentication.UserType));
            args.UmacCredentials.SetPassword(authentication.Password);
        };
        if (handler != null) portal.Authentication += handler;
        try
        {
            var server = FindServer(serverAlias);
            var info = server.CreateLocalSession(FindProject(server, projectName), sessionName, new DirectoryInfo(directory),
                (SessionCreationMode)Enum.Parse(typeof(SessionCreationMode), mode, true));
            return ToInfo(info);
        }
        finally { if (handler != null) portal.Authentication -= handler; }
    });

    public Task<MultiuserSessionStatus> GetStatusAsync() => Run(BuildStatus);

    public Task<MultiuserSessionStatus> SaveSessionAsync() => Run(() =>
    {
        var session = RequireSession();
        session.Save();
        return BuildStatus();
    });

    public Task<MultiuserCommitResult> CommitSessionAsync(string comment, bool requireUpToDate, bool requireNoConflicts) => Run(() =>
    {
        var portal = RequirePortal();
        var session = RequireSession();
        var status = BuildStatus();
        if (requireUpToDate && status.IsUpToDate != true) throw new InvalidOperationException("The local session is not up to date; commit was blocked.");
        if (requireNoConflicts && status.ConflictCount != 0) throw new InvalidOperationException($"The local session has {status.ConflictCount} conflicted marking(s); commit was blocked.");
        var sessionFile = FindSessionFile(session);
        session.Save();
        session.Close();
        _context.ClearProjectSelection();
        LocalSession? serverProject = null;
        try
        {
            serverProject = portal.LocalSessions.OpenServerProject(sessionFile);
            var revision = serverProject.CloseAndCommit(comment);
            return new MultiuserCommitResult { Revision = revision, Comment = comment };
        }
        catch
        {
            try { serverProject?.Close(); } catch { }
            throw;
        }
    });

    private MultiuserSessionStatus BuildStatus()
    {
        var session = _context.Session;
        if (session == null) return new MultiuserSessionStatus { IsMultiuser = false, IsOpen = false };
        var markings = session.MarkingService.GetMarkings();
        var rows = markings.AllMarkings.Select(x => new MultiuserMarkingInfo
        {
            ObjectName = TryName(x.MarkedObject),
            ObjectType = x.MarkedObject.GetType().FullName ?? x.MarkedObject.GetType().Name,
            State = x.MarkState.ToString()
        }).ToList();
        return new MultiuserSessionStatus { IsMultiuser = true, IsOpen = true, IsUpToDate = session.IsUptoDate(), MarkingCount = rows.Count, ConflictCount = markings.ConflictedMarkings.Count, Markings = rows };
    }

    private TiaPortal RequirePortal() => _context.Portal ?? throw new InvalidOperationException("Not connected to TIA Portal.");
    private LocalSession RequireSession() => _context.Session ?? throw new InvalidOperationException("No multiuser local session is selected.");
    private ProjectServer FindServer(string alias) => RequirePortal().ProjectServers.FirstOrDefault(x => string.Equals(x.ServerName, alias, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"Project Server connection '{alias}' was not found.");
    private static ServerProjectInfo FindProject(ProjectServer server, string name) => server.GetServerProjects().FirstOrDefault(x => string.Equals(x.ProjectName, name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"Server project '{name}' was not found.");
    private Task<T> Run<T>(Func<T> action) => _context.RunExclusiveAsync(() => Task.FromResult(action()));
    private static MultiuserServerInfo ToInfo(ProjectServer x) => new MultiuserServerInfo { Alias = x.ServerName, Host = x.Host, Port = x.Port };
    private static MultiuserLocalSessionInfo ToInfo(LocalSessionInfo x)
    {
        var path = x.ProjectFileInfo.FullName;
        if (string.IsNullOrEmpty(Path.GetExtension(path)) && Directory.Exists(path))
        {
            path = Directory.GetFiles(path, "*.als*", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? path;
        }
        return new MultiuserLocalSessionInfo { SessionId = x.SessionId, ProjectPath = path, ProjectName = Path.GetFileNameWithoutExtension(path) };
    }
    private static UmacUserType ParseUserType(string value) => string.Equals(value, "global", StringComparison.OrdinalIgnoreCase) ? UmacUserType.Global : UmacUserType.Project;
    private static bool SameHost(string left, string right) => string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || left.IndexOf(right.Trim('/'), StringComparison.OrdinalIgnoreCase) >= 0;
    private static FileInfo FindSessionFile(LocalSession session)
    {
        var projectPath = session.Project?.Path?.FullName ?? throw new InvalidOperationException("The local session project path is unavailable.");
        var projectDirectory = Directory.GetParent(projectPath)?.Parent?.FullName;
        if (string.IsNullOrWhiteSpace(projectDirectory)) throw new InvalidOperationException("Could not resolve the local session directory.");
        var path = Directory.GetFiles(projectDirectory, "*.als*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return path == null ? throw new FileNotFoundException("Could not find the .als local-session file.", projectDirectory) : new FileInfo(path);
    }
    private static string TryName(IEngineeringObject value) { try { return Convert.ToString(value.GetAttribute("Name")) ?? string.Empty; } catch { return string.Empty; } }
}
