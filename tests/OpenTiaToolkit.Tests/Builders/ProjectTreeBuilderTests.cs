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

        private sealed class FakeProject
        {
            public string Name { get; set; } = string.Empty;

            public List<FakeDevice> Devices { get; } = new List<FakeDevice>();
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
