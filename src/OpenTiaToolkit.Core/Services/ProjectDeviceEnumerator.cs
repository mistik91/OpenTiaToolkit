using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal static class ProjectDeviceEnumerator
    {
        public static IReadOnlyList<Device> EnumerateDevices(object project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var devices = new List<Device>();
            AddDevices(project, devices);
            AddGroupDevices(TiaReflectionHelper.GetEnumerableProperty(project, "DeviceGroups"), devices);
            return devices;
        }

        public static Device? FindDevice(object project, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return null;
            return EnumerateDevices(project).FirstOrDefault(device =>
                string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddGroupDevices(IEnumerable<object>? groups, ICollection<Device> devices)
        {
            if (groups == null) return;

            foreach (var group in groups)
            {
                AddDevices(group, devices);
                AddGroupDevices(TiaReflectionHelper.GetEnumerableProperty(group, "Groups"), devices);
                AddGroupDevices(TiaReflectionHelper.GetEnumerableProperty(group, "DeviceGroups"), devices);
            }
        }

        private static void AddDevices(object owner, ICollection<Device> devices)
        {
            var values = TiaReflectionHelper.GetEnumerableProperty(owner, "Devices");
            if (values == null) return;

            foreach (var value in values)
            {
                if (value is Device device)
                {
                    devices.Add(device);
                }
            }
        }
    }
}
