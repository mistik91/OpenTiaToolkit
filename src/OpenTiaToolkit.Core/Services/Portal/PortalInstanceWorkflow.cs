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
    internal sealed class PortalInstanceWorkflow
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

        public PortalInstanceWorkflow(EngineeringSessionContext sessionContext, ILogger? logger, IPortalSessionStateCoordinator state, IPortalRuntimeAdapter runtime, PortalOperationStatus status)
        {
            _sessionContext = sessionContext;
            _logger = logger;
            _state = state;
            _runtime = runtime;
            _status = status;
        }
        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Connecting to TIA Portal...");

                try
                {
                    var processes = _runtime.GetProcesses();
                    if (processes.Any())
                    {
                        var process = processes.First();
                        var candidatePortal = _runtime.Attach(process);
                        try
                        {
                            var openProjects = _runtime.GetOpenProjects(candidatePortal);
                            var candidate = openProjects.Count == 1 ? openProjects[0] : null;
                            _state.CommitPortal(candidatePortal, process.Id, candidate?.Session, candidate?.Project, false, false, isPortalVisible: true);
                        }
                        catch
                        {
                            _runtime.DisposePortal(candidatePortal);
                            throw;
                        }
                        _logger?.LogInformation("Attached to existing TIA Portal instance.");
                        return true;
                    }

                    var newPortal = _runtime.Start(TiaPortalMode.WithoutUserInterface);
                    _state.CommitPortal(newPortal, null, null, null, false, false, isPortalVisible: false);
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
                    foreach (var process in _runtime.GetProcesses())
                    {
                        instances.Add(new TiaPortalInstance(
                            process.Id,
                            process.Version,
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
                    var mode = withUi ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface;
                    var candidatePortal = _runtime.Start(mode);
                    _state.CommitPortal(candidatePortal, null, null, null, false, false, isPortalVisible: withUi);
                    _logger?.LogInformation("Started new TIA Portal instance.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open TIA Portal");
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

                    _runtime.DisposePortal(_portal);
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
                    var process = _runtime.GetProcesses().FirstOrDefault(candidate => candidate.Id == instanceId);
                    if (process == null)
                    {
                        _logger?.LogWarning("No TIA Portal instance found with ID: {InstanceId}", instanceId);
                        return false;
                    }

                    var candidatePortal = _runtime.Attach(process);
                    try
                    {
                        var openProjects = _runtime.GetOpenProjects(candidatePortal);
                        var candidate = openProjects.Count == 1 ? openProjects[0] : null;
                        _state.CommitPortal(candidatePortal, process.Id, candidate?.Session, candidate?.Project, false, false, isPortalVisible: true);
                    }
                    catch
                    {
                        _runtime.DisposePortal(candidatePortal);
                        throw;
                    }
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
                    if (_portal != null) _runtime.DisposePortal(_portal);
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

    }
}
