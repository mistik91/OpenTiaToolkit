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
using System.Threading.Tasks;
using static OpenTiaToolkit.Core.Services.PortalProjectStateInspector;
namespace OpenTiaToolkit.Core.Services
{
    internal sealed class PortalProjectLifecycleWorkflow
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger? _logger;
        private readonly IPortalSessionStateCoordinator _state;
        private readonly IPortalRuntimeAdapter _runtime;
        private readonly PortalOperationStatus _status;
        private TiaPortal? _portal => _sessionContext.Portal;
        private int? _attachedPortalProcessId => _sessionContext.AttachedPortalProcessId;
        private ProjectBase? _project => _sessionContext.Project;
        private LocalSession? _session => _sessionContext.Session;
        private bool OwnsPortalLifecycle => !_attachedPortalProcessId.HasValue;

        public PortalProjectLifecycleWorkflow(EngineeringSessionContext sessionContext, ILogger? logger, IPortalSessionStateCoordinator state, IPortalRuntimeAdapter runtime, PortalOperationStatus status)
        {
            _sessionContext = sessionContext;
            _logger = logger;
            _state = state;
            _runtime = runtime;
            _status = status;
        }
        public async Task<ProjectLifecycleResult?> CreateProjectAsync(string projectPath)
        {
            return await Task.Run(() =>
            {
                _status.LastError = null;
                if (_portal == null)
                {
                    _status.LastError = "Not connected to TIA Portal";
                    _logger?.LogWarning("{LastError}", _status.LastError);
                    return null;
                }

                try
                {
                    var projectTarget = PortalSessionPolicy.SplitProjectPath(projectPath);
                    var targetPortal = _portal!;
                    var usesCandidatePortal = CurrentPortalHasAnyOpenProject();
                    if (usesCandidatePortal)
                    {
                        targetPortal = _runtime.Start(TiaPortalMode.WithoutUserInterface);
                    }

                    PortalOpenProject? candidate;
                    try
                    {
                        candidate = _runtime.CreateProject(targetPortal, projectTarget.Directory, projectTarget.Name);
                    }
                    catch
                    {
                        if (usesCandidatePortal)
                        {
                            _runtime.DisposePortal(targetPortal);
                        }

                        throw;
                    }

                    if (candidate == null)
                    {
                        if (usesCandidatePortal)
                        {
                            _runtime.DisposePortal(targetPortal);
                        }

                        return null;
                    }

                    if (usesCandidatePortal)
                    {
                        _state.CommitPortal(targetPortal, null, null, candidate.Project, ownsProject: true, ownsSession: false, isPortalVisible: false);
                    }
                    else
                    {
                        _state.CommitProject(null, candidate.Project, ownsProject: OwnsPortalLifecycle, ownsSession: false);
                    }

                    return new ProjectLifecycleResult(candidate.Name, candidate.Path ?? string.Empty);
                }
                catch (Exception ex)
                {
                    _status.LastError = PortalSessionPolicy.FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to create project {ProjectPath}: {LastError}", projectPath, _status.LastError);
                    return null;
                }
            });
        }

        public async Task<DeviceInfo?> AddDeviceAsync(string orderNumber, string? version, string name)
        {
            return await Task.Run(() =>
            {
                _status.LastError = null;
                if (_project is not Project project)
                {
                    _status.LastError = "No project is currently open";
                    _logger?.LogWarning("{LastError}", _status.LastError);
                    return null;
                }

                try
                {
                    var typeIdentifier = PortalSessionPolicy.BuildDeviceTypeIdentifier(orderNumber, version);
                    var device = _runtime.CreateDevice(project, typeIdentifier, name);
                    return new DeviceInfo(device.Name, device.TypeIdentifier, device.GetIdentification(), null);
                }
                catch (Exception ex)
                {
                    _status.LastError = PortalSessionPolicy.FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to add device {DeviceName}: {LastError}", name, _status.LastError);
                    return null;
                }
            });
        }

        public async Task<ProjectArchiveResult?> ArchiveProjectAsync(string archivePath, string mode)
        {
            return await Task.Run(() =>
            {
                _status.LastError = null;
                if (_project is not Project project)
                {
                    _status.LastError = "No project is currently open";
                    _logger?.LogWarning("{LastError}", _status.LastError);
                    return null;
                }

                try
                {
                    var archiveTarget = PortalSessionPolicy.SplitArchivePath(archivePath);
                    Directory.CreateDirectory(archiveTarget.Directory.FullName);
                    var archivationMode = PortalSessionPolicy.ParseArchivationMode(mode);
                    _runtime.ArchiveProject(project, archiveTarget.Directory, archiveTarget.Name, archivationMode);
                    return new ProjectArchiveResult(archiveTarget.ExpectedPath, archivationMode.ToString());
                }
                catch (Exception ex)
                {
                    _status.LastError = PortalSessionPolicy.FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to archive project to {ArchivePath}: {LastError}", archivePath, _status.LastError);
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
                _status.LastError = null;
                if (_portal == null)
                {
                    _status.LastError = "Not connected to TIA Portal";
                    _logger?.LogWarning("{LastError}", _status.LastError);
                    return null;
                }

                try
                {
                    var source = new FileInfo(archivePath);
                    var target = new DirectoryInfo(targetDirectory);
                    Directory.CreateDirectory(target.FullName);
                    var targetPortal = _portal!;
                    var usesCandidatePortal = CurrentPortalHasAnyOpenProject();
                    if (usesCandidatePortal)
                    {
                        targetPortal = _runtime.Start(TiaPortalMode.WithoutUserInterface);
                    }

                    var umacDelegate = PortalSessionPolicy.CreateUmacDelegate(authentication);
                    PortalOpenProject? candidate;
                    try
                    {
                        candidate = _runtime.RetrieveProject(targetPortal, source, target, upgrade, umacDelegate);
                    }
                    catch
                    {
                        if (usesCandidatePortal)
                        {
                            _runtime.DisposePortal(targetPortal);
                        }

                        throw;
                    }

                    if (candidate == null)
                    {
                        if (usesCandidatePortal)
                        {
                            _runtime.DisposePortal(targetPortal);
                        }

                        return null;
                    }

                    if (usesCandidatePortal)
                    {
                        _state.CommitPortal(targetPortal, null, null, candidate.Project, ownsProject: true, ownsSession: false, isPortalVisible: false);
                    }
                    else
                    {
                        _state.CommitProject(null, candidate.Project, ownsProject: OwnsPortalLifecycle, ownsSession: false);
                    }

                    return new ProjectLifecycleResult(candidate.Name, candidate.Path ?? string.Empty);
                }
                catch (Exception ex)
                {
                    _status.LastError = PortalSessionPolicy.FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to retrieve project from {ArchivePath}: {LastError}", archivePath, _status.LastError);
                    return null;
                }
            });
        }

        public async Task<bool> CloseProjectAsync(bool saveChanges = false)
        {
            return await Task.Run(() =>
            {
                _status.LastError = null;
                if (_project == null)
                {
                    var discovered = _portal == null ? null : GetSingleOpenProject(_portal);
                    if (discovered != null)
                    {
                        _logger?.LogInformation(
                            "Rediscovered open project {ProjectName} before explicit close.",
                            discovered.Name);
                        _state.CommitProject(discovered.Session, discovered.Project, ownsProject: false, ownsSession: false);
                    }
                    else if (CurrentPortalHasAnyOpenProject())
                    {
                        _status.LastError = "No project is selected and the attached portal contains multiple open projects. Connect to the intended instance/project before closing.";
                        _logger?.LogWarning("{LastError}", _status.LastError);
                        return false;
                    }
                    else
                    {
                        _logger?.LogInformation("No project is currently open");
                        return true;
                    }
                }

                var selected = _project == null ? null : _runtime.GetProjectIdentity(_project);
                var projectPath = selected?.Path;
                var projectName = selected?.Name;
                _logger?.LogInformation("Closing project {ProjectName}...", projectName);

                var closeAttempted = false;
                try
                {
                    if (_session != null)
                    {
                        if (saveChanges) _runtime.SaveSession(_session);

                        closeAttempted = true;
                        _runtime.CloseSession(_session);
                    }
                    else if (_project is Project project)
                    {
                        if (saveChanges)
                        {
                            _runtime.SaveProject(project);
                        }

                        closeAttempted = true;
                        _runtime.CloseProject(project);
                    }
                    else
                    {
                        _status.LastError = $"The selected project type '{_project?.GetType().FullName}' cannot be closed by this command.";
                        _logger?.LogWarning("{LastError}", _status.LastError);
                        return false;
                    }

                    if (PortalContainsProject(projectPath, projectName))
                    {
                        _status.LastError = $"Siemens Openness returned from close, but project '{projectName}' is still open in the attached portal.";
                        _logger?.LogWarning("{LastError}", _status.LastError);
                        return false;
                    }

                    _state.ClearProjectSelection();
                    return true;
                }
                catch (Exception ex)
                {
                    if (closeAttempted)
                    {
                        _state.ClearProjectSelection();
                    }

                    _status.LastError = PortalSessionPolicy.FormatExceptionMessage(ex);
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

                _logger?.LogInformation("Saving project: {ProjectName}", _runtime.GetProjectIdentity(_project).Name);

                try
                {
                    if (_session != null)
                    {
                        _runtime.SaveSession(_session);
                        _logger?.LogInformation("Session project saved successfully");
                        return true;
                    }

                    if (_project is Project project)
                    {
                        _runtime.SaveProject(project);
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
                    if (_session != null)
                    {
                        _logger?.LogWarning("Save As is not supported for sessions");
                        return false;
                    }

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

                    _runtime.SaveProjectAs(project, destinationDirectory);
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

        private bool CurrentPortalHasAnyOpenProject()
        {
            if (_portal == null)
            {
                return false;
            }

            return _runtime.GetOpenProjects(_portal).Count > 0;
        }

        private PortalOpenProject? GetSingleOpenProject(TiaPortal portal)
        {
            var openProjects = _runtime.GetOpenProjects(portal);
            return openProjects.Count == 1 ? openProjects[0] : null;
        }

        private bool PortalContainsProject(string? projectPath, string? projectName)
        {
            if (_portal == null) return false;
            return _runtime.GetOpenProjects(_portal).Any(candidate =>
            {
                if (!string.IsNullOrWhiteSpace(projectPath) && !string.IsNullOrWhiteSpace(candidate.Path))
                    return PathEquals(candidate.Path, projectPath);
                return !string.IsNullOrWhiteSpace(projectName) && string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
