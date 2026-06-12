using OpenTiaToolkit.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiScreenJsonPatchApplierTests
    {
        [TestMethod]
        public void Apply_SetsPropertyAndLayoutWithoutMutatingOriginalScreen()
        {
            var screen = CreateScreen();
            var operations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-property",
                    Item = "Btn1",
                    Property = "Text",
                    Value = "Edited"
                },
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-layout",
                    Item = "Btn1",
                    Left = 24,
                    Top = 36,
                    Width = 140,
                    Height = 44
                }
            };

            var result = HmiScreenJsonPatchApplier.Apply(screen, operations);

            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(5, result.OperationResults.Count(operation => operation.Succeeded));

            var editedItem = result.Screen.Items.Single(item => item.Name == "Btn1");
            Assert.AreEqual("Edited", editedItem.Properties["Text"]);
            Assert.AreEqual("24", editedItem.Properties["Left"]);
            Assert.AreEqual("36", editedItem.Properties["Top"]);
            Assert.AreEqual("140", editedItem.Properties["Width"]);
            Assert.AreEqual("44", editedItem.Properties["Height"]);

            var originalItem = screen.Items.Single(item => item.Name == "Btn1");
            Assert.AreEqual("Base", originalItem.Properties["Text"]);
            Assert.AreEqual("10", originalItem.Properties["Left"]);
        }

        [TestMethod]
        public void Apply_UsesKebabCaseItemNameFromJsonExtensionData()
        {
            using var document = JsonDocument.Parse("[{\"op\":\"set-property\",\"item-name\":\"Btn1\",\"property\":\"Text\",\"value\":\"From Json\"}]");
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["operations"] = document.RootElement.Clone()
            });
            Assert.IsTrue(HmiJsonCommandSupport.TryGetPatchOperationsArgument(args, "operations", out var operations, out var error), error);

            var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), operations!);

            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual("From Json", result.Screen.Items.Single(item => item.Name == "Btn1").Properties["Text"]);
        }

        [TestMethod]
        public void Apply_ReturnsSuggestionWhenItemIsMissing()
        {
            var screen = CreateScreen();
            var operations = new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-property",
                    Item = "Bnt1",
                    Property = "Text",
                    Value = "Edited"
                }
            };

            var result = HmiScreenJsonPatchApplier.Apply(screen, operations);

            Assert.AreEqual(1, result.FailedCount);
            var failedOperation = result.OperationResults.Single(operation => !operation.Succeeded);
            Assert.IsTrue(failedOperation.Message!.Contains("was not found"));
            CollectionAssert.Contains(failedOperation.Suggestions, "Btn1");
        }

        [TestMethod]
        public void Apply_RenamesItemAndRejectsDuplicateName()
        {
            var screen = CreateScreen();
            screen.Items.Add(new HmiJsonItem { Name = "Existing", Type = "HmiButton" });

            var duplicateResult = HmiScreenJsonPatchApplier.Apply(
                screen,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "rename-item",
                        Item = "Btn1",
                        NewName = "Existing"
                    }
                });

            Assert.AreEqual(1, duplicateResult.FailedCount);

            var renameResult = HmiScreenJsonPatchApplier.Apply(
                screen,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "rename-item",
                        Item = "Btn1",
                        NewName = "BtnRenamed"
                    }
                });

            Assert.AreEqual(0, renameResult.FailedCount);
            Assert.IsTrue(renameResult.Screen.Items.Any(item => item.Name == "BtnRenamed"));
        }

        [TestMethod]
        public void Apply_CreatesItemWithTypeGeometryAndProperties()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "create-item",
                        Item = "NewButton",
                        ItemType = "button",
                        Left = 40,
                        Top = 50,
                        Width = 160,
                        Height = 44,
                        Properties = new Dictionary<string, object?>
                        {
                            ["Text"] = "Create Me",
                            ["Visible"] = true
                        }
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var newItem = result.Screen.Items.Single(item => item.Name == "NewButton");
            Assert.AreEqual("button", newItem.Type);
            Assert.AreEqual("40", newItem.Properties["Left"]);
            Assert.AreEqual("50", newItem.Properties["Top"]);
            Assert.AreEqual("160", newItem.Properties["Width"]);
            Assert.AreEqual("44", newItem.Properties["Height"]);
            Assert.AreEqual("Create Me", newItem.Properties["Text"]);
            Assert.AreEqual("True", newItem.Properties["Visible"]);
        }

        [TestMethod]
        public void Apply_CreatesItemUsingKebabCaseItemTypeFromJsonExtensionData()
        {
            using var document = JsonDocument.Parse("[{\"op\":\"create-item\",\"item\":\"NewButton\",\"item-type\":\"button\",\"left\":10}]");
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["operations"] = document.RootElement.Clone()
            });
            Assert.IsTrue(HmiJsonCommandSupport.TryGetPatchOperationsArgument(args, "operations", out var operations, out var error), error);

            var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), operations!);

            Assert.AreEqual(0, result.FailedCount);
            var newItem = result.Screen.Items.Single(item => item.Name == "NewButton");
            Assert.AreEqual("button", newItem.Type);
            Assert.AreEqual("10", newItem.Properties["Left"]);
        }

        [TestMethod]
        public void Apply_DeletesItem()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "delete-item",
                        Item = "Btn1"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            Assert.IsFalse(result.Screen.Items.Any(item => item.Name == "Btn1"));
            var operationResult = result.OperationResults.Single();
            Assert.AreEqual("HmiButton", operationResult.PreviousValue);
            Assert.IsNull(operationResult.NewValue);
        }

        [TestMethod]
        public void Apply_DuplicatesItemWithOverrides()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "duplicate-item",
                        Source = "Btn1",
                        Item = "BtnCopy",
                        Left = 80,
                        Top = 90,
                        Properties = new Dictionary<string, object?>
                        {
                            ["Text"] = "Copied"
                        }
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var copiedItem = result.Screen.Items.Single(item => item.Name == "BtnCopy");
            Assert.AreEqual("HmiButton", copiedItem.Type);
            Assert.AreEqual("Copied", copiedItem.Properties["Text"]);
            Assert.AreEqual("80", copiedItem.Properties["Left"]);
            Assert.AreEqual("90", copiedItem.Properties["Top"]);
            Assert.AreEqual("100", copiedItem.Properties["Width"]);
            Assert.AreEqual("30", copiedItem.Properties["Height"]);

            var originalItem = result.Screen.Items.Single(item => item.Name == "Btn1");
            Assert.AreEqual("Base", originalItem.Properties["Text"]);
        }

        [TestMethod]
        public void Apply_DuplicatesItemUsingKebabCaseSourceAndTargetFromJsonExtensionData()
        {
            using var document = JsonDocument.Parse("[{\"op\":\"duplicate-item\",\"source-item\":\"Btn1\",\"target-item\":\"BtnCopy\",\"left\":66}]");
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["operations"] = document.RootElement.Clone()
            });
            Assert.IsTrue(HmiJsonCommandSupport.TryGetPatchOperationsArgument(args, "operations", out var operations, out var error), error);

            var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), operations!);

            Assert.AreEqual(0, result.FailedCount);
            var copiedItem = result.Screen.Items.Single(item => item.Name == "BtnCopy");
            Assert.AreEqual("66", copiedItem.Properties["Left"]);
        }

        [TestMethod]
        public void Apply_DuplicatesItemUsingTargetNameAlias()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "duplicate-item",
                        Source = "Btn1",
                        TargetName = "BtnCopy"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            Assert.IsTrue(result.Screen.Items.Any(item => item.Name == "BtnCopy"));
        }

        [TestMethod]
        public void Apply_RenamesItemUsingTargetNameAlias()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "rename-item",
                        Item = "Btn1",
                        TargetName = "BtnRenamed"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            Assert.IsTrue(result.Screen.Items.Any(item => item.Name == "BtnRenamed"));
        }

        [TestMethod]
        public void Apply_RenamesItemUsingSourceAndTargetNameAliases()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "rename-item",
                        Source = "Btn1",
                        TargetName = "BtnRenamed"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            Assert.IsTrue(result.Screen.Items.Any(item => item.Name == "BtnRenamed"));
        }

        [TestMethod]
        public void ValidateOperationFilters_ChecksRenameSourceAndTargetItems()
        {
            var issues = HmiScreenJsonPatchApplier.ValidateOperationFilters(
                new HmiJsonFilterOptions
                {
                    ItemNames = new[] { "Btn1" },
                    Properties = new[] { "Text" }
                },
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "rename-item",
                        Source = "Btn1",
                        TargetName = "BtnRenamed"
                    }
                });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("BtnRenamed", issues.Single().ItemName);
        }

        [TestMethod]
        public void ValidateOperationFilters_ReturnsActionablePropertyAndItemIssues()
        {
            var filters = new HmiJsonFilterOptions
            {
                ItemNames = new[] { "Btn1" },
                Properties = new[] { "Text" }
            };

            var issues = HmiScreenJsonPatchApplier.ValidateOperationFilters(
                filters,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-layout",
                        Item = "MissingButton",
                        Left = 100
                    }
                });

            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.Any(issue => issue.Message!.Contains("item-names filter")));
            Assert.IsTrue(issues.Any(issue => issue.Message!.Contains("properties filter")));
        }

        [TestMethod]
        public void ValidateOperationFilters_ChecksDuplicateSourceAndTargetItems()
        {
            var filters = new HmiJsonFilterOptions
            {
                ItemNames = new[] { "Btn1" },
                Properties = new[] { "Text", "Left" }
            };

            var issues = HmiScreenJsonPatchApplier.ValidateOperationFilters(
                filters,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "duplicate-item",
                        Source = "Btn1",
                        Item = "BtnCopy",
                        Left = 120
                    }
                });

            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues.Single().Message!.Contains("BtnCopy"));
        }

        [TestMethod]
        public void MergeOperationFilters_IncludesDuplicateSourceAndTargetItems()
        {
            var filters = HmiScreenJsonPatchApplier.MergeOperationFilters(
                null,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "duplicate-item",
                        Source = "Btn1",
                        Item = "BtnCopy",
                        Left = 120
                    }
                });

            CollectionAssert.Contains(filters.ItemNames.ToList(), "Btn1");
            CollectionAssert.Contains(filters.ItemNames.ToList(), "BtnCopy");
            CollectionAssert.Contains(filters.Properties.ToList(), "Left");
        }

        [TestMethod]
        public void MergeOperationFilters_IncludesDeleteItemTarget()
        {
            var filters = HmiScreenJsonPatchApplier.MergeOperationFilters(
                null,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "delete-item",
                        Item = "Btn1"
                    }
                });

            CollectionAssert.Contains(filters.ItemNames.ToList(), "Btn1");
        }

        [TestMethod]
        public void Apply_SetsAndRemovesEventHandler()
        {
            var setResult = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-event-handler",
                        Item = "Btn1",
                        EventType = "Click",
                        ScriptCode = "return 1;"
                    }
                });

            Assert.AreEqual(0, setResult.FailedCount);
            var item = setResult.Screen.Items.Single(item => item.Name == "Btn1");
            Assert.IsNotNull(item.EventHandlers);
            Assert.AreEqual("Click", item.EventHandlers!.Single().EventType);
            Assert.AreEqual("return 1;", item.EventHandlers.Single().Script!.ScriptCode);

            var removeResult = HmiScreenJsonPatchApplier.Apply(
                setResult.Screen,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "remove-event-handler",
                        Item = "Btn1",
                        EventType = "Click"
                    }
                });

            Assert.AreEqual(0, removeResult.FailedCount);
            Assert.AreEqual(0, removeResult.Screen.Items.Single(item => item.Name == "Btn1").EventHandlers!.Count);
        }

        [TestMethod]
        public void Apply_SetsScreenLevelEventHandlerWhenItemIsOmitted()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-event-handler",
                        EventType = "Loaded",
                        ScriptCode = "return 7;"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            Assert.IsNotNull(result.Screen.EventHandlers);
            Assert.AreEqual("Loaded", result.Screen.EventHandlers!.Single().EventType);
            Assert.AreEqual("return 7;", result.Screen.EventHandlers.Single().Script!.ScriptCode);
            Assert.AreEqual("screen", result.OperationResults.Single().Scope);
            Assert.IsNull(result.OperationResults.Single().ItemName);
        }

        [TestMethod]
        public void Apply_SetsPropertyEventHandlerAndDynamization()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-property-event-handler",
                        Item = "Btn1",
                        Property = "Visible",
                        EventType = "Change",
                        ScriptCode = "return false;"
                    },
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-dynamization",
                        Item = "Btn1",
                        Property = "Left",
                        Tag = "test_int"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var item = result.Screen.Items.Single(item => item.Name == "Btn1");
            Assert.IsNotNull(item.PropertyEventHandlers);
            Assert.AreEqual("Visible", item.PropertyEventHandlers!.Single().PropertyName);
            Assert.AreEqual("Change", item.PropertyEventHandlers.Single().EventType);
            Assert.AreEqual("return false;", item.PropertyEventHandlers.Single().Script!.ScriptCode);
            Assert.IsNotNull(item.Dynamizations);
            Assert.AreEqual("Left", item.Dynamizations!.Single().Property);
            Assert.AreEqual("Tag", item.Dynamizations.Single().Type);
            Assert.AreEqual("test_int", item.Dynamizations.Single().Binding!.Tag);
        }

        [TestMethod]
        public void Apply_SetsFaceplateInterfaceBinding()
        {
            var screen = CreateScreen();
            var item = screen.Items.Single();
            item.Type = "HmiFaceplateContainer";
            item.Interface = new List<HmiJsonInterfaceBinding>
            {
                new HmiJsonInterfaceBinding
                {
                    PropertyName = "AV",
                    Value = "Valve_Old.AV"
                }
            };

            var result = HmiScreenJsonPatchApplier.Apply(
                screen,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-interface",
                        Item = "Btn1",
                        Property = "AV",
                        Value = "Valve_New.AV"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var binding = result.Screen.Items.Single(item => item.Name == "Btn1").Interface!.Single();
            Assert.AreEqual("AV", binding.PropertyName);
            Assert.AreEqual("Valve_New.AV", binding.Value);
            Assert.AreEqual("Interface.AV", result.OperationResults.Single().Property);
        }

        [TestMethod]
        public void Apply_SetsFaceplateInterfaceDynamization()
        {
            var screen = CreateScreen();
            var item = screen.Items.Single();
            item.Name = "Faceplate container_1";
            item.Type = "HmiFaceplateContainer";
            item.Interface = new List<HmiJsonInterfaceBinding>
            {
                new HmiJsonInterfaceBinding
                {
                    PropertyName = "First_Step_NR",
                    Value = "Step_1"
                }
            };

            var result = HmiScreenJsonPatchApplier.Apply(
                screen,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-interface",
                        ItemName = "Faceplate container_1",
                        Property = "First_Step_NR",
                        DynamizationType = "TagParameter",
                        DynamicTagName = "Steps[1]"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var binding = result.Screen.Items.Single(item => item.Name == "Faceplate container_1").Interface!.Single();
            Assert.AreEqual("Step_1", binding.Value);
            Assert.AreEqual("First_Step_NR", binding.Dynamizations!.Single().Property);
            Assert.AreEqual("TagParameter", binding.Dynamizations.Single().Type);
            Assert.AreEqual("Steps[1]", binding.Dynamizations.Single().Binding!.DynamicTagName);
            Assert.AreEqual("Interface.First_Step_NR.Dynamizations.First_Step_NR", result.OperationResults.Single().Property);
        }

        [TestMethod]
        public void Apply_SetsScriptDynamizationFromCompactFields()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-dynamization",
                        Item = "Btn1",
                        Property = "Text",
                        ScriptCode = "return 'dynamic';",
                        Async = true
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var dynamization = result.Screen.Items.Single(item => item.Name == "Btn1").Dynamizations!.Single();
            Assert.AreEqual("Text", dynamization.Property);
            Assert.AreEqual("Script", dynamization.Type);
            Assert.AreEqual(true, dynamization.Script!.Async);
            Assert.AreEqual("return 'dynamic';", dynamization.Script.ScriptCode);
        }

        [TestMethod]
        public void Apply_RemovesPropertyEventHandlerAndDynamization()
        {
            var screen = CreateScreen();
            var item = screen.Items.Single();
            item.PropertyEventHandlers = new List<HmiJsonPropertyEventHandler>
            {
                new HmiJsonPropertyEventHandler
                {
                    PropertyName = "Visible",
                    EventType = "Change",
                    Script = new HmiJsonScriptPayload { ScriptCode = "return false;" }
                }
            };
            item.Dynamizations = new List<HmiJsonDynamization>
            {
                new HmiJsonDynamization
                {
                    Property = "Left",
                    Type = "Tag",
                    Binding = new HmiJsonDynamizationBinding { Tag = "test_int" }
                }
            };

            var result = HmiScreenJsonPatchApplier.Apply(
                screen,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "remove-property-event-handler",
                        Item = "Btn1",
                        Property = "Visible",
                        EventType = "Change"
                    },
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "remove-dynamization",
                        Item = "Btn1",
                        Property = "Left"
                    }
                });

            Assert.AreEqual(0, result.FailedCount);
            var editedItem = result.Screen.Items.Single(item => item.Name == "Btn1");
            Assert.AreEqual(0, editedItem.PropertyEventHandlers!.Count);
            Assert.AreEqual(0, editedItem.Dynamizations!.Count);
        }

        [TestMethod]
        public void ValidateOperationFilters_RequiresCompositeRootProperties()
        {
            var filters = new HmiJsonFilterOptions
            {
                ItemNames = new[] { "Btn1" },
                Properties = new[] { "EventHandlers.Click" }
            };

            var issues = HmiScreenJsonPatchApplier.ValidateOperationFilters(
                filters,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "remove-event-handler",
                        Item = "Btn1",
                        EventType = "Click"
                    }
                });

            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues.Single().Message!.Contains("EventHandlers"));
            CollectionAssert.Contains(issues.Single().Suggestions, "Add 'EventHandlers' to properties.");
            CollectionAssert.Contains(issues.Single().Suggestions, "Composite edits must filter by the root property name: EventHandlers, PropertyEventHandlers, Dynamizations, or Interface.");
        }

        [TestMethod]
        public void MergeOperationFilters_IncludesCompositeRootProperties()
        {
            var filters = HmiScreenJsonPatchApplier.MergeOperationFilters(
                null,
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-event-handler",
                        Item = "Btn1",
                        EventType = "Click",
                        ScriptCode = "return 1;"
                    },
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-dynamization",
                        Item = "Btn1",
                        Property = "Text",
                        Tag = "test_int"
                    },
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "set-interface",
                        Item = "Btn1",
                        Property = "AV",
                        Value = "Valve_A.AV"
                    }
                });

            CollectionAssert.Contains(filters.ItemNames.ToList(), "Btn1");
            CollectionAssert.Contains(filters.Properties.ToList(), "EventHandlers");
            CollectionAssert.Contains(filters.Properties.ToList(), "Dynamizations");
            CollectionAssert.Contains(filters.Properties.ToList(), "Interface");
        }

        [TestMethod]
        public void Apply_UnsupportedOperationReturnsSupportedOperationSuggestion()
        {
            var result = HmiScreenJsonPatchApplier.Apply(
                CreateScreen(),
                new[]
                {
                    new HmiScreenJsonPatchOperation
                    {
                        Op = "teleport-item",
                        Item = "Btn1"
                    }
                });

            Assert.AreEqual(1, result.FailedCount);
            Assert.IsTrue(result.OperationResults.Single().Suggestions.Single().Contains("set-property"));
        }

        private static HmiJsonScreen CreateScreen()
        {
            return new HmiJsonScreen
            {
                Path = "Screens/Main",
                Name = "Main",
                Items =
                {
                    new HmiJsonItem
                    {
                        Name = "Btn1",
                        Type = "HmiButton",
                        Properties =
                        {
                            ["Text"] = "Base",
                            ["Left"] = "10",
                            ["Top"] = "20",
                            ["Width"] = "100",
                            ["Height"] = "30"
                        }
                    }
                }
            };
        }
    }
}
