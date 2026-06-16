using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Thin facade that keeps the public portal contract stable while delegating
    /// lifecycle and inspection work to narrower services.
    /// </summary>
    public class PortalService : IPortalService, IEngineeringSessionContextAccessor
    {
        static PortalService()
        {
            // Ensure Openness assemblies can be resolved even when the library is
            // consumed directly instead of via the CLI/MCP entry points.
            TiaPortalAssemblyResolver.Register();
        }

        private readonly EngineeringSessionContext _sessionContext;
        private readonly PortalSessionService _sessionService;
        private readonly PortalInspectionService _inspectionService;

        /// <summary>
        /// Initializes a new instance of the <see cref="PortalService"/> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public PortalService(ILogger<PortalService>? logger = null)
            : this(new EngineeringSessionContext(), logger)
        {
        }

        internal PortalService(EngineeringSessionContext sessionContext, ILogger<PortalService>? logger = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            var v21Adapter = new V21OpennessAdapter();
            _sessionService = new PortalSessionService(_sessionContext, logger);
            _inspectionService = new PortalInspectionService(_sessionContext, logger, v21Adapter);
        }

        EngineeringSessionContext IEngineeringSessionContextAccessor.SessionContext => _sessionContext;

        /// <inheritdoc/>
        public bool IsConnected => _sessionContext.Portal != null;

        /// <inheritdoc/>
        public string? Version => _inspectionService.GetVersion();

        /// <inheritdoc/>
        public ProjectBase? Project => _sessionContext.Project;

        /// <inheritdoc/>
        public string? LastError => _sessionService.LastError;

        /// <inheritdoc/>
        public async Task<bool> ConnectAsync()
        {
            return await _sessionService.ConnectAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<TiaPortalInstance>> GetInstancesAsync()
        {
            return await _sessionService.GetInstancesAsync();
        }

        /// <inheritdoc/>
        public async Task<bool> OpenTiaPortalAsync(bool withUi = false)
        {
            return await _sessionService.OpenTiaPortalAsync(withUi);
        }

        /// <inheritdoc/>
        public async Task<bool> CloseTiaPortalAsync()
        {
            return await _sessionService.CloseTiaPortalAsync();
        }

        /// <inheritdoc/>
        public async Task<bool> ConnectToInstanceAsync(int instanceId)
        {
            return await _sessionService.ConnectToInstanceAsync(instanceId);
        }

        /// <inheritdoc/>
        public async Task<bool> DisconnectAsync()
        {
            return await _sessionService.DisconnectAsync();
        }

        /// <inheritdoc/>
        public async Task<bool> OpenProjectAsync(string projectPath)
        {
            return await _sessionService.OpenProjectAsync(projectPath);
        }

        /// <inheritdoc/>
        public async Task<bool> OpenProjectAsync(string projectPath, ProjectAuthenticationOptions? authentication)
        {
            return await _sessionService.OpenProjectAsync(projectPath, authentication);
        }

        /// <inheritdoc/>
        public async Task<ProjectLifecycleResult?> CreateProjectAsync(string projectPath)
        {
            return await _sessionService.CreateProjectAsync(projectPath);
        }

        /// <inheritdoc/>
        public async Task<DeviceInfo?> AddDeviceAsync(string orderNumber, string? version, string name)
        {
            return await _sessionService.AddDeviceAsync(orderNumber, version, name);
        }

        /// <inheritdoc/>
        public async Task<ProjectArchiveResult?> ArchiveProjectAsync(string archivePath, string mode)
        {
            return await _sessionService.ArchiveProjectAsync(archivePath, mode);
        }

        /// <inheritdoc/>
        public async Task<ProjectLifecycleResult?> RetrieveProjectAsync(
            string archivePath,
            string targetDirectory,
            bool upgrade,
            ProjectAuthenticationOptions? authentication)
        {
            return await _sessionService.RetrieveProjectAsync(archivePath, targetDirectory, upgrade, authentication);
        }

        /// <inheritdoc/>
        public async Task<bool> CloseProjectAsync(bool saveChanges = false)
        {
            return await _sessionService.CloseProjectAsync(saveChanges);
        }

        /// <inheritdoc/>
        public async Task<ProjectInfo?> GetProjectInfoAsync()
        {
            return await _inspectionService.GetProjectInfoAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ProjectInfo>> GetOpenProjectsAsync()
        {
            return await _inspectionService.GetOpenProjectsAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync()
        {
            return await _inspectionService.GetDevicesAsync();
        }

        /// <inheritdoc/>
        public async Task<DeviceItemInfo?> GetDeviceInfoAsync(string deviceName)
        {
            return await _inspectionService.GetDeviceInfoAsync(deviceName);
        }

        /// <inheritdoc/>
        public async Task<DeviceItemInfo?> GetDeviceItemInfoAsync(string deviceName, string itemName)
        {
            return await _inspectionService.GetDeviceItemInfoAsync(deviceName, itemName);
        }

        /// <inheritdoc/>
        public async Task<bool> SaveProjectAsync()
        {
            return await _sessionService.SaveProjectAsync();
        }

        /// <inheritdoc/>
        public async Task<bool> SaveAsProjectAsync(string destinationPath)
        {
            return await _sessionService.SaveAsProjectAsync(destinationPath);
        }

        /// <inheritdoc/>
        public async Task<ProjectTreeNode?> GetProjectTreeAsync(int? maxDepth = null)
        {
            return await _inspectionService.GetProjectTreeAsync(maxDepth);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PlcInfo>> GetPlcsAsync()
        {
            return await _inspectionService.GetPlcsAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<HmiInfo>> GetHmisAsync()
        {
            return await _inspectionService.GetHmisAsync();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _sessionService.Dispose();
        }
    }

    #region Extension Methods

    /// <summary>
    /// Extension methods for Device class.
    /// </summary>
    public static class DeviceExtensions
    {
        /// <summary>
        /// Gets the identification string for a device.
        /// </summary>
        public static string? GetIdentification(this Device device)
        {
            try
            {
                var deviceItem = device.DeviceItems.FirstOrDefault();
                if (deviceItem != null)
                {
                    return $"{device.Name} ({device.TypeIdentifier})";
                }
            }
            catch
            {
                // Ignore
            }

            return device.Name;
        }
    }

    #endregion
}
