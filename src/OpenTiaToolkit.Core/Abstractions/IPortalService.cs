using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions;

/// <summary>
/// Defines the interface for TIA Portal service operations.
/// Provides a unified abstraction over the TIA Openness API.
/// </summary>
public interface IPortalService : IDisposable
{
    /// <summary>
    /// Gets whether the portal is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
        /// Gets the TIA Portal version (e.g., "V21", "V22").
    /// </summary>
    string? Version { get; }

    /// <summary>
    /// Gets the currently open TIA Portal project (if any).
    /// </summary>
    Siemens.Engineering.ProjectBase? Project { get; }

    /// <summary>
    /// Gets the most recent portal operation failure detail, if any.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Gets the list of running TIA Portal instances.
    /// </summary>
    /// <returns>List of TIA Portal process IDs.</returns>
    Task<IReadOnlyList<TiaPortalInstance>> GetInstancesAsync();

    /// <summary>
    /// Opens a new TIA Portal instance.
    /// </summary>
    /// <param name="withUi">Whether to start with user interface (default: false).</param>
    /// <returns>True if the instance was opened successfully.</returns>
    Task<bool> OpenTiaPortalAsync(bool withUi = false);

    /// <summary>
    /// Closes the current TIA Portal instance.
    /// </summary>
    /// <returns>True if the instance was closed successfully.</returns>
    Task<bool> CloseTiaPortalAsync();

    /// <summary>
    /// Connects to a specific TIA Portal instance by process ID.
    /// </summary>
    /// <param name="instanceId">The process ID of the TIA Portal instance.</param>
    /// <returns>True if connection succeeded.</returns>
    Task<bool> ConnectToInstanceAsync(int instanceId);

    /// <summary>
    /// Connects to the TIA Portal instance.
    /// </summary>
    /// <returns>True if connection succeeded.</returns>
    Task<bool> ConnectAsync();

    /// <summary>
    /// Disconnects from the TIA Portal instance.
    /// </summary>
    /// <returns>True if disconnection succeeded.</returns>
    Task<bool> DisconnectAsync();

    /// <summary>
    /// Opens a TIA Portal project.
    /// </summary>
    /// <param name="projectPath">The full path to the project file (.ap21/.als21 primary, legacy formats also supported).</param>
    /// <returns>True if the project was opened successfully.</returns>
    Task<bool> OpenProjectAsync(string projectPath);

    /// <summary>
    /// Opens a TIA Portal project with optional UMAC credentials.
    /// </summary>
    /// <param name="projectPath">The full path to the project file (.ap21/.als21 primary, legacy formats also supported).</param>
    /// <param name="authentication">Optional UMAC authentication options.</param>
    /// <returns>True if the project was opened successfully.</returns>
    Task<bool> OpenProjectAsync(string projectPath, ProjectAuthenticationOptions? authentication);

    /// <summary>
    /// Creates a new TIA Portal project and selects it as the current project.
    /// </summary>
    /// <param name="projectPath">Project folder path or .ap21 file path.</param>
    /// <returns>Project lifecycle result, or null if creation failed.</returns>
    Task<ProjectLifecycleResult?> CreateProjectAsync(string projectPath);

    /// <summary>
    /// Adds a hardware device with its primary item to the current project.
    /// </summary>
    /// <param name="orderNumber">TIA hardware type identifier or order number.</param>
    /// <param name="version">Optional device catalog version.</param>
    /// <param name="name">Device and item base name.</param>
    /// <returns>Device information for the created device, or null if creation failed.</returns>
    Task<DeviceInfo?> AddDeviceAsync(string orderNumber, string? version, string name);

    /// <summary>
    /// Archives the current project.
    /// </summary>
    /// <param name="archivePath">Target .zap archive path.</param>
    /// <param name="mode">Archive mode.</param>
    /// <returns>Archive result, or null if archiving failed.</returns>
    Task<ProjectArchiveResult?> ArchiveProjectAsync(string archivePath, string mode);

    /// <summary>
    /// Retrieves an archived project and selects it as the current project.
    /// </summary>
    /// <param name="archivePath">Source .zap archive path.</param>
    /// <param name="targetDirectory">Target directory for the retrieved project.</param>
    /// <param name="upgrade">Whether to upgrade the retrieved project to the current TIA version.</param>
    /// <param name="authentication">Optional UMAC authentication options.</param>
    /// <returns>Project lifecycle result, or null if retrieve failed.</returns>
    Task<ProjectLifecycleResult?> RetrieveProjectAsync(string archivePath, string targetDirectory, bool upgrade, ProjectAuthenticationOptions? authentication);

    /// <summary>
    /// Closes the currently open project.
    /// </summary>
    /// <param name="saveChanges">Whether to save changes before closing (default: false).</param>
    /// <returns>True if the project was closed successfully.</returns>
    Task<bool> CloseProjectAsync(bool saveChanges = false);

    /// <summary>
    /// Gets information about the currently open project.
    /// </summary>
    /// <returns>Project information or null if no project is open.</returns>
    Task<ProjectInfo?> GetProjectInfoAsync();

    /// <summary>
    /// Lists all open projects.
    /// </summary>
    /// <returns>List of open project information.</returns>
    Task<IReadOnlyList<ProjectInfo>> GetOpenProjectsAsync();

    /// <summary>
    /// Lists all devices in the current project.
    /// </summary>
    /// <returns>List of device information.</returns>
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync();

    /// <summary>
    /// Gets detailed information about a specific device.
    /// </summary>
    /// <param name="deviceName">The name of the device.</param>
    /// <returns>Detailed device information or null if not found.</returns>
    Task<DeviceItemInfo?> GetDeviceInfoAsync(string deviceName);

    /// <summary>
    /// Gets detailed information about a specific device item.
    /// </summary>
    /// <param name="deviceName">The name of the parent device.</param>
    /// <param name="itemName">The name of the device item.</param>
    /// <returns>Detailed device item information or null if not found.</returns>
    Task<DeviceItemInfo?> GetDeviceItemInfoAsync(string deviceName, string itemName);

    /// <summary>
    /// Saves the current project.
    /// </summary>
    /// <returns>True if the project was saved successfully.</returns>
    Task<bool> SaveProjectAsync();

    /// <summary>
    /// Saves the current project to a new location.
    /// </summary>
    /// <param name="destinationPath">The full path where the project should be saved.</param>
    /// <returns>True if the project was saved successfully.</returns>
    Task<bool> SaveAsProjectAsync(string destinationPath);

    /// <summary>
    /// Gets the hierarchical tree structure of the current project.
    /// </summary>
    /// <param name="maxDepth">Maximum depth to traverse (null for unlimited).</param>
    /// <returns>The root node of the project tree.</returns>
    Task<ProjectTreeNode?> GetProjectTreeAsync(int? maxDepth = null);

    /// <summary>
    /// Lists all PLC devices found in the current project.
    /// </summary>
    /// <returns>List of PLC device information.</returns>
    Task<IReadOnlyList<PlcInfo>> GetPlcsAsync();

    /// <summary>
    /// Lists all HMI devices found in the current project.
    /// </summary>
    /// <returns>List of HMI device information.</returns>
    Task<IReadOnlyList<HmiInfo>> GetHmisAsync();
}
