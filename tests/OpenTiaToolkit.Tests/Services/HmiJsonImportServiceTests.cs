using OpenTiaToolkit.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonImportServiceTests
    {
        [TestMethod]
        public void MergeFilteredEventHandlers_EntryFilterPreservesUnfilteredCurrentHandlers()
        {
            var current = new List<HmiJsonEventHandler>
            {
                EventHandler("Click", "return 1;"),
                EventHandler("Loaded", "return 2;")
            };
            var filtered = new List<HmiJsonEventHandler>
            {
                EventHandler("Click", "return 3;")
            };

            var merged = HmiJsonImportService.MergeFilteredEventHandlers(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "EventHandlers.Click" } });

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("return 3;", merged.Single(handler => handler.EventType == "Click").Script!.ScriptCode);
            Assert.AreEqual("return 2;", merged.Single(handler => handler.EventType == "Loaded").Script!.ScriptCode);
        }

        [TestMethod]
        public void MergeFilteredEventHandlers_RootFilterAllowsUnspecifiedHandlerDeletes()
        {
            var current = new List<HmiJsonEventHandler>
            {
                EventHandler("Click", "return 1;"),
                EventHandler("Loaded", "return 2;")
            };
            var filtered = new List<HmiJsonEventHandler>
            {
                EventHandler("Click", "return 3;")
            };

            var merged = HmiJsonImportService.MergeFilteredEventHandlers(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "EventHandlers" } });

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("Click", merged.Single().EventType);
            Assert.AreEqual("return 3;", merged.Single().Script!.ScriptCode);
        }

        [TestMethod]
        public void MergeFilteredEventHandlers_EntryFilterAllowsTargetedDelete()
        {
            var current = new List<HmiJsonEventHandler>
            {
                EventHandler("Click", "return 1;"),
                EventHandler("Loaded", "return 2;")
            };

            var merged = HmiJsonImportService.MergeFilteredEventHandlers(
                current,
                new List<HmiJsonEventHandler>(),
                new HmiJsonFilterOptions { Properties = new[] { "EventHandlers.Click" } });

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("Loaded", merged.Single().EventType);
            Assert.AreEqual("return 2;", merged.Single().Script!.ScriptCode);
        }

        [TestMethod]
        public void MergeFilteredPropertyEventHandlers_EntryFilterPreservesUnfilteredCurrentHandlers()
        {
            var current = new List<HmiJsonPropertyEventHandler>
            {
                PropertyEventHandler("Visible", "Change", "return true;"),
                PropertyEventHandler("Text", "Change", "return false;")
            };
            var filtered = new List<HmiJsonPropertyEventHandler>
            {
                PropertyEventHandler("Visible", "Change", "return 42;")
            };

            var merged = HmiJsonImportService.MergeFilteredPropertyEventHandlers(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "PropertyEventHandlers.Visible.Change" } });

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("return 42;", merged.Single(handler => handler.PropertyName == "Visible").Script!.ScriptCode);
            Assert.AreEqual("return false;", merged.Single(handler => handler.PropertyName == "Text").Script!.ScriptCode);
        }

        [TestMethod]
        public void MergeFilteredPropertyEventHandlers_EntryFilterAllowsTargetedDelete()
        {
            var current = new List<HmiJsonPropertyEventHandler>
            {
                PropertyEventHandler("Visible", "Change", "return true;"),
                PropertyEventHandler("Text", "Change", "return false;")
            };

            var merged = HmiJsonImportService.MergeFilteredPropertyEventHandlers(
                current,
                new List<HmiJsonPropertyEventHandler>(),
                new HmiJsonFilterOptions { Properties = new[] { "PropertyEventHandlers.Visible.Change" } });

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("Text", merged.Single().PropertyName);
            Assert.AreEqual("return false;", merged.Single().Script!.ScriptCode);
        }

        [TestMethod]
        public void MergeFilteredDynamizations_EntryFilterPreservesUnfilteredCurrentDynamizations()
        {
            var current = new List<HmiJsonDynamization>
            {
                Dynamization("Visible", "Tag_A"),
                Dynamization("Text", "Tag_B")
            };
            var filtered = new List<HmiJsonDynamization>
            {
                Dynamization("Visible", "Tag_C")
            };

            var merged = HmiJsonImportService.MergeFilteredDynamizations(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "Dynamizations.Visible" } });

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("Tag_C", merged.Single(dynamization => dynamization.Property == "Visible").Binding!.Tag);
            Assert.AreEqual("Tag_B", merged.Single(dynamization => dynamization.Property == "Text").Binding!.Tag);
        }

        [TestMethod]
        public void MergeFilteredDynamizations_EntryFilterAllowsTargetedDelete()
        {
            var current = new List<HmiJsonDynamization>
            {
                Dynamization("Visible", "Tag_A"),
                Dynamization("Text", "Tag_B")
            };

            var merged = HmiJsonImportService.MergeFilteredDynamizations(
                current,
                new List<HmiJsonDynamization>(),
                new HmiJsonFilterOptions { Properties = new[] { "Dynamizations.Visible" } });

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("Text", merged.Single().Property);
            Assert.AreEqual("Tag_B", merged.Single().Binding!.Tag);
        }

        [TestMethod]
        public void MergeFilteredInterfaceBindings_EntryFilterPreservesUnfilteredCurrentBindings()
        {
            var current = new List<HmiJsonInterfaceBinding>
            {
                InterfaceBinding("AV", "Valve_A.AV"),
                InterfaceBinding("CMD", "Valve_A.CMD")
            };
            var filtered = new List<HmiJsonInterfaceBinding>
            {
                InterfaceBinding("AV", "Valve_B.AV")
            };

            var merged = HmiJsonImportService.MergeFilteredInterfaceBindings(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "Interface.AV" } });

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("Valve_B.AV", merged.Single(binding => binding.PropertyName == "AV").Value);
            Assert.AreEqual("Valve_A.CMD", merged.Single(binding => binding.PropertyName == "CMD").Value);
        }

        [TestMethod]
        public void MergeFilteredInterfaceBindings_NestedEntryFilterPreservesUnfilteredCurrentBindings()
        {
            var current = new List<HmiJsonInterfaceBinding>
            {
                InterfaceBindingWithTagParameter("First_Step_NR", "Step_1", "Steps[1]"),
                InterfaceBinding("CMD", "Valve_A.CMD")
            };
            var filtered = new List<HmiJsonInterfaceBinding>
            {
                InterfaceBindingWithTagParameter("First_Step_NR", "Step_1", "Steps[2]")
            };

            var merged = HmiJsonImportService.MergeFilteredInterfaceBindings(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "Interface.First_Step_NR.Dynamizations.Value" } });

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual("Steps[2]", merged.Single(binding => binding.PropertyName == "First_Step_NR").Dynamizations!.Single().Binding!.DynamicTagName);
            Assert.AreEqual("Valve_A.CMD", merged.Single(binding => binding.PropertyName == "CMD").Value);
        }

        [TestMethod]
        public void MergeFilteredInterfaceBindings_RootFilterAllowsUnspecifiedBindingDeletes()
        {
            var current = new List<HmiJsonInterfaceBinding>
            {
                InterfaceBinding("AV", "Valve_A.AV"),
                InterfaceBinding("CMD", "Valve_A.CMD")
            };
            var filtered = new List<HmiJsonInterfaceBinding>
            {
                InterfaceBinding("AV", "Valve_B.AV")
            };

            var merged = HmiJsonImportService.MergeFilteredInterfaceBindings(
                current,
                filtered,
                new HmiJsonFilterOptions { Properties = new[] { "Interface" } });

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("AV", merged.Single().PropertyName);
            Assert.AreEqual("Valve_B.AV", merged.Single().Value);
        }

        [TestMethod]
        public void ApplySafetyPolicy_RequireCleanPreflightBlocksSkippedProperties()
        {
            var result = new HmiJsonImportResult
            {
                PropertySkippedCount = 1
            };

            HmiJsonImportService.ApplySafetyPolicy(
                result,
                new HmiJsonImportExecutionOptions { RequireCleanPreflight = true });

            Assert.IsTrue(result.SafetyBlocked);
            StringAssert.Contains(result.SafetyMessage, "1 skipped property change");
        }

        [TestMethod]
        public void ApplySafetyPolicy_FailOnWarningBlocksRuntimeWarnings()
        {
            var result = new HmiJsonImportResult
            {
                WarningCount = 1
            };

            HmiJsonImportService.ApplySafetyPolicy(
                result,
                new HmiJsonImportExecutionOptions { FailOnWarning = true },
                backupPath: "backup.json");

            Assert.IsTrue(result.SafetyBlocked);
            Assert.AreEqual("backup.json", result.BackupPath);
            StringAssert.Contains(result.SafetyMessage, "1 warning");
        }

        [TestMethod]
        public void IsOnlineModeTransactionFailure_DetectsSiemensOnlineModeMessage()
        {
            var ex = new System.InvalidOperationException(
                "Error when calling method 'Transaction' of type 'Siemens.Engineering.ExclusiveAccess'.",
                new System.InvalidOperationException("The operation is not permitted in online mode."));

            Assert.IsTrue(HmiJsonImportService.IsOnlineModeTransactionFailure(ex));
        }

        [TestMethod]
        public void IsOnlineModeTransactionFailure_IgnoresGenericTransactionFailure()
        {
            var ex = new System.InvalidOperationException(
                "Error when calling method 'Transaction' of type 'Siemens.Engineering.ExclusiveAccess'.",
                new System.InvalidOperationException("The project is locked by another operation."));

            Assert.IsFalse(HmiJsonImportService.IsOnlineModeTransactionFailure(ex));
        }

        private static HmiJsonEventHandler EventHandler(string eventType, string scriptCode)
        {
            return new HmiJsonEventHandler
            {
                EventType = eventType,
                Script = new HmiJsonScriptPayload { ScriptCode = scriptCode }
            };
        }

        private static HmiJsonPropertyEventHandler PropertyEventHandler(string propertyName, string eventType, string scriptCode)
        {
            return new HmiJsonPropertyEventHandler
            {
                PropertyName = propertyName,
                EventType = eventType,
                Script = new HmiJsonScriptPayload { ScriptCode = scriptCode }
            };
        }

        private static HmiJsonDynamization Dynamization(string propertyName, string tag)
        {
            return new HmiJsonDynamization
            {
                Property = propertyName,
                Type = "Tag",
                Binding = new HmiJsonDynamizationBinding { Tag = tag }
            };
        }

        private static HmiJsonInterfaceBinding InterfaceBinding(string propertyName, string value)
        {
            return new HmiJsonInterfaceBinding
            {
                PropertyName = propertyName,
                Value = value
            };
        }

        private static HmiJsonInterfaceBinding InterfaceBindingWithTagParameter(string propertyName, string value, string dynamicTagName)
        {
            return new HmiJsonInterfaceBinding
            {
                PropertyName = propertyName,
                Value = value,
                ValueType = "String",
                Dynamizations = new List<HmiJsonDynamization>
                {
                    new HmiJsonDynamization
                    {
                        Property = "Value",
                        Type = "TagParameter",
                        Binding = new HmiJsonDynamizationBinding { DynamicTagName = dynamicTagName }
                    }
                }
            };
        }
    }
}
