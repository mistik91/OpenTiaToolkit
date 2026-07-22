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
    internal sealed class PortalProjectOpenWorkflow
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

        public PortalProjectOpenWorkflow(EngineeringSessionContext sessionContext, ILogger? logger, IPortalSessionStateCoordinator state, IPortalRuntimeAdapter runtime, PortalOperationStatus status)
        {
            _sessionContext = sessionContext;
            _logger = logger;
            _state = state;
            _runtime = runtime;
            _status = status;
        }
        public async Task<bool> OpenProjectAsync(string projectPath)
        {
            return await OpenProjectAsync(projectPath, null, openInNewInstance: false);
        }

        public async Task<bool> OpenProjectAsync(string projectPath, ProjectAuthenticationOptions? authentication)
        {
            return await OpenProjectAsync(projectPath, authentication, openInNewInstance: false);
        }

        public async Task<bool> OpenProjectAsync(
            string projectPath,
            ProjectAuthenticationOptions? authentication,
            bool openInNewInstance)
        {
            return await Task.Run(() =>
            {
                _status.LastError = null;
                if (_portal == null)
                {
                    _status.LastError = "Not connected to TIA Portal";
                    _logger?.LogWarning("{LastError}", _status.LastError);
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

                    if (PortalSessionPolicy.IsLocalSessionFile(projectPath))
                    {
                        var candidateSession = _runtime.OpenLocalSession(_portal, fileInfo);
                        var localSessionProject = candidateSession == null ? null : _runtime.GetSessionProject(candidateSession);
                        if (localSessionProject == null)
                        {
                            _status.LastError = $"Opened local session file '{projectPath}', but Siemens Openness did not return a project.";
                            if (candidateSession != null) _runtime.CloseSession(candidateSession);
                            return false;
                        }

                        _state.CommitProject(
                            candidateSession,
                            localSessionProject,
                            ownsProject: OwnsPortalLifecycle,
                            ownsSession: OwnsPortalLifecycle);
                        return true;
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

                    var currentPortalHasOpenProject = CurrentPortalHasAnyOpenProject();
                    if (currentPortalHasOpenProject && !openInNewInstance)
                    {
                        _status.LastError =
                            $"The attached TIA Portal instance already has a different project open ({DescribeOpenProjects()}). " +
                            "Close it first or pass --open-in-new-instance true to use a dedicated hidden instance.";
                        _logger?.LogWarning("{LastError}", _status.LastError);
                        return false;
                    }

                    var targetPortal = _portal!;
                    var usesCandidatePortal = openInNewInstance;
                    if (usesCandidatePortal)
                    {
                        _logger?.LogInformation(
                            "Opening {ProjectPath} in an explicitly requested dedicated hidden TIA Portal instance.",
                            projectPath);
                        targetPortal = _runtime.Start(TiaPortalMode.WithoutUserInterface);
                    }

                    var umacDelegate = PortalSessionPolicy.CreateUmacDelegate(authentication);
                    PortalOpenProject? candidate;
                    try
                    {
                        candidate = _runtime.OpenProjectWithUpgrade(targetPortal, fileInfo, umacDelegate);
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
                        _status.LastError = $"Siemens Openness did not return a project for '{projectPath}'.";
                        if (usesCandidatePortal)
                        {
                            _runtime.DisposePortal(targetPortal);
                        }

                        return false;
                    }

                    if (!PathEquals(candidate.Path, normalizedProjectPath))
                    {
                        _status.LastError =
                            $"Siemens Openness opened project '{candidate.Path ?? candidate.Name}', " +
                            $"which does not match the requested path '{normalizedProjectPath}'.";
                        try
                        {
                            if (candidate.Project is Project mismatchedProject)
                            {
                                _runtime.CloseProject(mismatchedProject);
                            }
                        }
                        finally
                        {
                            if (usesCandidatePortal)
                            {
                                _runtime.DisposePortal(targetPortal);
                            }
                        }

                        return false;
                    }

                    if (usesCandidatePortal)
                    {
                        _state.CommitPortal(
                            targetPortal,
                            null,
                            null,
                            candidate.Project,
                            ownsProject: true,
                            ownsSession: false,
                            isPortalVisible: false);
                    }
                    else
                    {
                        _state.CommitProject(null, candidate.Project, ownsProject: OwnsPortalLifecycle, ownsSession: false);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _status.LastError = PortalSessionPolicy.FormatExceptionMessage(ex);
                    _logger?.LogError(ex, "Failed to open project {ProjectPath}: {LastError}", projectPath, _status.LastError);
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

        private bool TrySelectCurrentProject(string normalizedProjectPath, string projectName)
        {
            if (_project == null)
            {
                return false;
            }

            var current = _runtime.GetProjectIdentity(_project);
            return ProjectMatches(current.Path, current.Name, normalizedProjectPath, projectName);
        }

        private bool TrySelectOpenProjectInCurrentPortal(string normalizedProjectPath, string projectName)
        {
            if (_portal == null)
            {
                return false;
            }

            foreach (var candidate in _runtime.GetOpenProjects(_portal))
            {
                if (ProjectMatches(candidate.Path, candidate.Name, normalizedProjectPath, projectName))
                {
                    _state.CommitProject(candidate.Session, candidate.Project, ownsProject: false, ownsSession: false);
                    return true;
                }
            }

            return false;
        }

        private bool TryAttachToExistingPortalWithProject(string normalizedProjectPath, string projectName)
        {
            foreach (var process in _runtime.GetProcesses())
            {
                if (_attachedPortalProcessId.HasValue && process.Id == _attachedPortalProcessId.Value)
                {
                    continue;
                }

                TiaPortal? attachedPortal = null;

                try
                {
                    attachedPortal = _runtime.Attach(process);

                    var matched = FindOpenProject(attachedPortal, normalizedProjectPath, projectName);
                    if (matched != null)
                    {
                        _state.CommitPortal(attachedPortal, process.Id, matched.Session, matched.Project, false, false, isPortalVisible: true);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to inspect TIA Portal instance {InstanceId} while locating an already open project.", process.Id);
                }

                try
                {
                    if (attachedPortal != null) _runtime.DisposePortal(attachedPortal);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to dispose inspected TIA Portal instance {InstanceId}.", process.Id);
                    Trace.TraceWarning($"[PortalWorkflowCoordinator] Failed to dispose inspected TIA Portal instance {process.Id}: {PortalSessionPolicy.FormatExceptionMessage(ex)}");
                }
            }

            return false;
        }

        private PortalOpenProject? FindOpenProject(TiaPortal portal, string normalizedProjectPath, string projectName)
        {
            return _runtime.GetOpenProjects(portal).FirstOrDefault(candidate =>
                ProjectMatches(candidate.Path, candidate.Name, normalizedProjectPath, projectName));
        }

        private string DescribeOpenProjects()
        {
            if (_portal == null) return "none";
            var descriptions = _runtime.GetOpenProjects(_portal)
                .Select(candidate => candidate.Path ?? candidate.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return descriptions.Count == 0 ? "none" : string.Join(", ", descriptions);
        }
    }
}
