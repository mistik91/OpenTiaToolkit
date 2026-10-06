using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>Compatibility facade over focused portal instance, open, and lifecycle workflows.</summary>
    internal sealed class PortalSessionService : IDisposable
    {
        private readonly PortalInstanceWorkflow _instances;
        private readonly PortalProjectOpenWorkflow _open;
        private readonly PortalProjectLifecycleWorkflow _lifecycle;
        private readonly PortalSessionStateCoordinator _state;
        private readonly PortalOperationStatus _status;

        public PortalSessionService(EngineeringSessionContext sessionContext, ILogger? logger)
        {
            IPortalRuntimeAdapter runtime = new PortalRuntimeAdapter();
            _status = new PortalOperationStatus();
            _state = new PortalSessionStateCoordinator(sessionContext, logger, runtime);
            _instances = new PortalInstanceWorkflow(sessionContext, logger, _state, runtime, _status);
            _open = new PortalProjectOpenWorkflow(sessionContext, logger, _state, runtime, _status);
            _lifecycle = new PortalProjectLifecycleWorkflow(sessionContext, logger, _state, runtime, _status);
        }

        public string? LastError => _status.LastError;
        public Task<bool> ConnectAsync() => _instances.ConnectAsync();
        public Task<IReadOnlyList<TiaPortalInstance>> GetInstancesAsync() => _instances.GetInstancesAsync();
        public Task<bool> OpenTiaPortalAsync(bool withUi = false) => _instances.OpenTiaPortalAsync(withUi);
        public Task<bool> CloseTiaPortalAsync() => _instances.CloseTiaPortalAsync();
        public Task<bool> ConnectToInstanceAsync(int instanceId) => _instances.ConnectToInstanceAsync(instanceId);
        public Task<bool> DisconnectAsync() => _instances.DisconnectAsync();
        public Task<bool> OpenProjectAsync(string path) => _open.OpenProjectAsync(path);
        public Task<bool> OpenProjectAsync(string path, ProjectAuthenticationOptions? authentication) => _open.OpenProjectAsync(path, authentication);
        public Task<bool> OpenProjectAsync(string path, ProjectAuthenticationOptions? authentication, bool newInstance) => _open.OpenProjectAsync(path, authentication, newInstance);
        public Task<ProjectLifecycleResult?> CreateProjectAsync(string path) => _lifecycle.CreateProjectAsync(path);
        public Task<DeviceInfo?> AddDeviceAsync(string orderNumber, string? version, string name) => _lifecycle.AddDeviceAsync(orderNumber, version, name);
        public Task<ProjectArchiveResult?> ArchiveProjectAsync(string path, string mode) => _lifecycle.ArchiveProjectAsync(path, mode);
        public Task<ProjectLifecycleResult?> RetrieveProjectAsync(string archive, string target, bool upgrade, ProjectAuthenticationOptions? authentication) => _lifecycle.RetrieveProjectAsync(archive, target, upgrade, authentication);
        public Task<bool> CloseProjectAsync(bool save = false) => _lifecycle.CloseProjectAsync(save);
        public Task<bool> SaveProjectAsync() => _lifecycle.SaveProjectAsync();
        public Task<bool> SaveAsProjectAsync(string path) => _lifecycle.SaveAsProjectAsync(path);
        public void Dispose() => _state.Dispose();
    }
}
