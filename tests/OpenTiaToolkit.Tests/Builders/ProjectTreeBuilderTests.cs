using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Builders
{
    [TestClass]
    public class ProjectTreeBuilderTests
    {
        [TestMethod]
        public void Build_AddsSyntheticSoftwareNode_WhenDeviceItemExposesPlcSoftwareService()
        {
            // Arrange
            var cpu = new FakeDeviceItem
            {
                Name = "CPU_1",
                PlcSoftware = new PlcSoftware { Name = "Software" },
                DeviceItems = { new FakeDeviceItem { Name = "Interface_1" } }
            };

            var project = new FakeProject
            {
                Name = "FactoryProject",
                Devices =
                {
                    new FakeDevice
                    {
                        Name = "PLC_Rack",
                        DeviceItems = { cpu }
                    }
                }
            };

            var builder = new ProjectTreeBuilder(typeof(PlcSoftware));

            // Act
            var tree = builder.Build(project);

            // Assert
            Assert.AreEqual("FactoryProject", tree.Name);
            Assert.AreEqual(1, tree.Children.Count);

            var rackNode = tree.Children.Single();
            Assert.AreEqual("PLC_Rack", rackNode.Name);

            var cpuNode = rackNode.Children.Single();
            Assert.AreEqual("CPU_1", cpuNode.Name);
            Assert.AreEqual(2, cpuNode.Children.Count);
            Assert.IsTrue(cpuNode.Children.Any(child => child.Name == "Interface_1"));
            Assert.IsTrue(cpuNode.Children.Any(child => child.Name == "Software" && child.Type == "PlcSoftware"));
        }

        [TestMethod]
        public void Build_StopsTraversalAtConfiguredDepth()
        {
            // Arrange
            var project = new FakeProject
            {
                Name = "FactoryProject",
                Devices =
                {
                    new FakeDevice
                    {
                        Name = "PLC_Rack",
                        DeviceItems =
                        {
                            new FakeDeviceItem
                            {
                                Name = "CPU_1",
                                DeviceItems = { new FakeDeviceItem { Name = "Interface_1" } }
                            }
                        }
                    }
                }
            };

            var builder = new ProjectTreeBuilder(typeof(PlcSoftware));

            // Act
            var tree = builder.Build(project, maxDepth: 1);

            // Assert
            Assert.AreEqual(1, tree.Children.Count);
            var rackNode = tree.Children.Single();
            Assert.AreEqual("PLC_Rack", rackNode.Name);
            Assert.AreEqual(0, rackNode.Children.Count);
        }

        [TestMethod]
        public void Build_IncludesDevicesInsideDeviceGroups()
        {
            var project = new FakeProject
            {
                Name = "FactoryProject",
                DeviceGroups =
                {
                    new FakeDeviceGroup
                    {
                        Name = "Field Devices",
                        Devices =
                        {
                            new FakeDevice { Name = "Promag_1" }
                        },
                        Groups =
                        {
                            new FakeDeviceGroup
                            {
                                Name = "Flow",
                                Devices = { new FakeDevice { Name = "Promag_2" } }
                            }
                        }
                    }
                }
            };

            var builder = new ProjectTreeBuilder(typeof(PlcSoftware));

            var tree = builder.Build(project);

            var fieldGroup = tree.Children.Single();
            Assert.AreEqual("Field Devices", fieldGroup.Name);
            Assert.AreEqual("DeviceGroup", fieldGroup.Type);
            Assert.IsTrue(fieldGroup.Children.Any(child => child.Name == "Promag_1" && child.Type == "Device"));
            var flowGroup = fieldGroup.Children.Single(child => child.Name == "Flow");
            Assert.IsTrue(flowGroup.Children.Any(child => child.Name == "Promag_2" && child.Type == "Device"));
        }

        private sealed class FakeProject
        {
            public string Name { get; set; } = string.Empty;

            public List<FakeDevice> Devices { get; } = new List<FakeDevice>();

            public List<FakeDeviceGroup> DeviceGroups { get; } = new List<FakeDeviceGroup>();
        }

        private sealed class FakeDeviceGroup
        {
            public string Name { get; set; } = string.Empty;

            public List<FakeDevice> Devices { get; } = new List<FakeDevice>();

            public List<FakeDeviceGroup> Groups { get; } = new List<FakeDeviceGroup>();
        }

        private sealed class FakeDevice
        {
            public string Name { get; set; } = string.Empty;

            public List<FakeDeviceItem> DeviceItems { get; } = new List<FakeDeviceItem>();
        }

        private sealed class FakeDeviceItem
        {
            public string Name { get; set; } = string.Empty;

            public List<FakeDeviceItem> DeviceItems { get; } = new List<FakeDeviceItem>();

            public PlcSoftware? PlcSoftware { get; set; }

            public T? GetService<T>() where T : class
            {
                if (typeof(T).Name == nameof(SoftwareContainer) && PlcSoftware != null)
                {
                    return new SoftwareContainer { Software = PlcSoftware } as T;
                }

                return PlcSoftware as T;
            }
        }

        private sealed class PlcSoftware
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class SoftwareContainer
        {
            public PlcSoftware? Software { get; set; }
        }
    }
}
