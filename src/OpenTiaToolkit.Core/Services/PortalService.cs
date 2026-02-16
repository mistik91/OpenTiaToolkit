using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Multiuser;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Hmi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Provides TIA Portal service operations, wrapping the TIA Openness API.
    /// Supports both local projects and multiuser sessions.
    /// </summary>
    public class PortalService : IPortalService
    {
        private TiaPortal? _portal;
        private ProjectBase? _project;
        private LocalSession? _session;
        private readonly ILogger<PortalService>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PortalService"/> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public PortalService(ILogger<PortalService>? logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public bool IsConnected => _portal != null;

        /// <inheritdoc/>
        public string? Version => _portal?.ToString();

        /// <inheritdoc/>
        public ProjectBase? Project => _project;

        /// <inheritdoc/>
        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Connecting to TIA Portal...");

                try
                {
                    _project = null;
                    _session = null;
                    _portal = null;

                    // Try to attach to running TIA Portal instance
                    var processes = TiaPortal.GetProcesses();
                    if (processes.Any())
                    {
                        _portal = processes.First().Attach();

                        // Check for existing local sessions
                        if (_portal.LocalSessions.Any())
                        {
                            _session = _portal.LocalSessions.First();
                            _project = _session.Project;
                        }
                        // Check for existing projects
                        else if (_portal.Projects.Any())
                        {
                            _project = _portal.Projects.First();
                        }

                        _logger?.LogInformation("Attached to existing TIA Portal instance.");
                        return true;
                    }

                    // Start new TIA Portal instance
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

        /// <inheritdoc/>
        public async Task<IReadOnlyList<TiaPortalInstance>> GetInstancesAsync()
        {
            return await Task.Run(() =>
            {
                var instances = new List<TiaPortalInstance>();

                try
                {
                    var processes = TiaPortal.GetProcesses();
                    foreach (var process in processes)
                    {
                        // TiaPortalProcess has an Id property, not ProcessId
                        instances.Add(new TiaPortalInstance(
                            process.Id,
                            process.ToString(),
                            _portal != null && process.Id == (int?)_portal.GetType().GetProperty("Id")?.GetValue(_portal)
                        ));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get TIA Portal instances");
                }

                return instances;
            });
        }

        /// <inheritdoc/>
        public async Task<bool> OpenTiaPortalAsync(bool withUi = false)
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Opening new TIA Portal instance (with UI: {WithUi})...", withUi);

                try
                {
                    _project = null;
                    _session = null;

                    // Dispose existing portal if any
                    _portal?.Dispose();

                    var mode = withUi ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface;
                    _portal = new TiaPortal(mode);
                    _logger?.LogInformation("Started new TIA Portal instance.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open TIA Portal");
                    _portal = null;
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> CloseTiaPortalAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Closing TIA Portal instance...");

                try
                {
                    _project = null;
                    _session = null;

                    if (_portal != null)
                    {
                        _portal.Dispose();
                        _portal = null;
                        _logger?.LogInformation("TIA Portal instance closed.");
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to close TIA Portal");
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ConnectToInstanceAsync(int instanceId)
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Connecting to TIA Portal instance {InstanceId}...", instanceId);

                try
                {
                    _project = null;
                    _session = null;
                    _portal = null;

                    var processes = TiaPortal.GetProcesses();
                    var process = processes.FirstOrDefault(p => p.Id == instanceId);

                    if (process == null)
                    {
                        _logger?.LogWarning("No TIA Portal instance found with ID: {InstanceId}", instanceId);
                        return false;
                    }

                    _portal = process.Attach();

                    // Check for existing local sessions
                    if (_portal.LocalSessions.Any())
                    {
                        _session = _portal.LocalSessions.First();
                        _project = _session.Project;
                    }
                    // Check for existing projects
                    else if (_portal.Projects.Any())
                    {
                        _project = _portal.Projects.First();
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

        /// <inheritdoc/>
        public async Task<bool> DisconnectAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Disconnecting from TIA Portal...");

                try
                {
                    _project = null;
                    _session = null;

                    _portal?.Dispose();
                    _portal = null;

                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to disconnect from TIA Portal");
                    return false;
                }
            });
        }

        /// <inheritdoc/>
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
                    // Close existing project/session if any
                    if (_project != null)
                    {
                        (_project as Project)?.Close();
                        _project = null;
                    }

                    if (_session != null)
                    {
                        _session.Close();
                        _session = null;
                    }

                    var fileInfo = new FileInfo(projectPath);

                    // Check if it's a session file
                    if (IsLocalSessionFile(projectPath))
                    {
                        _session = _portal.LocalSessions.Open(fileInfo);
                        _project = _session?.Project;
                        return _project != null;
                    }

                    // Check if project is already open
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);
                    var existingProjects = GetProjectsList();
                    if (!string.IsNullOrEmpty(projectName) &&
                        existingProjects.Any(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _project = _portal.Projects.FirstOrDefault(p =>
                            p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                        return _project != null;
                    }

                    // Open the project
                    _project = _portal.Projects.OpenWithUpgrade(fileInfo);
                    return _project != null;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open project: {ProjectPath}", projectPath);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
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
                        // Save if requested, then close
                        if (saveChanges && _session.Project != null)
                        {
                            // Sessions auto-save on close
                        }
                        _session.Close();
                        _session = null;
                    }
                    else if (_project is Project project)
                    {
                        if (saveChanges)
                        {
                            project.Save();
                        }
                        project.Close();
                    }

                    _project = null;
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to close project");
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ProjectInfo?> GetProjectInfoAsync()
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    return null;
                }

                try
                {
                    return new ProjectInfo(
                        _project.Name,
                        _project.Path.FullName,
                        Version,
                        File.GetCreationTime(_project.Path.FullName)
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get project info");
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ProjectInfo>> GetOpenProjectsAsync()
        {
            return await Task.Run(() =>
            {
                var projects = new List<ProjectInfo>();

                if (_portal == null)
                {
                    return projects;
                }

                try
                {
                    // Get regular projects
                    if (_portal.Projects != null)
                    {
                        foreach (var project in _portal.Projects)
                        {
                            projects.Add(new ProjectInfo(
                                project.Name,
                                project.Path.FullName,
                                Version,
                                File.GetCreationTime(project.Path.FullName)
                            ));
                        }
                    }

                    // Get local sessions
                    if (_portal.LocalSessions != null)
                    {
                        foreach (var session in _portal.LocalSessions)
                        {
                            if (session.Project != null)
                            {
                                projects.Add(new ProjectInfo(
                                    session.Project.Name + " (Session)",
                                    session.Project.Path.FullName,
                                    Version,
                                    File.GetCreationTime(session.Project.Path.FullName)
                                ));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get open projects");
                }

                return projects;
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync()
        {
            return await Task.Run(() =>
            {
                var devices = new List<DeviceInfo>();

                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return devices;
                }

                try
                {
                    foreach (var device in _project.Devices)
                    {
                        devices.Add(new DeviceInfo(
                            device.Name,
                            device.TypeIdentifier,
                            device.GetIdentification(),
                            GetDeviceIpAddress(device)
                        ));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get devices");
                }

                return devices;
            });
        }

        /// <inheritdoc/>
        public async Task<DeviceItemInfo?> GetDeviceInfoAsync(string deviceName)
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return null;
                }

                try
                {
                    var device = _project.Devices.FirstOrDefault(d =>
                        d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

                    if (device == null)
                    {
                        _logger?.LogWarning("Device not found: {DeviceName}", deviceName);
                        return null;
                    }

                    return new DeviceItemInfo(
                        device.Name,
                        device.TypeIdentifier,
                        device.GetIdentification()
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get device info: {DeviceName}", deviceName);
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<DeviceItemInfo?> GetDeviceItemInfoAsync(string deviceName, string itemName)
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return null;
                }

                try
                {
                    var device = _project.Devices.FirstOrDefault(d =>
                        d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

                    if (device == null)
                    {
                        _logger?.LogWarning("Device not found: {DeviceName}", deviceName);
                        return null;
                    }

                    var item = device.DeviceItems.FirstOrDefault(d =>
                        d.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                    if (item == null)
                    {
                        _logger?.LogWarning("Device item not found: {ItemName}", itemName);
                        return null;
                    }

                    return new DeviceItemInfo(
                        item.Name,
                        item.TypeIdentifier,
                        item.Name
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get device item info: {DeviceName}/{ItemName}", deviceName, itemName);
                    return null;
                }
            });
        }

        /// <inheritdoc/>
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
                        // Sessions auto-save
                        _logger?.LogInformation("Session project auto-saved");
                        return true;
                    }
                    else if (_project is Project project)
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

        /// <inheritdoc/>
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
                    if (_project is Project project)
                    {
                        // Project.SaveAs expects DirectoryInfo (the folder path)
                        // If a file path was provided, extract the directory
                        DirectoryInfo destDirInfo;
                        if (Path.HasExtension(destinationPath))
                        {
                            // It's a file path - get the directory
                            var directory = Path.GetDirectoryName(destinationPath);
                            if (string.IsNullOrEmpty(directory))
                            {
                                destDirInfo = new DirectoryInfo(destinationPath);
                            }
                            else
                            {
                                destDirInfo = new DirectoryInfo(directory);
                            }
                        }
                        else
                        {
                            destDirInfo = new DirectoryInfo(destinationPath);
                        }

                        project.SaveAs(destDirInfo);
                        _logger?.LogInformation("Project saved successfully to: {DestinationPath}", destDirInfo.FullName);
                        return true;
                    }

                    _logger?.LogWarning("Save As is not supported for sessions");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to save project to: {DestinationPath}", destinationPath);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ProjectTreeNode?> GetProjectTreeAsync(int? maxDepth = null)
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return null;
                }

                var depthStr = maxDepth.HasValue ? maxDepth.Value.ToString() : "unlimited";
                _logger?.LogInformation("Getting project tree (max depth: {MaxDepth})", depthStr);

                try
                {
                    return BuildProjectTree(_project, _project.Name, maxDepth, 0);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get project tree");
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PlcInfo>> GetPlcsAsync()
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return (IReadOnlyList<PlcInfo>)Array.Empty<PlcInfo>();
                }

                _logger?.LogInformation("Scanning for PLC devices in project");

                try
                {
                    var plcs = new List<PlcInfo>();
                    var devices = _project.Devices;

                    // Load Siemens assemblies to get PlcSoftware type
                    Type plcSoftwareType = null;
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    foreach (var device in devices)
                    {
                        // Recursively search all device items at all levels
                        SearchDeviceItemsRecursive(device.DeviceItems, device.Name, plcSoftwareType, plcs);
                    }

                    _logger?.LogInformation("Found {PlcCount} PLC device(s)", plcs.Count);
                    return (IReadOnlyList<PlcInfo>)plcs;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error scanning for PLCs");
                    return (IReadOnlyList<PlcInfo>)Array.Empty<PlcInfo>();
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<HmiInfo>> GetHmisAsync()
        {
            return await Task.Run(() =>
            {
                if (_project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return (IReadOnlyList<HmiInfo>)Array.Empty<HmiInfo>();
                }

                _logger?.LogInformation("Scanning for HMI devices in project");

                try
                {
                    var hmis = new List<HmiInfo>();
                    var devices = _project.Devices;

                    // Load Siemens assemblies to get HMI types
                    Type hmiTargetType = null;
                    Type hmiSoftwareType = null;
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();

                                if (hmiTargetType == null)
                                {
                                    var foundType = types.FirstOrDefault(t => t.Name == "HmiTarget");
                                    if (foundType != null)
                                    {
                                        hmiTargetType = foundType;
                                    }
                                }

                                if (hmiSoftwareType == null)
                                {
                                    var foundType = types.FirstOrDefault(t => t.Name == "HmiSoftware");
                                    if (foundType != null)
                                    {
                                        hmiSoftwareType = foundType;
                                    }
                                }

                                if (hmiTargetType != null && hmiSoftwareType != null)
                                {
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    foreach (var device in devices)
                    {
                        // Recursively search all device items at all levels
                        SearchDeviceItemsForHmisRecursive(device.DeviceItems, device.Name, hmiTargetType, hmiSoftwareType, hmis);
                    }

                    _logger?.LogInformation("Found {HmiCount} HMI device(s)", hmis.Count);
                    return (IReadOnlyList<HmiInfo>)hmis;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error scanning for HMIs");
                    return (IReadOnlyList<HmiInfo>)Array.Empty<HmiInfo>();
                }
            });
        }

        /// <summary>
        /// Recursively searches device items for PLCs at all levels of the hierarchy.
        /// </summary>
        private void SearchDeviceItemsRecursive(System.Collections.IEnumerable deviceItems, string parentDeviceName, Type plcSoftwareType, List<PlcInfo> plcs)
        {
            foreach (DeviceItem deviceItem in deviceItems)
            {
                // Debug: log every device item we find (disabled for production)
                // _logger?.LogDebug("Scanning device item: {DeviceName} (parent: {ParentDevice}, type: {ItemType})", deviceItem.Name, parentDeviceName, deviceItem.GetType().Name);

                bool isPlc = false;
                string? typeInfo = null;

                // Method 1: Name-based detection (contains "PLC")
                if (deviceItem.Name.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isPlc = true;
                    typeInfo = "Detected by name pattern";
                }

                // Method 2: Try to get PLC software
                if (plcSoftwareType != null && !isPlc)
                {
                    try
                    {
                        var getMethod = typeof(DeviceItem).GetMethod("GetService");
                        if (getMethod != null)
                        {
                            var genericMethod = getMethod.MakeGenericMethod(plcSoftwareType);
                            var software = genericMethod.Invoke(deviceItem, null);
                            if (software != null)
                            {
                                isPlc = true;
                                typeInfo = "Confirmed PLC software";
                            }
                        }
                    }
                    catch
                    {
                        // Ignore reflection errors
                    }
                }

                // Method 3: Device type identifier check (check the deviceItem's type)
                if (!isPlc)
                {
                    try
                    {
                        // Try to get TypeIdentifier from deviceItem
                        var typeIdProperty = deviceItem.GetType().GetProperty("TypeIdentifier");
                        if (typeIdProperty != null)
                        {
                            var typeIdentifier = typeIdProperty.GetValue(deviceItem)?.ToString();
                            // _logger?.LogDebug("Device item {DeviceName} TypeIdentifier: {TypeId}", deviceItem.Name, typeIdentifier ?? "null");

                            if (!string.IsNullOrEmpty(typeIdentifier) &&
                                (typeIdentifier.Contains("S7-1200") ||
                                typeIdentifier.Contains("S71200") ||
                                typeIdentifier.Contains("S7-1500") ||
                                typeIdentifier.Contains("S71500") ||
                                typeIdentifier.Contains("S7-300") ||
                                typeIdentifier.Contains("S7300") ||
                                typeIdentifier.Contains("S7-400") ||
                                typeIdentifier.Contains("S7400") ||
                                typeIdentifier.Contains("CPU") ||
                                typeIdentifier.Contains("Interface")))
                            {
                                isPlc = true;
                                typeInfo = "Device type: " + typeIdentifier;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore property access errors
                    }
                }

                // Method 4: Check if name contains "CPU" or common CPU naming patterns
                if (!isPlc)
                {
                    if (deviceItem.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        deviceItem.Name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        deviceItem.Name.IndexOf("S7", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isPlc = true;
                        typeInfo = "Detected by CPU name pattern";
                    }
                }

                // Method 5: Fallback - if parent device is ET 200SP or similar, treat device items as potential PLCs
                // This handles cases where users rename their PLCs to custom names
                if (!isPlc)
                {
                    if (parentDeviceName.IndexOf("ET 200", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        parentDeviceName.IndexOf("Station", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // For ET 200SP stations, look for CPU/controller cards
                        // Exclude obvious non-PLC items like ports, interfaces, card readers
                        if (!deviceItem.Name.StartsWith("Port", StringComparison.OrdinalIgnoreCase) &&
                            !deviceItem.Name.StartsWith("PROFINET", StringComparison.OrdinalIgnoreCase) &&
                            !deviceItem.Name.StartsWith("OPC", StringComparison.OrdinalIgnoreCase) &&
                            !deviceItem.Name.StartsWith("Card", StringComparison.OrdinalIgnoreCase) &&
                            !deviceItem.Name.StartsWith("Rack", StringComparison.OrdinalIgnoreCase) &&
                            !deviceItem.Name.StartsWith("BA ", StringComparison.OrdinalIgnoreCase))
                        {
                            isPlc = true;
                            typeInfo = "Detected as CPU in distributed station";
                        }
                    }
                }

                if (isPlc)
                {
                    plcs.Add(new PlcInfo
                    {
                        Name = deviceItem.Name,
                        Type = typeInfo ?? "Unknown",
                        ParentDevice = parentDeviceName,
                        IsConfirmedPlc = plcSoftwareType != null && typeInfo == "Confirmed PLC software"
                    });
                }

                // Recursively search child device items
                try
                {
                    if (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0)
                    {
                        SearchDeviceItemsRecursive(deviceItem.DeviceItems as System.Collections.IEnumerable, parentDeviceName, plcSoftwareType, plcs);
                    }
                }
                catch
                {
                    // Ignore errors accessing child items
                }
            }
        }

        /// <summary>
        /// Recursively searches device items for HMIs at all levels of the hierarchy.
        /// </summary>
        private void SearchDeviceItemsForHmisRecursive(System.Collections.IEnumerable deviceItems, string parentDeviceName, Type hmiTargetType, Type hmiSoftwareType, List<HmiInfo> hmis)
        {
            foreach (DeviceItem deviceItem in deviceItems)
            {
                bool isHmi = false;
                string? typeInfo = null;
                bool isConfirmedHmi = false;

                // Method 1: Name-based detection (contains "HMI")
                if (deviceItem.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isHmi = true;
                    typeInfo = "Detected by name pattern";
                }

                // Method 2: Try to get HMI target service (regular HMI)
                if (hmiTargetType != null)
                {
                    try
                    {
                        var getMethod = typeof(DeviceItem).GetMethod("GetService");
                        if (getMethod != null)
                        {
                            var genericMethod = getMethod.MakeGenericMethod(hmiTargetType);
                            var hmiTarget = genericMethod.Invoke(deviceItem, null);
                            if (hmiTarget != null)
                            {
                                isHmi = true;
                                isConfirmedHmi = true;
                                typeInfo = "HMI_Regular";
                            }
                        }
                    }
                    catch
                    {
                        // Ignore reflection errors
                    }
                }

                // Method 3: Try to get HMI software service (unified HMI)
                if (hmiSoftwareType != null && !isConfirmedHmi)
                {
                    try
                    {
                        var getMethod = typeof(DeviceItem).GetMethod("GetService");
                        if (getMethod != null)
                        {
                            var genericMethod = getMethod.MakeGenericMethod(hmiSoftwareType);
                            var hmiSoftware = genericMethod.Invoke(deviceItem, null);
                            if (hmiSoftware != null)
                            {
                                isHmi = true;
                                isConfirmedHmi = true;
                                typeInfo = "HMI_Unified";
                            }
                        }
                    }
                    catch
                    {
                        // Ignore reflection errors
                    }
                }

                // Method 4: Device type identifier check
                if (!isHmi)
                {
                    try
                    {
                        var typeIdProperty = deviceItem.GetType().GetProperty("TypeIdentifier");
                        if (typeIdProperty != null)
                        {
                            var typeIdentifier = typeIdProperty.GetValue(deviceItem)?.ToString();

                            if (!string.IsNullOrEmpty(typeIdentifier) &&
                                (typeIdentifier.Contains("Comfort", StringComparison.OrdinalIgnoreCase) ||
                                typeIdentifier.Contains("Unified", StringComparison.OrdinalIgnoreCase) ||
                                typeIdentifier.Contains("Basic", StringComparison.OrdinalIgnoreCase) ||
                                typeIdentifier.Contains("Mobile Panel", StringComparison.OrdinalIgnoreCase) ||
                                typeIdentifier.Contains(" TP", StringComparison.OrdinalIgnoreCase) ||
                                typeIdentifier.Contains(" MP", StringComparison.OrdinalIgnoreCase) ||
                                typeIdentifier.Contains("WinCC", StringComparison.OrdinalIgnoreCase)))
                            {
                                isHmi = true;
                                typeInfo = "Device type: " + typeIdentifier;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore property access errors
                    }
                }

                if (isHmi)
                {
                    hmis.Add(new HmiInfo
                    {
                        Name = deviceItem.Name,
                        Type = typeInfo ?? "Unknown",
                        ParentDevice = parentDeviceName,
                        IsConfirmedHmi = isConfirmedHmi
                    });
                }

                // Recursively search child device items
                try
                {
                    if (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0)
                    {
                        SearchDeviceItemsForHmisRecursive(deviceItem.DeviceItems as System.Collections.IEnumerable, parentDeviceName, hmiTargetType, hmiSoftwareType, hmis);
                    }
                }
                catch
                {
                    // Ignore errors accessing child items
                }
            }
        }

        /// <summary>
        /// Releases all resources used by the PortalService.
        /// </summary>
        public void Dispose()
        {
            try
            {
                (_project as Project)?.Close();
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
        }

        #region Helper Methods

        /// <summary>
        /// Determines whether the specified file path is a TIA Portal local session file.
        /// </summary>
        private static bool IsLocalSessionFile(string sessionPath)
        {
            var regex = new Regex(@"\.als\d+$", RegexOptions.IgnoreCase);
            return regex.IsMatch(sessionPath);
        }

        /// <summary>
        /// Determines whether the specified file path is a TIA Portal local project file.
        /// </summary>
        private static bool IsLocalProjectFile(string projectPath)
        {
            var regex = new Regex(@"\.ap\d+$", RegexOptions.IgnoreCase);
            return regex.IsMatch(projectPath);
        }

        /// <summary>
        /// Gets the list of open projects.
        /// </summary>
        private List<ProjectBase> GetProjectsList()
        {
            var projects = new List<ProjectBase>();

            if (_portal?.Projects != null)
            {
                foreach (var project in _portal.Projects)
                {
                    projects.Add(project);
                }
            }

            return projects;
        }

        /// <summary>
        /// Gets the IP address of a device if configured.
        /// </summary>
        private static string? GetDeviceIpAddress(Device device)
        {
            try
            {
                // Try to get network interface from device items
                foreach (var deviceItem in device.DeviceItems)
                {
                    // Look for network interface device items
                    if (deviceItem.Name.Contains("PN", StringComparison.OrdinalIgnoreCase) ||
                        deviceItem.Name.Contains("IE", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Configured"; // Simplified for Phase 1
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }

        /// <summary>
        /// Gets the identification string for a device.
        /// </summary>
        private static string? GetDeviceIdentification(Device device)
        {
            try
            {
                return device.GetIdentification();
            }
            catch
            {
                return device.Name;
            }
        }

        /// <summary>
        /// Builds a hierarchical tree structure from a project or container.
        /// </summary>
        private static ProjectTreeNode BuildProjectTree(object parent, string parentPath, int? maxDepth, int currentDepth)
        {
            var children = new List<ProjectTreeNode>();
            var nodeType = "Unknown";
            var nodeName = string.Empty;

            // Check if we've reached max depth
            if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
            {
                return new ProjectTreeNode(parentPath, nodeType, parentPath, children);
            }

            // Handle different types of objects
            if (parent is ProjectBase project)
            {
                nodeType = "Project";
                nodeName = project.Name;

                // Add devices as children
                foreach (var device in project.Devices)
                {
                    children.Add(BuildProjectTree(device, $"{parentPath}/{device.Name}", maxDepth, currentDepth + 1));
                }
            }
            else if (parent is Device device)
            {
                nodeType = device.TypeIdentifier;
                nodeName = device.Name;

                // Check if this is a PLC device by type identifier
                var typeId = device.TypeIdentifier ?? string.Empty;
                if (typeId.Contains("S7-1200") ||
                    typeId.Contains("S71200") ||
                    typeId.Contains("S7-1500") ||
                    typeId.Contains("S71500") ||
                    typeId.Contains("S7-300") ||
                    typeId.Contains("S7300") ||
                    typeId.Contains("S7-400") ||
                    typeId.Contains("S7400"))
                {
                    nodeType = $"PLC ({device.TypeIdentifier})";
                }

                // Add device items as children
                foreach (var deviceItem in device.DeviceItems)
                {
                    children.Add(BuildProjectTree(deviceItem, $"{parentPath}/{deviceItem.Name}", maxDepth, currentDepth + 1));
                }
            }
            else if (parent is DeviceItem deviceItem)
            {
                nodeType = "DeviceItem";
                nodeName = deviceItem.Name;

                // Try multiple methods to detect if this DeviceItem is a PLC
                bool isPlc = false;
                object plcSoftware = null;

                // Method 1: Check if the name contains "PLC" (common naming convention)
                if (deviceItem.Name.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isPlc = true;
                }

                // Method 2: Try to get PLC software using GetService<PlcSoftware>
                try
                {
                    // Force load TIA assemblies by accessing a known TIA type
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();

                    // Look for PlcSoftware type in all assemblies
                    Type plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType != null)
                    {
                        // Try to call GetService<PlcSoftware>() on the DeviceItem
                        var deviceItemType = deviceItem.GetType();
                        var getServiceMethod = deviceItemType.GetMethod("GetService");

                        if (getServiceMethod != null)
                        {
                            var genericMethod = getServiceMethod.MakeGenericMethod(plcSoftwareType);
                            plcSoftware = genericMethod.Invoke(deviceItem, null);

                            if (plcSoftware != null)
                            {
                                isPlc = true;
                                var nameProperty = plcSoftwareType.GetProperty("Name");
                                var softwareName = nameProperty?.GetValue(plcSoftware) as string ?? "Software";

                                children.Add(new ProjectTreeNode(
                                    softwareName,
                                    "PlcSoftware",
                                    $"{parentPath}/{softwareName}",
                                    new List<ProjectTreeNode>()
                                ));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log for debugging but continue
                    System.Diagnostics.Debug.WriteLine($"Error detecting PLC software: {ex.Message}");
                }

                // Mark as PLC if detected by any method
                if (isPlc)
                {
                    nodeType = "PLC";
                }
            }

            return new ProjectTreeNode(nodeName, nodeType, parentPath, children);
        }

        #endregion
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
