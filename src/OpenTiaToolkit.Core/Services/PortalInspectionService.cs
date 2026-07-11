using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Read-only portal inspection operations extracted from PortalService.
    /// This keeps queries separate from portal/project lifecycle management.
    /// </summary>
    internal sealed class PortalInspectionService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21OpennessAdapter _v21Adapter;
        private readonly ILogger? _logger;

        public PortalInspectionService(EngineeringSessionContext sessionContext, ILogger? logger, V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _v21Adapter = v21Adapter ?? new V21OpennessAdapter();
            _logger = logger;
        }

        public string? GetVersion()
        {
            var portal = _sessionContext.Portal;
            if (portal == null) return null;

            try
            {
                var versionProperty = portal.GetType().GetProperty("Version");
                if (versionProperty != null)
                {
                    var version = versionProperty.GetValue(portal);
                    if (version != null)
                    {
                        return version.ToString();
                    }
                }

                var assembly = portal.GetType().Assembly;
                var assemblyName = assembly.GetName().Name;
                if (assemblyName != null && assemblyName.IndexOf("Siemens.Engineering", StringComparison.Ordinal) >= 0)
                {
                    var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    var majorVersion = fileVersion.FileMajorPart;
                    if (majorVersion >= 15)
                    {
                        return $"V{majorVersion}";
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<ProjectInfo?> GetProjectInfoAsync()
        {
            return await Task.Run(() =>
            {
                var project = _sessionContext.Project;
                if (project == null)
                {
                    return null;
                }

                try
                {
                    return new ProjectInfo(
                        project.Name,
                        project.Path.FullName,
                        GetVersion(),
                        File.GetCreationTime(project.Path.FullName));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get project info");
                    return null;
                }
            });
        }

        public async Task<IReadOnlyList<ProjectInfo>> GetOpenProjectsAsync()
        {
            return await Task.Run(() =>
            {
                var projects = new List<ProjectInfo>();
                var portal = _sessionContext.Portal;
                if (portal == null)
                {
                    return (IReadOnlyList<ProjectInfo>)projects;
                }

                try
                {
                    if (portal.Projects != null)
                    {
                        foreach (var project in portal.Projects)
                        {
                            projects.Add(new ProjectInfo(
                                project.Name,
                                project.Path.FullName,
                                GetVersion(),
                                File.GetCreationTime(project.Path.FullName)));
                        }
                    }

                    if (portal.LocalSessions != null)
                    {
                        foreach (var session in portal.LocalSessions)
                        {
                            if (session.Project != null)
                            {
                                projects.Add(new ProjectInfo(
                                    session.Project.Name + " (Session)",
                                    session.Project.Path.FullName,
                                    GetVersion(),
                                    File.GetCreationTime(session.Project.Path.FullName)));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get open projects");
                }

                return (IReadOnlyList<ProjectInfo>)projects;
            });
        }

        public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync()
        {
            return await Task.Run(() =>
            {
                var devices = new List<DeviceInfo>();
                var project = _sessionContext.Project;
                if (project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return (IReadOnlyList<DeviceInfo>)devices;
                }

                try
                {
                    foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(project))
                    {
                        devices.Add(new DeviceInfo(
                            device.Name,
                            device.TypeIdentifier,
                            device.GetIdentification(),
                            GetDeviceIpAddress(device)));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get devices");
                }

                return (IReadOnlyList<DeviceInfo>)devices;
            });
        }

        public async Task<DeviceItemInfo?> GetDeviceInfoAsync(string deviceName)
        {
            return await Task.Run(() =>
            {
                var project = _sessionContext.Project;
                if (project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return null;
                }

                try
                {
                    var device = ProjectDeviceEnumerator.FindDevice(project, deviceName);

                    if (device == null)
                    {
                        _logger?.LogWarning("Device not found: {DeviceName}", deviceName);
                        return null;
                    }

                    return new DeviceItemInfo(
                        device.Name,
                        device.TypeIdentifier,
                        device.GetIdentification());
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get device info: {DeviceName}", deviceName);
                    return null;
                }
            });
        }

        public async Task<DeviceItemInfo?> GetDeviceItemInfoAsync(string deviceName, string itemName)
        {
            return await Task.Run(() =>
            {
                var project = _sessionContext.Project;
                if (project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return null;
                }

                try
                {
                    var device = ProjectDeviceEnumerator.FindDevice(project, deviceName);

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
                        item.Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get device item info: {DeviceName}/{ItemName}", deviceName, itemName);
                    return null;
                }
            });
        }

        public async Task<ProjectTreeNode?> GetProjectTreeAsync(int? maxDepth = null)
        {
            return await Task.Run(() =>
            {
                var project = _sessionContext.Project;
                if (project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return null;
                }

                var depthStr = maxDepth.HasValue ? maxDepth.Value.ToString() : "unlimited";
                _logger?.LogInformation("Getting project tree (max depth: {MaxDepth})", depthStr);

                try
                {
                    var builder = new ProjectTreeBuilder(_v21Adapter.FindTypeByName("PlcSoftware"));
                    return builder.Build(project, maxDepth);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get project tree");
                    return null;
                }
            });
        }

        public async Task<IReadOnlyList<PlcInfo>> GetPlcsAsync()
        {
            return await Task.Run(() =>
            {
                var project = _sessionContext.Project;
                if (project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return (IReadOnlyList<PlcInfo>)Array.Empty<PlcInfo>();
                }

                _logger?.LogInformation("Scanning for PLC devices in project");

                try
                {
                    var plcs = new Dictionary<string, PlcInfo>(StringComparer.OrdinalIgnoreCase);

                    foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(project))
                    {
                        SearchDeviceItemsRecursive(device.DeviceItems, device.Name, device.Name, plcs);
                    }

                    _logger?.LogInformation("Found {PlcCount} PLC device(s)", plcs.Count);
                    return (IReadOnlyList<PlcInfo>)plcs.Values.OrderBy(plc => plc.Path, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error scanning for PLCs");
                    return (IReadOnlyList<PlcInfo>)Array.Empty<PlcInfo>();
                }
            });
        }

        public async Task<IReadOnlyList<HmiInfo>> GetHmisAsync()
        {
            return await Task.Run(() =>
            {
                var project = _sessionContext.Project;
                if (project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return (IReadOnlyList<HmiInfo>)Array.Empty<HmiInfo>();
                }

                _logger?.LogInformation("Scanning for HMI devices in project");

                try
                {
                    var hmis = new Dictionary<string, HmiInfo>(StringComparer.OrdinalIgnoreCase);

                    foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(project))
                    {
                        SearchDeviceItemsForHmisRecursive(device.DeviceItems, device.Name, device.Name, hmis);
                    }

                    _logger?.LogInformation("Found {HmiCount} HMI device(s)", hmis.Count);
                    return (IReadOnlyList<HmiInfo>)hmis.Values.OrderBy(hmi => hmi.Path, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error scanning for HMIs");
                    return (IReadOnlyList<HmiInfo>)Array.Empty<HmiInfo>();
                }
            });
        }

        private void SearchDeviceItemsRecursive(
            System.Collections.IEnumerable deviceItems,
            string parentDeviceName,
            string parentPath,
            IDictionary<string, PlcInfo> plcs)
        {
            foreach (DeviceItem deviceItem in deviceItems)
            {
                var itemPath = parentPath + "/" + deviceItem.Name;
                var software = _v21Adapter.GetPlcSoftware(deviceItem);
                if (software != null)
                {
                    plcs[itemPath] = new PlcInfo
                    {
                        Name = deviceItem.Name,
                        Type = "PLC software: " + software.GetType().FullName,
                        ParentDevice = parentDeviceName,
                        Path = itemPath,
                        IsConfirmedPlc = true
                    };
                }

                try
                {
                    if (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0 &&
                        deviceItem.DeviceItems is System.Collections.IEnumerable childItems)
                    {
                        SearchDeviceItemsRecursive(childItems, parentDeviceName, itemPath, plcs);
                    }
                }
                catch
                {
                    // Ignore errors accessing child items.
                }
            }
        }

        private void SearchDeviceItemsForHmisRecursive(
            System.Collections.IEnumerable deviceItems,
            string parentDeviceName,
            string parentPath,
            IDictionary<string, HmiInfo> hmis)
        {
            foreach (DeviceItem deviceItem in deviceItems)
            {
                var itemPath = parentPath + "/" + deviceItem.Name;
                var software = _v21Adapter.GetUnifiedHmiSoftware(deviceItem);
                if (software != null)
                {
                    hmis[itemPath] = new HmiInfo
                    {
                        Name = deviceItem.Name,
                        Type = "Unified HMI software: " + software.GetType().FullName,
                        ParentDevice = parentDeviceName,
                        Path = itemPath,
                        IsConfirmedHmi = true
                    };
                }

                try
                {
                    if (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0 &&
                        deviceItem.DeviceItems is System.Collections.IEnumerable childItems)
                    {
                        SearchDeviceItemsForHmisRecursive(childItems, parentDeviceName, itemPath, hmis);
                    }
                }
                catch
                {
                    // Ignore errors accessing child items.
                }
            }
        }

        private static string? GetDeviceIpAddress(Device device)
        {
            try
            {
                return device.DeviceItems
                    .SelectMany(item => item.Addresses.Cast<Address>())
                    .Select(address => address.GetAttribute("Address"))
                    .FirstOrDefault(attribute => attribute != null)?.ToString();
            }
            catch
            {
                return null;
            }
        }

    }
}
