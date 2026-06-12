using OpenTiaToolkit.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.CLI.Output;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
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
                new ProjectInfo("Project1", "C:\\Proj1.ap21", "V21", DateTime.Now),
                new ProjectInfo("Project2", "C:\\Proj2.ap21", "V21", DateTime.Now)
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
        public void Format_UsesSharedTreeFormatting_ForProjectAndSoftwareTrees()
        {
            var projectTree = new ProjectTreeNode(
                "Project",
                "Project",
                "Project",
                new[]
                {
                    new ProjectTreeNode("PLC_1", "Device", "Project/PLC_1", Array.Empty<ProjectTreeNode>())
                });
            var softwareTree = new SoftwareTreeNode(
                "PLC_1",
                "PlcSoftware",
                "PLC_1",
                new[]
                {
                    new SoftwareTreeNode("Program blocks", "BlockGroup", "PLC_1/Program blocks", Array.Empty<SoftwareTreeNode>())
                });

            var formattedProject = TableOutputFormatter.Format(CommandResult.CreateSuccess(projectTree, "project"));
            var formattedSoftware = TableOutputFormatter.Format(CommandResult.CreateSuccess(softwareTree, "software"));

            StringAssert.Contains(formattedProject, "Project Tree:");
            StringAssert.Contains(formattedProject, "Project [Project]");
            StringAssert.Contains(formattedProject, "  PLC_1 [Device]");
            StringAssert.Contains(formattedSoftware, "PLC Software Tree:");
            StringAssert.Contains(formattedSoftware, "PLC_1 [PlcSoftware]");
            StringAssert.Contains(formattedSoftware, "  Program blocks [BlockGroup]");
        }

        [TestMethod]
        public void FormatProjectInfo_ReturnsFormattedInfo()
        {
            // Arrange
            var project = new ProjectInfo("TestProject", "C:\\Test.ap21", "V21", new DateTime(2025, 1, 1));
            var result = CommandResult.CreateSuccess(project, "Project info");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Project Information:");
            StringAssert.Contains(formatted, "TestProject");
            StringAssert.Contains(formatted, "C:\\Test.ap21");
            StringAssert.Contains(formatted, "V21");
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
            var status = new { connected = true, version = "V21", count = 3 };
            var result = CommandResult.CreateSuccess(status, "Status");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "connected");
            StringAssert.Contains(formatted, "version");
            StringAssert.Contains(formatted, "V21");
        }

        [TestMethod]
        public void FormatHmiScreens_ReturnsTable_WhenScreensExist()
        {
            // Arrange
            var screens = new List<HmiScreenInfo>
            {
                new HmiScreenInfo { Name = "Home", Path = "Root/Home", Width = 1920, Height = 1080, ItemCount = 3, Enabled = true },
                new HmiScreenInfo { Name = "Alarms", Path = "Root/Alarms", Width = 1920, Height = 1080, ItemCount = 1, Enabled = true }
            };
            var result = CommandResult.CreateSuccess(screens, "Found screens");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Found 2 screen(s)");
            StringAssert.Contains(formatted, "Root/Home");
            StringAssert.Contains(formatted, "Root/Alarms");
            StringAssert.Contains(formatted, "Items");
        }

        [TestMethod]
        public void FormatHmiTags_ReturnsTable()
        {
            // Arrange
            var tags = new List<HmiTagInfo>
            {
                new HmiTagInfo { Path = "Motor.Run", DataType = "Bool", TagTableName = "Default", MemberCount = 0, LoggingTagCount = 1 },
                new HmiTagInfo { Path = "Motor.Speed", DataType = "Real", TagTableName = "Default", MemberCount = 0, LoggingTagCount = 0 }
            };
            var result = CommandResult.CreateSuccess(tags, "Tags");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Found 2 tag(s)");
            StringAssert.Contains(formatted, "Motor.Run");
            StringAssert.Contains(formatted, "Default");
            StringAssert.Contains(formatted, "Logs");
        }

        [TestMethod]
        public void FormatHmiJsonExportResult_ReturnsSummary()
        {
            var result = CommandResult.CreateSuccess(new HmiJsonExportResult
            {
                OutputPath = @"C:\Temp\unified_hmi_export.json",
                ScreenCount = 2,
                ItemCount = 4,
                WarningCount = 1
            });

            var formatted = TableOutputFormatter.Format(result);

            StringAssert.Contains(formatted, "Unified HMI JSON Export:");
            StringAssert.Contains(formatted, "unified_hmi_export.json");
            StringAssert.Contains(formatted, "Screens:       2");
        }

        [TestMethod]
        public void FormatHmiJsonImportResult_ReturnsSummary()
        {
            var result = CommandResult.CreateSuccess(new HmiJsonImportResult
            {
                DryRun = true,
                InputPath = @"C:\Temp\unified_hmi_patch.json",
                ReportPath = @"C:\Temp\unified_hmi_preflight_report.json",
                SafetyBlocked = true,
                SafetyMessage = "Warnings found.",
                BlockedReason = "tia-online-mode",
                ScreenCount = 1,
                ItemCount = 1,
                PropertySetCount = 2,
                PropertyUnchangedCount = 1,
                PropertySkippedCount = 0,
                PropertyFailedCount = 0,
                WarningCount = 1,
                Warnings = new List<string> { "Warnings found." }
            });

            var formatted = TableOutputFormatter.Format(result);

            StringAssert.Contains(formatted, "Unified HMI JSON Preflight:");
            StringAssert.Contains(formatted, "Safety Blocked:        Yes");
            StringAssert.Contains(formatted, "Blocked Reason:        tia-online-mode");
            StringAssert.Contains(formatted, "Warnings:");
        }

        [TestMethod]
        public void FormatHmiScreenInfo_ReturnsDetailedOutput()
        {
            // Arrange
            var screen = new HmiScreenInfo
            {
                Name = "Home",
                Path = "Root/Home",
                Width = 1920,
                Height = 1080,
                Enabled = true,
                ItemCount = 1,
                Items = new List<HmiScreenItemInfo>
                {
                    new HmiScreenItemInfo { Name = "BtnStart", Type = "HmiButton", Left = 10, Top = 20, Width = 120, Height = 40 }
                }
            };
            var result = CommandResult.CreateSuccess(screen, "Screen info");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Unified HMI Screen:");
            StringAssert.Contains(formatted, "Home");
            StringAssert.Contains(formatted, "BtnStart");
            StringAssert.Contains(formatted, "HmiButton");
        }

        [TestMethod]
        public void FormatHmiItemProperties_ReturnsTable()
        {
            // Arrange
            var properties = new List<HmiItemPropertyInfo>
            {
                new HmiItemPropertyInfo { Name = "Visible", Value = "True", ValueType = "Boolean", IsWritable = true, Source = "Property" },
                new HmiItemPropertyInfo { Name = "Left", Value = "10", ValueType = "Int32", IsWritable = true, Source = "Attribute" }
            };
            var result = CommandResult.CreateSuccess(properties, "Properties");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Visible");
            StringAssert.Contains(formatted, "Attribute");
            StringAssert.Contains(formatted, "Boolean");
        }

        [TestMethod]
        public void FormatHmiScreenCopyResult_ReturnsSummary()
        {
            // Arrange
            var copyResult = new HmiScreenCopyResult
            {
                TemplatePath = "Templates/Base",
                TargetPath = "Runtime/Screen01",
                ReplacementApplied = true,
                CopiedItemCount = 3,
                CopiedScreenEventHandlerCount = 1,
                CopiedItemEventHandlerCount = 2,
                Screen = new HmiScreenInfo
                {
                    Name = "Screen01",
                    Path = "Runtime/Screen01",
                    Width = 1280,
                    Height = 720,
                    Enabled = true,
                    ItemCount = 3
                }
            };
            var result = CommandResult.CreateSuccess(copyResult, "Copied");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Unified HMI Screen Copy:");
            StringAssert.Contains(formatted, "Templates/Base");
            StringAssert.Contains(formatted, "Runtime/Screen01");
            StringAssert.Contains(formatted, "Copied Items");
        }

        [TestMethod]
        public void FormatHmiTrendInfo_ReturnsDetails()
        {
            // Arrange
            var trend = new HmiTrendInfo
            {
                TagPath = "Motor.Speed",
                TagName = "Speed",
                LoggingTagName = "Motor_Speed",
                DataLogName = "TrendLog01",
                LoggingMode = "Cyclic",
                Cycle = "T2s",
                StorageFolder = "/Trends",
                LogMaxSize = 2000,
                DurationDays = 365,
                SegmentMaxSize = 500,
                DataLogCreated = true,
                LoggingTagCreated = true
            };
            var result = CommandResult.CreateSuccess(trend, "Trend");

            // Act
            var formatted = TableOutputFormatter.Format(result);

            // Assert
            StringAssert.Contains(formatted, "Unified HMI Trend:");
            StringAssert.Contains(formatted, "Motor.Speed");
            StringAssert.Contains(formatted, "TrendLog01");
            StringAssert.Contains(formatted, "Cyclic");
        }

        [TestMethod]
        public void FormatHmiDataLogDeleteResult_ReturnsBlockingReferencesOnFailure()
        {
            var deletion = new HmiDataLogDeleteResult
            {
                DataLogName = "TrendLog01",
                Error = "Data log is still referenced.",
                References = new List<HmiLoggingTagReferenceInfo>
                {
                    new HmiLoggingTagReferenceInfo
                    {
                        TagPath = "Motor.Speed",
                        LoggingTagName = "Motor_Speed",
                        DataLogName = "TrendLog01"
                    }
                }
            };
            var result = CommandResult.CreateFailure(deletion.Error, deletion);

            var formatted = TableOutputFormatter.Format(result);

            StringAssert.Contains(formatted, "Unified HMI Data Log Deletion:");
            StringAssert.Contains(formatted, "Motor.Speed");
            StringAssert.Contains(formatted, "Motor_Speed");
        }

        [TestMethod]
        public void FormatHmiTrendBulkResult_ReturnsSummaryAndBlockingReferences()
        {
            var bulk = new HmiTrendBulkResult
            {
                Operation = "delete-hmi-data-logs",
                DryRun = true,
                RequestedCount = 1,
                BlockedCount = 1,
                Results = new List<HmiTrendBulkItemResult>
                {
                    new HmiTrendBulkItemResult
                    {
                        DataLogName = "TrendLog01",
                        Status = "Blocked",
                        Message = "Still referenced.",
                        References = new List<HmiLoggingTagReferenceInfo>
                        {
                            new HmiLoggingTagReferenceInfo { TagPath = "Motor.Speed", LoggingTagName = "Motor_Speed" }
                        }
                    }
                }
            };

            var formatted = TableOutputFormatter.Format(CommandResult.CreateFailure("Blocked", bulk));

            StringAssert.Contains(formatted, "Unified HMI Trend Batch:");
            StringAssert.Contains(formatted, "delete-hmi-data-logs");
            StringAssert.Contains(formatted, "Referenced by: Motor.Speed / Motor_Speed");
        }
    }
}
