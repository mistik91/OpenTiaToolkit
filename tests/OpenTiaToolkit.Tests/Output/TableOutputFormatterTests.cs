using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.CLI.Output;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Output
{
    /// <summary>
    /// Unit tests for TableOutputFormatter.
    /// </summary>
    [TestClass]
    public class TableOutputFormatterTests
    {
        [TestMethod]
        public void Format_ReturnsErrorMessage_WhenFailed()
        {
            // Arrange
            var result = CommandResult.CreateFailure("Error occurred");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Error:");
            StringAssert.Contains(formatted, "Error occurred");
        }

        [TestMethod]
        public void Format_ReturnsMessage_WhenDataIsNull()
        {
            // Arrange
            var result = CommandResult.CreateSuccess(message: "Operation complete");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            Assert.AreEqual("Operation complete", formatted);
        }

        [TestMethod]
        public void Format_ReturnsString_WhenDataIsString()
        {
            // Arrange
            var result = CommandResult.CreateSuccess(data: "Plain text");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            Assert.AreEqual("Plain text", formatted);
        }

        [TestMethod]
        public void FormatProjects_ReturnsTable_WhenProjectsExist()
        {
            // Arrange
            var projects = new List<ProjectInfo>
            {
                new ProjectInfo("Project1", "C:\\Proj1.ap19", "V19", DateTime.Now),
                new ProjectInfo("Project2", "C:\\Proj2.ap19", "V19", DateTime.Now)
            };
            var result = CommandResult.CreateSuccess(projects, "Found 2 projects");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Found 2 project(s)");
            StringAssert.Contains(formatted, "Project1");
            StringAssert.Contains(formatted, "Project2");
            StringAssert.Contains(formatted, "Name");
            StringAssert.Contains(formatted, "Path");
            StringAssert.Contains(formatted, "Version");
        }

        [TestMethod]
        public void FormatProjects_ReturnsMessage_WhenNoProjects()
        {
            // Arrange
            var projects = new List<ProjectInfo>();
            var result = CommandResult.CreateSuccess(projects, "No projects");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            Assert.AreEqual("No projects found.", formatted);
        }

        [TestMethod]
        public void FormatDevices_ReturnsTable_WhenDevicesExist()
        {
            // Arrange
            var devices = new List<DeviceInfo>
            {
                new DeviceInfo("PLC_1", "S7-1500", "CPU 1516-3 PN/DP", "192.168.0.1"),
                new DeviceInfo("HMI_1", "TP1200", "Comfort Panel", "192.168.0.2")
            };
            var result = CommandResult.CreateSuccess(devices, "Found 2 devices");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Found 2 device(s)");
            StringAssert.Contains(formatted, "PLC_1");
            StringAssert.Contains(formatted, "HMI_1");
            StringAssert.Contains(formatted, "Type");
            StringAssert.Contains(formatted, "Identification");
        }

        [TestMethod]
        public void FormatDevices_ReturnsMessage_WhenNoDevices()
        {
            // Arrange
            var devices = new List<DeviceInfo>();
            var result = CommandResult.CreateSuccess(devices, "No devices");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            Assert.AreEqual("No devices found.", formatted);
        }

        [TestMethod]
        public void FormatProjectInfo_ReturnsFormattedInfo()
        {
            // Arrange
            var project = new ProjectInfo("TestProject", "C:\\Test.ap19", "V19", new DateTime(2025, 1, 1));
            var result = CommandResult.CreateSuccess(project, "Project info");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Project Information:");
            StringAssert.Contains(formatted, "TestProject");
            StringAssert.Contains(formatted, "C:\\Test.ap19");
            StringAssert.Contains(formatted, "V19");
        }

        [TestMethod]
        public void FormatDeviceInfo_ReturnsFormattedInfo()
        {
            // Arrange
            var device = new DeviceInfo("PLC_1", "S7-1500", "CPU 1516-3", "192.168.0.1");
            var result = CommandResult.CreateSuccess(device, "Device info");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Device Information:");
            StringAssert.Contains(formatted, "PLC_1");
            StringAssert.Contains(formatted, "S7-1500");
            StringAssert.Contains(formatted, "192.168.0.1");
        }

        [TestMethod]
        public void FormatAnonymousObject_ReturnsKeyValuePairs()
        {
            // Arrange
            var status = new { connected = true, version = "V19", count = 3 };
            var result = CommandResult.CreateSuccess(status, "Status");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "connected");
            StringAssert.Contains(formatted, "version");
            StringAssert.Contains(formatted, "V19");
        }
    }
}
