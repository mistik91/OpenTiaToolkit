using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Hmi;

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
    /// Gets the TIA Portal version (e.g., "V19", "V20").
    /// </summary>
    string? Version { get; }

    /// <summary>
    /// Gets the currently open TIA Portal project (if any).
    /// </summary>
    Siemens.Engineering.ProjectBase? Project { get; }

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
    /// <param name="projectPath">The full path to the .ap19 file.</param>
    /// <returns>True if the project was opened successfully.</returns>
    Task<bool> OpenProjectAsync(string projectPath);

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

/// <summary>
/// Represents information about a running TIA Portal instance.
/// </summary>
public class TiaPortalInstance
{
    public TiaPortalInstance(int processId, string? version, bool isAttached)
    {
        ProcessId = processId;
        Version = version;
        IsAttached = isAttached;
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the TIA Portal version.</summary>
    public string? Version { get; }

    /// <summary>Gets whether this instance is currently attached.</summary>
    public bool IsAttached { get; }
}

/// <summary>
/// Represents a node in the project tree structure.
/// </summary>
public class ProjectTreeNode
{
    public ProjectTreeNode(string name, string type, string path, IEnumerable<ProjectTreeNode> children)
    {
        Name = name;
        Type = type;
        Path = path;
        Children = new List<ProjectTreeNode>(children);
    }

    /// <summary>Gets the node name.</summary>
    public string Name { get; }

    /// <summary>Gets the node type (e.g., "Device", "Folder", "PlcSoftware").</summary>
    public string Type { get; }

    /// <summary>Gets the full path to the node.</summary>
    public string Path { get; }

    /// <summary>Gets the child nodes.</summary>
    public IReadOnlyList<ProjectTreeNode> Children { get; }
}

/// <summary>
/// Represents information about a TIA Portal project.
/// </summary>
public class ProjectInfo
{
    public ProjectInfo(string name, string path, string? version, DateTime? created)
    {
        Name = name;
        Path = path;
        Version = version;
        Created = created;
    }

    /// <summary>Gets the project name.</summary>
    public string Name { get; }

    /// <summary>Gets the full path to the project file.</summary>
    public string Path { get; }

    /// <summary>Gets the TIA Portal version.</summary>
    public string? Version { get; }

    /// <summary>Gets when the project was created.</summary>
    public DateTime? Created { get; }
}

/// <summary>
/// Represents information about a device in a TIA Portal project.
/// </summary>
public class DeviceInfo
{
    public DeviceInfo(string name, string type, string? identification, string? ipAddress)
    {
        Name = name;
        Type = type;
        Identification = identification;
        IpAddress = ipAddress;
    }

    /// <summary>Gets the device name.</summary>
    public string Name { get; }

    /// <summary>Gets the device type (e.g., "PLC_1200", "HMI_Comfort").</summary>
    public string Type { get; }

    /// <summary>Gets the device identification.</summary>
    public string? Identification { get; }

    /// <summary>Gets the device IP address if configured.</summary>
    public string? IpAddress { get; }
}
