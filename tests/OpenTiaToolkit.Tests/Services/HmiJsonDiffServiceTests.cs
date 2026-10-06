using OpenTiaToolkit.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonDiffServiceTests
    {
        [TestMethod]
        public async Task CreatePatchAsync_ProducesCreateDeleteUpdateAndRecreateActions()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiJsonDiff", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var basePath = Path.Combine(tempRoot, "base");
                var modifiedPath = Path.Combine(tempRoot, "modified");
                var patchPath = Path.Combine(tempRoot, "patch");

                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                        HmiName = "HMI_1",
                        Screens =
                        {
                            new HmiJsonScreen
                            {
                                Path = "Screens/Main",
                                Name = "Main",
                                Properties = { ["Width"] = "800" },
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "Btn1",
                                        Type = "HmiButton",
                                        Properties = { ["Text"] = "Base", ["Visible"] = "True" }
                                    },
                                    new HmiJsonItem
                                    {
                                        Name = "Legacy",
                                        Type = "HmiButton",
                                        Properties = { ["Text"] = "Legacy" }
                                    }
                                }
                            },
                            new HmiJsonScreen
                            {
                                Path = "Screens/DeleteMe",
                                Name = "DeleteMe"
                            }
                        }
                    },
                    HmiJsonDocumentStore.ResolveExportFilePath(basePath));

                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                        HmiName = "HMI_1",
                        Screens =
                        {
                            new HmiJsonScreen
                            {
                                Path = "Screens/Main",
                                Name = "Main",
                                Properties = { ["Width"] = "800" },
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "Btn1",
                                        Type = "HmiButton",
                                        Properties = { ["Text"] = "Edited", ["Visible"] = "True" }
                                    },
                                    new HmiJsonItem
                                    {
                                        Name = "Legacy",
                                        Type = "HmiText",
                                        Properties = { ["Text"] = "Recreated" }
                                    },
                                    new HmiJsonItem
                                    {
                                        Name = "NewItem",
                                        Type = "HmiButton",
                                        Properties = { ["Text"] = "New" }
                                    }
                                }
                            },
                            new HmiJsonScreen
                            {
                                Path = "Screens/NewScreen",
                                Name = "NewScreen",
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "ScreenItem",
                                        Type = "HmiButton",
                                        Properties = { ["Text"] = "Created" }
                                    }
                                }
                            }
                        }
                    },
                    HmiJsonDocumentStore.ResolveExportFilePath(modifiedPath));

                var service = new HmiJsonDiffService();

                var result = await service.CreatePatchAsync(basePath, modifiedPath, patchPath, filters: null);

                Assert.IsNotNull(result);
                Assert.AreEqual(1, result!.ScreenCreateCount);
                Assert.AreEqual(1, result.ScreenDeleteCount);
                Assert.AreEqual(2, result.ItemCreateCount);
                Assert.AreEqual(1, result.ItemRecreateCount);
                Assert.AreEqual(2, result.PropertyChangeCount);

                var patchDocument = HmiJsonDocumentStore.LoadDocument(patchPath);
                Assert.AreEqual("Patch", patchDocument.DocumentKind);
                Assert.AreEqual(3, patchDocument.Screens.Count);

                var updatedScreen = patchDocument.Screens.Single(screen => screen.Path == "Screens/Main");
                Assert.AreEqual("Update", updatedScreen.Action);
                Assert.IsTrue(updatedScreen.Items.Any(item => item.Name == "Btn1" && item.Action == "Update"));
                Assert.IsTrue(updatedScreen.Items.Any(item => item.Name == "Legacy" && item.Action == "Recreate"));
                Assert.IsTrue(updatedScreen.Items.Any(item => item.Name == "NewItem" && item.Action == "Create"));

                var deletedScreen = patchDocument.Screens.Single(screen => screen.Path == "Screens/DeleteMe");
                Assert.AreEqual("Delete", deletedScreen.Action);

                var createdScreen = patchDocument.Screens.Single(screen => screen.Path == "Screens/NewScreen");
                Assert.AreEqual("Create", createdScreen.Action);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task CreatePatchAsync_TracksEventHandlerAndDynamizationChanges()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiJsonDiff", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var basePath = Path.Combine(tempRoot, "base");
                var modifiedPath = Path.Combine(tempRoot, "modified");
                var patchPath = Path.Combine(tempRoot, "patch");

                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                        HmiName = "HMI_1",
                        Screens =
                        {
                            new HmiJsonScreen
                            {
                                Path = "Screens/Main",
                                Name = "Main",
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "Btn1",
                                        Type = "HmiButton",
                                        EventHandlers = new System.Collections.Generic.List<HmiJsonEventHandler>
                                        {
                                            new HmiJsonEventHandler
                                            {
                                                EventType = "Click",
                                                Script = new HmiJsonScriptPayload
                                                {
                                                    ScriptCode = "return 1;"
                                                }
                                            }
                                        },
                                        PropertyEventHandlers = new List<HmiJsonPropertyEventHandler>
                                        {
                                            new HmiJsonPropertyEventHandler
                                            {
                                                PropertyName = "Visible",
                                                EventType = "Change",
                                                Script = new HmiJsonScriptPayload
                                                {
                                                    ScriptCode = "return false;"
                                                }
                                            }
                                        },
                                        Dynamizations = new List<HmiJsonDynamization>
                                        {
                                            new HmiJsonDynamization
                                            {
                                                Property = "Left",
                                                Type = "Tag",
                                                Binding = new HmiJsonDynamizationBinding
                                                {
                                                    Tag = "test_int"
                                                },
                                                ValueConverter = new HmiJsonValueConverter
                                                {
                                                    IsFormulaSelected = false,
                                                    MappingTable = new HmiJsonMappingTable
                                                    {
                                                        ConditionType = "Equal",
                                                        Entries = new List<HmiJsonMappingTableEntry>
                                                        {
                                                            new HmiJsonMappingTableEntry
                                                            {
                                                                EntryType = "Simple",
                                                                Condition = "1",
                                                                Value = "10"
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    HmiJsonDocumentStore.ResolveExportFilePath(basePath));

                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                        HmiName = "HMI_1",
                        Screens =
                        {
                            new HmiJsonScreen
                            {
                                Path = "Screens/Main",
                                Name = "Main",
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "Btn1",
                                        Type = "HmiButton",
                                        EventHandlers = new System.Collections.Generic.List<HmiJsonEventHandler>
                                        {
                                            new HmiJsonEventHandler
                                            {
                                                EventType = "Click",
                                                Script = new HmiJsonScriptPayload
                                                {
                                                    ScriptCode = "return 2;"
                                                }
                                            }
                                        },
                                        PropertyEventHandlers = new List<HmiJsonPropertyEventHandler>
                                        {
                                            new HmiJsonPropertyEventHandler
                                            {
                                                PropertyName = "Visible",
                                                EventType = "Change",
                                                Script = new HmiJsonScriptPayload
                                                {
                                                    ScriptCode = "return true;"
                                                }
                                            }
                                        },
                                        Dynamizations = new List<HmiJsonDynamization>
                                        {
                                            new HmiJsonDynamization
                                            {
                                                Property = "Left",
                                                Type = "Tag",
                                                Binding = new HmiJsonDynamizationBinding
                                                {
                                                    Tag = "test_int"
                                                },
                                                ValueConverter = new HmiJsonValueConverter
                                                {
                                                    IsFormulaSelected = false,
                                                    MappingTable = new HmiJsonMappingTable
                                                    {
                                                        ConditionType = "Equal",
                                                        Entries = new List<HmiJsonMappingTableEntry>
                                                        {
                                                            new HmiJsonMappingTableEntry
                                                            {
                                                                EntryType = "Simple",
                                                                Condition = "1",
                                                                Value = "20"
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    HmiJsonDocumentStore.ResolveExportFilePath(modifiedPath));

                var service = new HmiJsonDiffService();

                var result = await service.CreatePatchAsync(basePath, modifiedPath, patchPath, filters: null);

                Assert.IsNotNull(result);
                Assert.AreEqual(3, result!.PropertyChangeCount);

                var patchDocument = HmiJsonDocumentStore.LoadDocument(patchPath);
                var updatedItem = patchDocument.Screens.Single().Items.Single();
                Assert.AreEqual("Update", updatedItem.Action);
                Assert.IsNotNull(updatedItem.EventHandlers);
                Assert.AreEqual("return 2;", updatedItem.EventHandlers!.Single().Script!.ScriptCode);
                Assert.IsNotNull(updatedItem.PropertyEventHandlers);
                Assert.AreEqual("return true;", updatedItem.PropertyEventHandlers!.Single().Script!.ScriptCode);
                Assert.IsNotNull(updatedItem.Dynamizations);
                var updatedDynamization = updatedItem.Dynamizations!.Single();
                Assert.AreEqual("test_int", updatedDynamization.Binding!.Tag);
                Assert.AreEqual("20", updatedDynamization.ValueConverter!.MappingTable!.Entries!.Single().Value);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task CreatePatchAsync_TracksFaceplateInterfaceChanges()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiJsonDiff", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var basePath = Path.Combine(tempRoot, "base");
                var modifiedPath = Path.Combine(tempRoot, "modified");
                var patchPath = Path.Combine(tempRoot, "patch");

                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                        HmiName = "HMI_1",
                        Screens =
                        {
                            new HmiJsonScreen
                            {
                                Path = "Screens/Main",
                                Name = "Main",
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "V114",
                                        Type = "HmiFaceplateContainer",
                                        Interface = new List<HmiJsonInterfaceBinding>
                                        {
                                            new HmiJsonInterfaceBinding { PropertyName = "AV", Value = "Valve_Old.AV" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    HmiJsonDocumentStore.ResolveExportFilePath(basePath));

                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                        HmiName = "HMI_1",
                        Screens =
                        {
                            new HmiJsonScreen
                            {
                                Path = "Screens/Main",
                                Name = "Main",
                                Items =
                                {
                                    new HmiJsonItem
                                    {
                                        Name = "V114",
                                        Type = "HmiFaceplateContainer",
                                        Interface = new List<HmiJsonInterfaceBinding>
                                        {
                                            new HmiJsonInterfaceBinding { PropertyName = "AV", Value = "Valve_New.AV" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    HmiJsonDocumentStore.ResolveExportFilePath(modifiedPath));

                var service = new HmiJsonDiffService();

                var result = await service.CreatePatchAsync(basePath, modifiedPath, patchPath, filters: null);

                Assert.IsNotNull(result);
                Assert.AreEqual(1, result!.PropertyChangeCount);

                var patchDocument = HmiJsonDocumentStore.LoadDocument(patchPath);
                var updatedItem = patchDocument.Screens.Single().Items.Single();
                Assert.AreEqual("Update", updatedItem.Action);
                Assert.IsNotNull(updatedItem.Interface);
                Assert.AreEqual("AV", updatedItem.Interface!.Single().PropertyName);
                Assert.AreEqual("Valve_New.AV", updatedItem.Interface.Single().Value);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task CreatePatchAsync_TracksFaceplateInterfaceDynamizationChanges()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiJsonDiff", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var basePath = Path.Combine(tempRoot, "base");
                var modifiedPath = Path.Combine(tempRoot, "modified");
                var patchPath = Path.Combine(tempRoot, "patch");

                HmiJsonDocumentStore.SaveDocument(
                    CreateFaceplateInterfaceDynamizationDocument("Steps[1]"),
                    HmiJsonDocumentStore.ResolveExportFilePath(basePath));

                HmiJsonDocumentStore.SaveDocument(
                    CreateFaceplateInterfaceDynamizationDocument("Steps[2]"),
                    HmiJsonDocumentStore.ResolveExportFilePath(modifiedPath));

                var service = new HmiJsonDiffService();

                var result = await service.CreatePatchAsync(basePath, modifiedPath, patchPath, filters: null);

                Assert.IsNotNull(result);
                Assert.AreEqual(1, result!.PropertyChangeCount);

                var patchDocument = HmiJsonDocumentStore.LoadDocument(patchPath);
                var binding = patchDocument.Screens.Single().Items.Single().Interface!.Single();
                Assert.AreEqual("First_Step_NR", binding.PropertyName);
                Assert.AreEqual("Step_1", binding.Value);
                Assert.AreEqual("String", binding.ValueType);
                Assert.AreEqual("Steps[2]", binding.Dynamizations!.Single().Binding!.DynamicTagName);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        private static HmiJsonDocument CreateFaceplateInterfaceDynamizationDocument(string dynamicTagName)
        {
            return new HmiJsonDocument
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                HmiName = "HMI_1",
                Screens =
                {
                    new HmiJsonScreen
                    {
                        Path = "Screens/Main",
                        Name = "Main",
                        Items =
                        {
                            new HmiJsonItem
                            {
                                Name = "V114",
                                Type = "HmiFaceplateContainer",
                                Interface = new List<HmiJsonInterfaceBinding>
                                {
                                    new HmiJsonInterfaceBinding
                                    {
                                        PropertyName = "First_Step_NR",
                                        ValueType = "String",
                                        Value = "Step_1",
                                        Dynamizations = new List<HmiJsonDynamization>
                                        {
                                            new HmiJsonDynamization
                                            {
                                                Property = "Value",
                                                Type = "TagParameter",
                                                Binding = new HmiJsonDynamizationBinding
                                                {
                                                    DynamicTagName = dynamicTagName
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
