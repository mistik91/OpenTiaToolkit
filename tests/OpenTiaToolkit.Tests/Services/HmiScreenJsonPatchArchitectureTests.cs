using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiScreenJsonPatchArchitectureTests
    {
        private static readonly string[] CanonicalOperations =
        {
            "set-property", "set-properties", "move-item", "resize-item", "set-layout",
            "rename-item", "create-item", "delete-item", "duplicate-item",
            "set-event-handler", "remove-event-handler", "set-property-event-handler",
            "remove-property-event-handler", "set-dynamization", "remove-dynamization", "set-interface"
        };

        [TestMethod]
        public void CatalogAndDefaultRegistry_CoverExactlyTheCanonicalOperationsInOrder()
        {
            CollectionAssert.AreEqual(CanonicalOperations, HmiScreenJsonPatchOperationCatalog.SupportedOperations.ToArray());
            var registered = HmiScreenJsonPatchEngine.CreateDefault().Dispatcher.RegisteredOperationNames.OrderBy(value => value).ToArray();
            var expected = CanonicalOperations.Select(HmiScreenJsonPatchOperationCatalog.Normalize).OrderBy(value => value).ToArray();
            CollectionAssert.AreEqual(expected, registered);
            CollectionAssert.AreEqual(CanonicalOperations, HmiScreenJsonAgentSupport.SupportedPatchOperations.ToArray());
        }

        [TestMethod]
        public void Catalog_NormalizesExistingSpellingsWithoutExpandingTheCatalog()
        {
            var normalized = new[] { "set-property", "setProperty", "SET_PROPERTY", "setproperty" }
                .Select(HmiScreenJsonPatchOperationCatalog.Normalize).Distinct().ToArray();
            CollectionAssert.AreEqual(new[] { "setproperty" }, normalized);
            Assert.AreEqual(string.Empty, HmiScreenJsonPatchOperationCatalog.Normalize("  "));
        }

        [TestMethod]
        public void Dispatcher_RejectsDuplicateNormalizedRegistrations()
        {
            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                new HmiScreenJsonPatchDispatcher(new IHmiScreenJsonPatchOperationHandler[]
                { new StubHandler("set-property"), new StubHandler("SET_PROPERTY") }));
            StringAssert.Contains(exception.Message, "Duplicate patch operation handler registration");
        }

        [TestMethod]
        public void Apply_LaterOperationsObserveRenameAndCreate()
        {
            var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), new[]
            {
                new HmiScreenJsonPatchOperation { Op = "rename-item", Item = "Btn1", NewName = "Renamed" },
                new HmiScreenJsonPatchOperation { Op = "set-property", Item = "Renamed", Property = "Text", Value = "After rename" },
                new HmiScreenJsonPatchOperation { Op = "create-item", Item = "Created", ItemType = "HmiButton" },
                new HmiScreenJsonPatchOperation { Op = "set-property", Item = "Created", Property = "Text", Value = "After create" }
            });
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual("After rename", result.Screen.Items.Single(item => item.Name == "Renamed").Properties["Text"]);
            Assert.AreEqual("After create", result.Screen.Items.Single(item => item.Name == "Created").Properties["Text"]);
        }

        [TestMethod]
        public void Apply_FailureDoesNotStopLaterOperationOrMutateOriginal()
        {
            var original = CreateScreen();
            var result = HmiScreenJsonPatchApplier.Apply(original, new[]
            {
                new HmiScreenJsonPatchOperation { Op = "delete-item", Item = "Missing" },
                new HmiScreenJsonPatchOperation { Op = "set-property", Item = "Btn1", Property = "Text", Value = "Continued" }
            });
            Assert.AreEqual(1, result.FailedCount);
            Assert.AreEqual("Continued", result.Screen.Items.Single().Properties["Text"]);
            Assert.AreEqual("Base", original.Items.Single().Properties["Text"]);
        }

        [TestMethod]
        public void Apply_SetPropertiesPreservesValidEntriesWhenAnotherEntryIsInvalid()
        {
            using var document = JsonDocument.Parse("{}");
            var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), new[]
            {
                new HmiScreenJsonPatchOperation
                {
                    Op = "set-properties", Item = "Btn1",
                    Properties = new Dictionary<string, object?> { ["Invalid"] = document.RootElement.Clone(), ["Text"] = "Valid" }
                }
            });
            Assert.AreEqual(1, result.FailedCount);
            Assert.AreEqual(2, result.OperationResults.Count);
            Assert.AreEqual("Valid", result.Screen.Items.Single().Properties["Text"]);
            Assert.IsFalse(result.Screen.Items.Single().Properties.ContainsKey("Invalid"));
        }

        [TestMethod]
        public void Apply_DuplicateDeepClonesNestedCompositeCollections()
        {
            var screen = CreateScreen();
            screen.Items.Single().EventHandlers = new List<HmiJsonEventHandler>
            { new HmiJsonEventHandler { EventType = "Click", Script = new HmiJsonScriptPayload { ScriptCode = "source" } } };
            screen.Items.Single().Dynamizations = new List<HmiJsonDynamization>
            { new HmiJsonDynamization { Property = "Text", Type = "Tag", Binding = new HmiJsonDynamizationBinding { Tag = "SourceTag" } } };
            var result = HmiScreenJsonPatchApplier.Apply(screen, new[]
            { new HmiScreenJsonPatchOperation { Op = "duplicate-item", Source = "Btn1", Item = "Copy" } });
            var source = result.Screen.Items.Single(item => item.Name == "Btn1");
            var copy = result.Screen.Items.Single(item => item.Name == "Copy");
            copy.EventHandlers!.Single().Script!.ScriptCode = "copy";
            copy.Dynamizations!.Single().Binding!.Tag = "CopyTag";
            Assert.AreEqual("source", source.EventHandlers!.Single().Script!.ScriptCode);
            Assert.AreEqual("SourceTag", source.Dynamizations!.Single().Binding!.Tag);
            Assert.AreEqual("source", screen.Items.Single().EventHandlers!.Single().Script!.ScriptCode);
        }

        [TestMethod]
        public void OperationReader_PreservesCanonicalFieldPrecedenceOverExtensionAliases()
        {
            using var itemAlias = JsonDocument.Parse("\"AliasItem\"");
            using var operationAlias = JsonDocument.Parse("\"delete-item\"");
            var operation = new HmiScreenJsonPatchOperation
            {
                Op = "set-property",
                Item = "Btn1",
                Property = "Text",
                Value = "Canonical",
                ExtensionData = new Dictionary<string, JsonElement>
                { ["operation"] = operationAlias.RootElement.Clone(), ["item-name"] = itemAlias.RootElement.Clone() }
            };
            var reader = new HmiScreenJsonPatchOperationReader();
            Assert.AreEqual("set-property", reader.OperationName(operation));
            Assert.AreEqual("Btn1", reader.ItemName(operation));
            Assert.AreEqual(0, HmiScreenJsonPatchApplier.Apply(CreateScreen(), new[] { operation }).FailedCount);
        }

        [TestMethod]
        public void Apply_FormatsNumericScalarsInvariantlyUnderItalianCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
                var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), new[]
                { new HmiScreenJsonPatchOperation { Op = "set-property", Item = "Btn1", Property = "Opacity", Value = 1.5m } });
                Assert.AreEqual("1.5", result.Screen.Items.Single().Properties["Opacity"]);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [TestMethod]
        public void Apply_ScreenAndItemCompositeTargetsRemainDistinct()
        {
            var result = HmiScreenJsonPatchApplier.Apply(CreateScreen(), new[]
            {
                new HmiScreenJsonPatchOperation { Op = "set-event-handler", EventType = "Loaded", ScriptCode = "screen" },
                new HmiScreenJsonPatchOperation { Op = "set-event-handler", Item = "Btn1", EventType = "Click", ScriptCode = "item" }
            });
            Assert.AreEqual("Loaded", result.Screen.EventHandlers!.Single().EventType);
            Assert.AreEqual("Click", result.Screen.Items.Single().EventHandlers!.Single().EventType);
            CollectionAssert.AreEqual(new[] { "screen", "item" }, result.OperationResults.Select(value => value.Scope).ToArray());
        }

        [TestMethod]
        public void FilterPlanner_MapsEveryOperationFamilyToExpectedItemsAndProperties()
        {
            var operations = new[]
            {
                new HmiScreenJsonPatchOperation { Op = "set-property", Item = "A", Property = "Text", Value = "x" },
                new HmiScreenJsonPatchOperation { Op = "move-item", Item = "B", Left = 1 },
                new HmiScreenJsonPatchOperation { Op = "create-item", Item = "C", ItemType = "button", Width = 2, Properties = new Dictionary<string, object?> { ["Visible"] = true } },
                new HmiScreenJsonPatchOperation { Op = "duplicate-item", Source = "C", Item = "D", Height = 3 },
                new HmiScreenJsonPatchOperation { Op = "set-event-handler", Item = "E", EventType = "Click", ScriptCode = "x" },
                new HmiScreenJsonPatchOperation { Op = "set-property-event-handler", Item = "F", Property = "Text", EventType = "Change", ScriptCode = "x" },
                new HmiScreenJsonPatchOperation { Op = "set-dynamization", Item = "G", Property = "Text", Tag = "T" },
                new HmiScreenJsonPatchOperation { Op = "set-interface", Item = "H", Property = "AV", Value = "x" },
                new HmiScreenJsonPatchOperation { Op = "rename-item", Source = "H", TargetName = "I" },
                new HmiScreenJsonPatchOperation { Op = "delete-item", Item = "J" }
            };
            var merged = HmiScreenJsonPatchApplier.MergeOperationFilters(null, operations);
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" }, merged.ItemNames.ToArray());
            CollectionAssert.AreEqual(new[] { "Text", "Left", "Visible", "Width", "Height", "EventHandlers", "PropertyEventHandlers", "Dynamizations", "Interface" }, merged.Properties.ToArray());
        }

        private static HmiJsonScreen CreateScreen() => new HmiJsonScreen
        {
            Name = "Main",
            Path = "Screens/Main",
            Items = { new HmiJsonItem { Name = "Btn1", Type = "HmiButton", Properties = { ["Text"] = "Base" } } }
        };

        private sealed class StubHandler : IHmiScreenJsonPatchOperationHandler
        {
            public StubHandler(params string[] names) { OperationNames = names; }
            public IReadOnlyList<string> OperationNames { get; }
            public void Apply(HmiScreenJsonPatchOperationContext context) { }
        }
    }
}
