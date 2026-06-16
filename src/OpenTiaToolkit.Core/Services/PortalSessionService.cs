using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Portal/session lifecycle operations extracted from PortalService.
    /// </summary>
    internal sealed class PortalSessionService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger? _logger;

        private TiaPortal? _portal
        {
            get => _sessionContext.Portal;
            set => _sessionContext.Portal = value;
        }

        private int? _attachedPortalProcessId
        {
            get => _sessionContext.AttachedPortalProcessId;
            set => _sessionContext.AttachedPortalProcessId = value;
        }

        private ProjectBase? _project
        {
            get => _sessionContext.Project;
            set => _sessionContext.Project = value;
        }

        private LocalSession? _session
        {
            get => _sessionContext.Session;
            set => _sessionContext.Session = value;
        }

        private bool _ownsCurrentProject
        {
            get => _sessionContext.OwnsCurrentProject;
            set => _sessionContext.OwnsCurrentProject = value;
        }

        private bool _ownsCurrentSession
        {
            get => _sessionContext.OwnsCurrentSession;
            set => _sessionContext.OwnsCurrentSession = value;
        }

        public string? LastError { get; private set; }

        public PortalSessionService(EngineeringSessionContext sessionContext, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
        }

        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Connecting to TIA Portal...");

                try
                {
                    _sessionContext.ClearPortalSelection();

                    var processes = TiaPortal.GetProcesses();
                    if (processes.Any())
                    {
                        var process = processes.First();
                        _portal = process.Attach();
                        _attachedPortalProcessId = process.Id;
                        TrySelectSingleOpenProjectInCurrentPortal();
                        _logger?.LogInformation("Attached to existing TIA Portal instance.");
                        return true;
                    }

                    _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                    _attachedPortalProcessId = null;
                    _logger?.LogInformation("Started new TIA Portal instance.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to connect to TIA Portal");
                    return false;
                }
            });
        }

        public async Task<IReadOnlyList<TiaPortalInstance>> GetInstancesAsync()
        {
            return await Task.Run(() =>
            {
                var instances = new List<TiaPortalInstance>();

                try
                {
                    foreach (var process in TiaPortal.GetProcesses())
                    {
                        instances.Add(new TiaPortalInstance(
                            process.Id,
                            GetProcessVersion(process),
                            _attachedPortalProcessId.HasValue && process.Id == _attachedPortalProcessId.Value));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get TIA Portal instances");
                }

                return (IReadOnlyList<TiaPortalInstance>)instances;
            });
        }

        public async Task<bool> OpenTiaPortalAsync(bool withUi = false)
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Opening new TIA Portal instance (with UI: {WithUi})...", withUi);

                try
                {
                    _portal?.Dispose();
                    _sessionContext.ClearPortalSelection();

                    var mode = withUi ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface;
                    _portal = new TiaPortal(mode);
                    _attachedPortalProcessId = null;
                    _logger?.LogInformation("Started new TIA Portal instance.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open TIA Portal");
                    _sessionContext.ClearPortalSelection();
                    return false;
                }
            });
        }

        public async Task<bool> CloseTiaPortalAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Closing TIA Portal instance...");

                try
                {
                    if (_portal == null)
                    {
                        return false;
                    }

                    _portal.Dispose();
                    _sessionContext.ClearPortalSelection();
                    _logger?.LogInformation("TIA Portal instance closed.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to close TIA Portal");
                    return false;
                }
            });
        }

        public async Task<bool> ConnectToInstanceAsync(int instanceId)
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Connecting to TIA Portal instance {InstanceId}...", instanceId);

                try
                {
                    _sessionContext.ClearPortalSelection();

                    var process = TiaPortal.GetProcesses().FirstOrDefault(candidate => candidate.Id == instanceId);
                    if (process == null)
                    {
                        _logger?.LogWarning("No TIA Portal instance found with ID: {InstanceId}", instanceId);
                        return false;
                    }

                    _portal = process.Attach();
                    _attachedPortalProcessId = process.Id;
                    TrySelectSingleOpenProjectInCurrentPortal();
                    _logger?.LogInformation("Attached to TIA Portal instance {InstanceId}.", instanceId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to connect to TIA Portal instance {InstanceId}", instanceId);
                    return false;
                }
            });
        }

        public async Task<bool> DisconnectAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Disconnecting from TIA Portal...");

                try
                {
                    _portal?.Dispose();
                    _sessionContext.ClearPortalSelection();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to disconnect from TIA Portal");
                    return false;
                }
            });
        }

        public async Task<bool> OpenProjectAsync(string projectPath)
        {
            return await OpenProjectAsync(projectPath, null);
        }

        public async Task<bool> OpenProjectAsync(string projectPath, ProjectAuthenticationOptions? authentication)
        {
            return await Task.Run(() =>
            {
                LastError = null;
                if (_portal == null)
                {
                    LastError = "Not connected to TIA Portal";
                    _logger?.LogWarning("{LastError}", LastError);
                    return false;
                }

                _logger?.LogInformation("Opening project: {ProjectPath}", projectPath);

                try
                {
                    var fileInfo = new FileInfo(projectPath);
                    var normalizedProjectPath = NormalizeProjectPath(projectPath);
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);

                    if (TrySelectCurrentProject(normalizedProjectPath, projectName))
                    {
                        _logger?.LogInformation("Project is already selected: {ProjectPath}", projectPath);
                        return true;
                    }

                    if (IsLocalSessionFile(projectPath))
                    {
                        _sessionContext.ClearProjectSelection();
                        _session = _portal.LocalSessions.Open(fileInfo);
                        _project = _session?.Project;
                        _ownsCurrentSession = _session != null;
                        _ownsCurrentProject = _project != null;
                        if (_project == null)
                        {
                            LastError = $"Opened local session file '{projectPath}', but Siemens Openness did not return a project.";
                        }
                        return _project != null;
                    }

                    if (TrySelectOpenProjectInCurrentPortal(normalizedProjectPath, projectName))
                    {
                        _logger?.LogInformation("Reusing project already open in current TIA Portal instance: {ProjectPath}", projectPath);
                        return true;
                    }

                    if (TryAttachToExistingPortalWithProject(normalizedProjectPath, projectName))
                    {
                        _logger?.LogInformation("Attached to existing TIA Portal instance that already has the project open: {ProjectPath}", projectPath);
                        return true;
                    }

                    if (CurrentPortalHasAnyOpenProject())
                    {
                        if (_ownsCurrentSession)
                        {
                            _session?.Close();
                            _sessionContext.ClearProjectSelection();
                        }
                        else if (_ownsCurrentProject && _project is Project ownedProject)
                        {
                            ownedProject.Close();
                            _sessionContext.ClearProjectSelection();
                        }
                        else
                        {
                            _logger?.LogInformation(
                                "The attached TIA Portal instance already has a different project open. Starting a dedicated hidden instance for {ProjectPath}.",
                                projectPath);

                            _portal?.Dispose();
                            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                            _sessionContext.ClearProjectSelection();
                        }
                    }

                    _sessionContext.ClearProjectSelection();
                    var umacDelegate = CreateUmacDelegate(authentication);
                    _project = umacDelegate == null
                        ? _portal.Projects.OpenWithUpgrade(fileInfo)
                        : _portal.Projects.OpenWithUpgrade(fileInfo, umacDelegate);
                    _ownsCurrentProject = _project != null;
                    _ownsCurrentSession = false;
                    if (_project == null)
                    {
                        LastError = $"Siemens Openness did not return a project for '{projectPath}'.";
                    }
                    return _project != null;
                }
                catch (Exception ex)
                {
                    LastError = FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to open project {ProjectPath}: {LastError}", projectPath, LastError);
                    return false;
                }
            });
        }

        public async Task<ProjectLifecycleResult?> CreateProjectAsync(string projectPath)
        {
            return await Task.Run(() =>
            {
                LastError = null;
                if (_portal == null)
                {
                    LastError = "Not connected to TIA Portal";
                    _logger?.LogWarning("{LastError}", LastError);
                    return null;
                }

                try
                {
                    var projectTarget = SplitProjectPath(projectPath);
                    PreparePortalForNewSelectedProject(projectTarget.Name);

                    _project = _portal.Projects.Create(projectTarget.Directory, projectTarget.Name);
                    _ownsCurrentProject = _project != null;
                    _ownsCurrentSession = false;

                    return _project == null
                        ? null
                        : ToLifecycleResult(_project);
                }
                catch (Exception ex)
                {
                    LastError = FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to create project {ProjectPath}: {LastError}", projectPath, LastError);
                    return null;
                }
            });
        }

        public async Task<DeviceInfo?> AddDeviceAsync(string orderNumber, string? version, string name)
        {
            return await Task.Run(() =>
            {
                LastError = null;
                if (_project is not Project project)
                {
                    LastError = "No project is currently open";
                    _logger?.LogWarning("{LastError}", LastError);
                    return null;
                }

                try
                {
                    var typeIdentifier = BuildDeviceTypeIdentifier(orderNumber, version);
                    var device = project.Devices.CreateWithItem(typeIdentifier, name, name);
                    return new DeviceInfo(device.Name, device.TypeIdentifier, device.GetIdentification(), null);
                }
                catch (Exception ex)
                {
                    LastError = FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to add device {DeviceName}: {LastError}", name, LastError);
                    return null;
                }
            });
        }

        public async Task<ProjectArchiveResult?> ArchiveProjectAsync(string archivePath, string mode)
        {
            return await Task.Run(() =>
            {
                LastError = null;
                if (_project is not Project project)
                {
                    LastError = "No project is currently open";
                    _logger?.LogWarning("{LastError}", LastError);
                    return null;
                }

                try
                {
                    var archiveTarget = SplitArchivePath(archivePath);
                    Directory.CreateDirectory(archiveTarget.Directory.FullName);
                    var archivationMode = ParseArchivationMode(mode);
                    project.Archive(archiveTarget.Directory, archiveTarget.Name, archivationMode);
                    return new ProjectArchiveResult(archiveTarget.ExpectedPath, archivationMode.ToString());
                }
                catch (Exception ex)
                {
                    LastError = FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to archive project to {ArchivePath}: {LastError}", archivePath, LastError);
                    return null;
                }
            });
        }

        public async Task<ProjectLifecycleResult?> RetrieveProjectAsync(
            string archivePath,
            string targetDirectory,
            bool upgrade,
            ProjectAuthenticationOptions? authentication)
        {
            return await Task.Run(() =>
            {
                LastError = null;
                if (_portal == null)
                {
                    LastError = "Not connected to TIA Portal";
                    _logger?.LogWarning("{LastError}", LastError);
                    return null;
                }

                try
                {
                    var source = new FileInfo(archivePath);
                    var target = new DirectoryInfo(targetDirectory);
                    Directory.CreateDirectory(target.FullName);
                    PreparePortalForNewSelectedProject(Path.GetFileNameWithoutExtension(archivePath));

                    var umacDelegate = CreateUmacDelegate(authentication);
                    _project = upgrade
                        ? (umacDelegate == null
                            ? _portal.Projects.RetrieveWithUpgrade(source, target)
                            : _portal.Projects.RetrieveWithUpgrade(source, target, umacDelegate))
                        : (umacDelegate == null
                            ? _portal.Projects.Retrieve(source, target)
                            : _portal.Projects.Retrieve(source, target, umacDelegate));
                    _ownsCurrentProject = _project != null;
                    _ownsCurrentSession = false;

                    return _project == null
                        ? null
                        : ToLifecycleResult(_project);
                }
                catch (Exception ex)
                {
                    LastError = FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to retrieve project from {ArchivePath}: {LastError}", archivePath, LastError);
                    return null;
                }
            });
        }

        public async Task<bool> CloseProjectAsync(bool saveChanges = false)
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return true;
                }

                _logger?.LogInformation("Closing project...");

                try
                {
                    if (_session != null)
                    {
                        if (_ownsCurrentSession)
                        {
                            if (saveChanges && _session.Project != null)
                            {
                                // Sessions auto-save on close.
                            }

                            _session.Close();
                        }
                        else
                        {
                            _logger?.LogInformation("Leaving externally managed local session open.");
                        }
                    }
                    else if (_project is Project project)
                    {
                        if (_ownsCurrentProject)
                        {
                            if (saveChanges)
                            {
                                project.Save();
                            }

                            project.Close();
                        }
                        else
                        {
                            _logger?.LogInformation("Leaving externally managed project open: {ProjectName}", project.Name);
                        }
                    }

                    _sessionContext.ClearProjectSelection();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to close project");
                    return false;
                }
            });
        }

        public async Task<bool> SaveProjectAsync()
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return false;
                }

                _logger?.LogInformation("Saving project: {ProjectName}", _project.Name);

                try
                {
                    if (_session != null)
                    {
                        _logger?.LogInformation("Session project auto-saved");
                        return true;
                    }

                    if (_project is Project project)
                    {
                        project.Save();
                        _logger?.LogInformation("Project saved successfully");
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to save project");
                    return false;
                }
            });
        }

        public async Task<bool> SaveAsProjectAsync(string destinationPath)
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return false;
                }

                _logger?.LogInformation("Saving project to: {DestinationPath}", destinationPath);

                try
                {
                    if (_project is not Project project)
                    {
                        _logger?.LogWarning("Save As is not supported for sessions");
                        return false;
                    }

                    DirectoryInfo destinationDirectory;
                    if (Path.HasExtension(destinationPath))
                    {
                        var directory = Path.GetDirectoryName(destinationPath);
                        destinationDirectory = string.IsNullOrEmpty(directory)
                            ? new DirectoryInfo(destinationPath)
                            : new DirectoryInfo(directory);
                    }
                    else
                    {
                        destinationDirectory = new DirectoryInfo(destinationPath);
                    }

                    project.SaveAs(destinationDirectory);
                    _logger?.LogInformation("Project saved successfully to: {DestinationPath}", destinationDirectory.FullName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to save project to: {DestinationPath}", destinationPath);
                    return false;
                }
            });
        }

        public void Dispose()
        {
            try
            {
                if (_ownsCurrentSession)
                {
                    _session?.Close();
                }
                else if (_ownsCurrentProject && _project is Project project)
                {
                    project.Close();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to close owned TIA Portal project/session during dispose.");
                Trace.TraceWarning($"[PortalSessionService] Failed to close owned project/session during dispose: {FormatExceptionMessage(ex)}");
            }

            try
            {
                _portal?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to dispose TIA Portal instance.");
                Trace.TraceWarning($"[PortalSessionService] Failed to dispose TIA Portal instance: {FormatExceptionMessage(ex)}");
            }

            _sessionContext.ClearPortalSelection();
        }

        private static bool IsLocalSessionFile(string sessionPath)
        {
            return Regex.IsMatch(sessionPath, @"\.als\d+$", RegexOptions.IgnoreCase);
        }

        private void PreparePortalForNewSelectedProject(string requestedProjectName)
        {
            if (_portal == null)
            {
                throw new InvalidOperationException("Not connected to TIA Portal");
            }

            if (!CurrentPortalHasAnyOpenProject())
            {
                _sessionContext.ClearProjectSelection();
                return;
            }

            if (_ownsCurrentSession)
            {
                _session?.Close();
                _sessionContext.ClearProjectSelection();
                return;
            }

            if (_ownsCurrentProject && _project is Project ownedProject)
            {
                ownedProject.Close();
                _sessionContext.ClearProjectSelection();
                return;
            }

            _logger?.LogInformation(
                "The attached TIA Portal instance already has an externally managed project open. Starting a dedicated hidden instance for {ProjectName}.",
                requestedProjectName);

            _portal.Dispose();
            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
            _attachedPortalProcessId = null;
            _sessionContext.ClearProjectSelection();
        }

        private static UmacDelegate? CreateUmacDelegate(ProjectAuthenticationOptions? authentication)
        {
            if (authentication == null)
            {
                return null;
            }

            return credentials =>
            {
                credentials.SetAttribute("Name", authentication.UserName);
                credentials.SetAttribute("Type", ParseUmacUserType(authentication.UserType));
                credentials.SetPassword(authentication.Password);
            };
        }

        private static UmacUserType ParseUmacUserType(string userType)
        {
            return string.Equals(userType, "global", StringComparison.OrdinalIgnoreCase)
                ? UmacUserType.Global
                : UmacUserType.Project;
        }

        private static ProjectArchivationMode ParseArchivationMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return ProjectArchivationMode.Compressed;
            }

            return (ProjectArchivationMode)Enum.Parse(typeof(ProjectArchivationMode), mode, ignoreCase: true);
        }

        private static string BuildDeviceTypeIdentifier(string orderNumber, string? version)
        {
            if (string.IsNullOrWhiteSpace(version) ||
                orderNumber.IndexOf(version, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return orderNumber;
            }

            return $"{orderNumber}/{version}";
        }

        private static (DirectoryInfo Directory, string Name) SplitProjectPath(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Project path is required.", nameof(projectPath));
            }

            var fullPath = Path.GetFullPath(projectPath);
            var name = Path.GetFileNameWithoutExtension(fullPath);
            var parentPath = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parentPath))
            {
                throw new ArgumentException("Project path must include a project name and parent directory.", nameof(projectPath));
            }

            return (new DirectoryInfo(parentPath), name);
        }

        private static (DirectoryInfo Directory, string Name, string ExpectedPath) SplitArchivePath(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                throw new ArgumentException("Archive path is required.", nameof(archivePath));
            }

            var fullPath = Path.GetFullPath(archivePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            var name = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Archive path must include a file name and parent directory.", nameof(archivePath));
            }

            return (new DirectoryInfo(directoryPath), name, fullPath);
        }

        private static ProjectLifecycleResult ToLifecycleResult(ProjectBase project)
        {
            return new ProjectLifecycleResult(project.Name, TryGetProjectPath(project) ?? string.Empty);
        }

        private bool CurrentPortalHasAnyOpenProject()
        {
            if (_portal == null)
            {
                return false;
            }

            return (_portal.LocalSessions != null && _portal.LocalSessions.Any()) ||
                   (_portal.Projects != null && _portal.Projects.Any());
        }

        private bool TrySelectCurrentProject(string normalizedProjectPath, string projectName)
        {
            if (_project == null)
            {
                return false;
            }

            var currentPath = TryGetProjectPath(_project);
            return ProjectMatches(currentPath, _project.Name, normalizedProjectPath, projectName);
        }

        private bool TrySelectOpenProjectInCurrentPortal(string normalizedProjectPath, string projectName)
        {
            if (_portal == null)
            {
                return false;
            }

            if (_portal.LocalSessions != null)
            {
                foreach (var session in _portal.LocalSessions)
                {
                    var sessionProject = session.Project;
                    if (sessionProject != null &&
                        ProjectMatches(TryGetProjectPath(sessionProject), sessionProject.Name, normalizedProjectPath, projectName))
                    {
                        _sessionContext.ClearPlcSelection();
                        _session = session;
                        _project = sessionProject;
                        _ownsCurrentProject = false;
                        _ownsCurrentSession = false;
                        return true;
                    }
                }
            }

            if (_portal.Projects != null)
            {
                foreach (var project in _portal.Projects)
                {
                    if (ProjectMatches(TryGetProjectPath(project), project.Name, normalizedProjectPath, projectName))
                    {
                        _sessionContext.ClearPlcSelection();
                        _session = null;
                        _project = project;
                        _ownsCurrentProject = false;
                        _ownsCurrentSession = false;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TrySelectSingleOpenProjectInCurrentPortal()
        {
            if (_portal == null)
            {
                return false;
            }

            LocalSession? selectedSession = null;
            ProjectBase? selectedProject = null;
            var count = 0;

            if (_portal.LocalSessions != null)
            {
                foreach (var session in _portal.LocalSessions)
                {
                    if (session.Project != null)
                    {
                        count++;
                        selectedSession = session;
                        selectedProject = session.Project;
                    }
                }
            }

            if (_portal.Projects != null)
            {
                foreach (var project in _portal.Projects)
                {
                    count++;
                    selectedSession = null;
                    selectedProject = project;
                }
            }

            if (count != 1 || selectedProject == null)
            {
                return false;
            }

            _sessionContext.ClearProjectSelection();
            _session = selectedSession;
            _project = selectedProject;
            _ownsCurrentProject = false;
            _ownsCurrentSession = false;
            _logger?.LogInformation("Selected the only open project after attaching: {ProjectName}", selectedProject.Name);
            return true;
        }

        private bool TryAttachToExistingPortalWithProject(string normalizedProjectPath, string projectName)
        {
            foreach (var process in TiaPortal.GetProcesses())
            {
                if (_attachedPortalProcessId.HasValue && process.Id == _attachedPortalProcessId.Value)
                {
                    continue;
                }

                TiaPortal? attachedPortal = null;

                try
                {
                    attachedPortal = process.Attach();

                    if (TrySelectOpenProjectInPortal(attachedPortal, normalizedProjectPath, projectName, out var matchedSession, out var matchedProject))
                    {
                        _portal?.Dispose();

                        _portal = attachedPortal;
                        _attachedPortalProcessId = process.Id;
                        _sessionContext.ClearPlcSelection();
                        _session = matchedSession;
                        _project = matchedProject;
                        _ownsCurrentProject = false;
                        _ownsCurrentSession = false;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to inspect TIA Portal instance {InstanceId} while locating an already open project.", process.Id);
                }

                try
                {
                    attachedPortal?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to dispose inspected TIA Portal instance {InstanceId}.", process.Id);
                    Trace.TraceWarning($"[PortalSessionService] Failed to dispose inspected TIA Portal instance {process.Id}: {FormatExceptionMessage(ex)}");
                }
            }

            return false;
        }

        private static bool TrySelectOpenProjectInPortal(
            TiaPortal portal,
            string normalizedProjectPath,
            string projectName,
            out LocalSession? matchedSession,
            out ProjectBase? matchedProject)
        {
            if (portal.LocalSessions != null)
            {
                foreach (var session in portal.LocalSessions)
                {
                    if (session.Project != null &&
                        ProjectMatches(TryGetProjectPath(session.Project), session.Project.Name, normalizedProjectPath, projectName))
                    {
                        matchedSession = session;
                        matchedProject = session.Project;
                        return true;
                    }
                }
            }

            if (portal.Projects != null)
            {
                foreach (var project in portal.Projects)
                {
                    if (ProjectMatches(TryGetProjectPath(project), project.Name, normalizedProjectPath, projectName))
                    {
                        matchedSession = null;
                        matchedProject = project;
                        return true;
                    }
                }
            }

            matchedSession = null;
            matchedProject = null;
            return false;
        }

        private static string? GetProcessVersion(TiaPortalProcess process)
        {
            try
            {
                var portalProduct = process.InstalledSoftware?
                    .FirstOrDefault(product =>
                        product.Name?.IndexOf("Automation Portal", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? process.InstalledSoftware?.FirstOrDefault();

                var version = portalProduct?.Version;
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetProjectPath(ProjectBase? project)
        {
            try
            {
                return project?.Path?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeProjectPath(string projectPath)
        {
            return Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool PathEquals(string? left, string? right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var normalizedLeft = NormalizeProjectPath(left);
            var normalizedRight = NormalizeProjectPath(right);

            return string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ProjectMatches(string? candidatePath, string? candidateName, string normalizedProjectPath, string projectName)
        {
            if (PathEquals(candidatePath, normalizedProjectPath))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(candidateName) &&
                !string.IsNullOrWhiteSpace(projectName) &&
                string.Equals(candidateName, projectName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string FormatExceptionMessage(Exception ex)
        {
            return ex.InnerException?.Message ?? ex.Message;
        }
    }
}
