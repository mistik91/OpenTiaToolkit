using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                        _portal = processes.First().Attach();
                        _logger?.LogInformation("Attached to existing TIA Portal instance.");
                        return true;
                    }

                    _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
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
                    var currentPortalInstanceId = GetPortalInstanceId(_portal);
                    foreach (var process in TiaPortal.GetProcesses())
                    {
                        instances.Add(new TiaPortalInstance(
                            process.Id,
                            process.ToString(),
                            currentPortalInstanceId.HasValue && process.Id == currentPortalInstanceId.Value));
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
            return await Task.Run(() =>
            {
                if (_portal == null)
                {
                    _logger?.LogWarning("Not connected to TIA Portal");
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
                    _project = _portal.Projects.OpenWithUpgrade(fileInfo);
                    _ownsCurrentProject = _project != null;
                    _ownsCurrentSession = false;
                    return _project != null;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open project: {ProjectPath}", projectPath);
                    return false;
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
            catch (Exception)
            {
                // Ignore
            }

            try
            {
                _portal?.Dispose();
            }
            catch (Exception)
            {
                // Ignore
            }

            _sessionContext.ClearPortalSelection();
        }

        private static bool IsLocalSessionFile(string sessionPath)
        {
            return Regex.IsMatch(sessionPath, @"\.als\d+$", RegexOptions.IgnoreCase);
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

        private bool TryAttachToExistingPortalWithProject(string normalizedProjectPath, string projectName)
        {
            var currentPortalInstanceId = GetPortalInstanceId(_portal);

            foreach (var process in TiaPortal.GetProcesses())
            {
                if (currentPortalInstanceId.HasValue && process.Id == currentPortalInstanceId.Value)
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
                catch (Exception)
                {
                    // Ignore
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

        private static int? GetPortalInstanceId(TiaPortal? portal)
        {
            return (int?)portal?.GetType().GetProperty("Id")?.GetValue(portal);
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
    }
}
