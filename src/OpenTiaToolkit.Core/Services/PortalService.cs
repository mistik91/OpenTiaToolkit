using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Multiuser;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core;
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
        static PortalService()
        {
            // Ensure Openness assemblies can be resolved even when the library is
            // consumed directly instead of via the CLI/MCP entry points.
            TiaPortalAssemblyResolver.Register();
        }

        private TiaPortal? _portal;
        private ProjectBase? _project;
        private LocalSession? _session;
        private bool _ownsCurrentProject;
        private bool _ownsCurrentSession;
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
        public string? Version
        {
            get
            {
                if (_portal == null) return null;

                try
                {
                    // Try to get the Version property via reflection
                    var versionProperty = _portal.GetType().GetProperty("Version");
                    if (versionProperty != null)
                    {
                        var version = versionProperty.GetValue(_portal);
                        if (version != null)
                        {
                            // The Version object has a ToString() that returns "V21", "V20", etc.
                            return version.ToString();
                        }
                    }

                    // Fallback: try to parse from assembly
                    var assembly = _portal.GetType().Assembly;
                    var assemblyName = assembly.GetName().Name;
                    if (assemblyName != null && assemblyName.Contains("Siemens.Engineering"))
                    {
                        // Try to get version from file version
                        var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                        var majorVersion = fileVersion.FileMajorPart;
                        if (majorVersion >= 15) // V15 = 2017, V16 = 2018, etc.
                        {
                            return $"V{majorVersion}";
                        }
                    }

                    // Last resort: return null
                    return null;
                }
                catch
                {
                    return null;
                }
            }
        }

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
                    _ownsCurrentProject = false;
                    _ownsCurrentSession = false;

                    // Try to attach to running TIA Portal instance
                    var processes = TiaPortal.GetProcesses();
                    if (processes.Any())
                    {
                        _portal = processes.First().Attach();
                        _project = null;
                        _session = null;
                        _ownsCurrentProject = false;
                        _ownsCurrentSession = false;

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
                    _ownsCurrentProject = false;
                    _ownsCurrentSession = false;
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
                        _ownsCurrentProject = false;
                        _ownsCurrentSession = false;
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
                    _ownsCurrentProject = false;
                    _ownsCurrentSession = false;

                    var processes = TiaPortal.GetProcesses();
                    var process = processes.FirstOrDefault(p => p.Id == instanceId);

                    if (process == null)
                    {
                        _logger?.LogWarning("No TIA Portal instance found with ID: {InstanceId}", instanceId);
                        return false;
                    }

                    _portal = process.Attach();
                    _project = null;
                    _session = null;
                    _ownsCurrentProject = false;
                    _ownsCurrentSession = false;

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
                    _ownsCurrentProject = false;
                    _ownsCurrentSession = false;

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
                    var fileInfo = new FileInfo(projectPath);
                    var normalizedProjectPath = NormalizeProjectPath(projectPath);
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);

                    if (TrySelectCurrentProject(normalizedProjectPath, projectName))
                    {
                        _logger?.LogInformation("Project is already selected: {ProjectPath}", projectPath);
                        return true;
                    }

                    // Check if it's a session file
                    if (IsLocalSessionFile(projectPath))
                    {
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
                            _session = null;
                            _project = null;
                            _ownsCurrentProject = false;
                            _ownsCurrentSession = false;
                        }
                        else if (_ownsCurrentProject && _project is Project ownedProject)
                        {
                            ownedProject.Close();
                            _project = null;
                            _ownsCurrentProject = false;
                            _ownsCurrentSession = false;
                        }
                        else
                        {
                            _logger?.LogInformation(
                                "The attached TIA Portal instance already has a different project open. Starting a dedicated hidden instance for {ProjectPath}.",
                                projectPath);

                            _portal?.Dispose();
                            _portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                            _project = null;
                            _session = null;
                            _ownsCurrentProject = false;
                            _ownsCurrentSession = false;
                        }
                    }

                    // Open the project
                    _session = null;
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
                        if (_ownsCurrentSession)
                        {
                            // Save if requested, then close
                            if (saveChanges && _session.Project != null)
                            {
                                // Sessions auto-save on close
                            }

                            _session.Close();
                        }
                        else
                        {
                            _logger?.LogInformation("Leaving externally managed local session open.");
                        }

                        _session = null;
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

                    _project = null;
                    _ownsCurrentProject = false;
                    _ownsCurrentSession = false;
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
                    Type? plcSoftwareType = null;
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
                    Type? hmiTargetType = null;
                    Type? hmiSoftwareType = null;
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
        private void SearchDeviceItemsRecursive(System.Collections.IEnumerable deviceItems, string parentDeviceName, Type? plcSoftwareType, List<PlcInfo> plcs)
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

                            if (typeIdentifier is string nonNullTypeIdentifier &&
                                (nonNullTypeIdentifier.Contains("S7-1200") ||
                                nonNullTypeIdentifier.Contains("S71200") ||
                                nonNullTypeIdentifier.Contains("S7-1500") ||
                                nonNullTypeIdentifier.Contains("S71500") ||
                                nonNullTypeIdentifier.Contains("S7-300") ||
                                nonNullTypeIdentifier.Contains("S7300") ||
                                nonNullTypeIdentifier.Contains("S7-400") ||
                                nonNullTypeIdentifier.Contains("S7400") ||
                                nonNullTypeIdentifier.Contains("CPU") ||
                                nonNullTypeIdentifier.Contains("Interface")))
                            {
                                isPlc = true;
                                typeInfo = "Device type: " + nonNullTypeIdentifier;
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
                        if (deviceItem.DeviceItems is System.Collections.IEnumerable childItems)
                        {
                            SearchDeviceItemsRecursive(childItems, parentDeviceName, plcSoftwareType, plcs);
                        }
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
        private void SearchDeviceItemsForHmisRecursive(System.Collections.IEnumerable deviceItems, string parentDeviceName, Type? hmiTargetType, Type? hmiSoftwareType, List<HmiInfo> hmis)
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

                            if (typeIdentifier is string nonNullTypeIdentifier &&
                                (nonNullTypeIdentifier.Contains("Comfort", StringComparison.OrdinalIgnoreCase) ||
                                nonNullTypeIdentifier.Contains("Unified", StringComparison.OrdinalIgnoreCase) ||
                                nonNullTypeIdentifier.Contains("Basic", StringComparison.OrdinalIgnoreCase) ||
                                nonNullTypeIdentifier.Contains("Mobile Panel", StringComparison.OrdinalIgnoreCase) ||
                                nonNullTypeIdentifier.Contains(" TP", StringComparison.OrdinalIgnoreCase) ||
                                nonNullTypeIdentifier.Contains(" MP", StringComparison.OrdinalIgnoreCase) ||
                                nonNullTypeIdentifier.Contains("WinCC", StringComparison.OrdinalIgnoreCase)))
                            {
                                isHmi = true;
                                typeInfo = "Device type: " + nonNullTypeIdentifier;
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
                        if (deviceItem.DeviceItems is System.Collections.IEnumerable childItems)
                        {
                            SearchDeviceItemsForHmisRecursive(childItems, parentDeviceName, hmiTargetType, hmiSoftwareType, hmis);
                        }
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

            _project = null;
            _session = null;
            _portal = null;
            _ownsCurrentProject = false;
            _ownsCurrentSession = false;
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
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                NormalizeProjectPath(left!),
                NormalizeProjectPath(right!),
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
                object? plcSoftware = null;

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
                    Type? plcSoftwareType = null;
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
