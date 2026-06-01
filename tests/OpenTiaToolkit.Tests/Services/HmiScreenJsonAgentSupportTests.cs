using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiScreenJsonAgentSupportTests
    {
        [TestMethod]
        public void CreateGuidance_DryRunSafeIncludesApplyArgs()
        {
            var guidance = HmiScreenJsonAgentSupport.CreateGuidance(new HmiScreenJsonEditResult
            {
                HmiName = "HMI_1",
                ScreenPath = "Screens/Main",
                DryRun = true,
                SafeToApply = true,
                CurrentHash = "hash-1",
                EffectiveFilters = new HmiJsonFilterOptions
                {
                    ItemNames = new[] { "Btn1" },
                    Properties = new[] { "Text", "EventHandlers" }
                }
            });

            Assert.AreEqual("apply-with-dry-run-false", guidance.RecommendedNextAction);
            Assert.IsTrue(guidance.ShouldApply);
            Assert.IsNotNull(guidance.RecommendedNextArgs);
            Assert.AreEqual("HMI_1", guidance.RecommendedNextArgs!.HmiName);
            Assert.AreEqual("Screens/Main", guidance.RecommendedNextArgs.ScreenPath);
            Assert.AreEqual("hash-1", guidance.RecommendedNextArgs.BaseHash);
            Assert.IsFalse(guidance.RecommendedNextArgs.DryRun);
            Assert.IsTrue(guidance.RecommendedNextArgs.Backup);
            CollectionAssert.AreEqual(new[] { "Btn1" }, guidance.RecommendedNextArgs.ItemNames.ToArray());
            CollectionAssert.AreEqual(new[] { "Text", "EventHandlers" }, guidance.RecommendedNextArgs.Properties.ToArray());
        }

        [TestMethod]
        public void CreateGuidance_PatchFailuresIncludeRequiredFiltersAndFixes()
        {
            var guidance = HmiScreenJsonAgentSupport.CreateGuidance(new HmiScreenJsonEditResult
            {
                ScreenPath = "Screens/Main",
                PatchOperationFailedCount = 1,
                PatchOperations = new List<HmiScreenJsonPatchOperationResult>
                {
                    new HmiScreenJsonPatchOperationResult
                    {
                        Succeeded = false,
                        ItemName = "Btn1",
                        Property = "Dynamizations",
                        Message = "Property 'Dynamizations' is outside the current properties filter.",
                        Suggestions = new List<string> { "Add 'Dynamizations' to properties." }
                    }
                }
            });

            Assert.AreEqual("fix-patch-operations", guidance.RecommendedNextAction);
            Assert.IsTrue(guidance.ShouldReRead);
            CollectionAssert.Contains(guidance.RequiredItemNames, "Btn1");
            CollectionAssert.Contains(guidance.RequiredProperties, "Dynamizations");
            CollectionAssert.Contains(guidance.SuggestedFixes, "Add 'Dynamizations' to properties.");
        }

        [TestMethod]
        public void CreateGuidance_StaleBaseRecommendsReRead()
        {
            var guidance = HmiScreenJsonAgentSupport.CreateGuidance(new HmiScreenJsonEditResult
            {
                HmiName = "HMI_1",
                ScreenPath = "Screens/Main",
                DryRun = true,
                SafetyBlocked = true,
                SafetyMessage = "Current screen hash does not match the supplied base hash.",
                CurrentHash = "current-hash",
                EffectiveFilters = new HmiJsonFilterOptions
                {
                    ItemNames = new[] { "Btn1" },
                    Properties = new[] { "Text" }
                }
            });

            Assert.AreEqual("re-read-screen", guidance.RecommendedNextAction);
            Assert.IsTrue(guidance.ShouldReRead);
            Assert.IsNotNull(guidance.RecommendedNextArgs);
            Assert.AreEqual("current-hash", guidance.RecommendedNextArgs!.BaseHash);
            CollectionAssert.Contains(guidance.SuggestedFixes, "Run get-hmi-screen-json or inspect-hmi-screen-json-capabilities with the same screen and filters, then retry with the returned baseHash.");
        }

        [TestMethod]
        public void CreateGuidance_OnlineModeBlockRecommendsOfflineRetry()
        {
            var guidance = HmiScreenJsonAgentSupport.CreateGuidance(new HmiScreenJsonEditResult
            {
                HmiName = "HMI_1",
                ScreenPath = "Screens/Main",
                DryRun = false,
                SafetyBlocked = true,
                SafetyMessage = "Import was blocked because TIA Portal is online.",
                BlockedReason = "tia-online-mode",
                Preflight = new HmiJsonImportResult()
            });

            Assert.AreEqual("take-hmi-offline-and-retry", guidance.RecommendedNextAction);
            Assert.IsFalse(guidance.ShouldApply);
            Assert.IsFalse(guidance.ShouldInspectPreflight);
            CollectionAssert.Contains(guidance.SuggestedFixes, "Take the HMI or project offline in TIA Portal, then replay the same apply command with dry-run=false, the same baseHash, the same filters, and the same operations.");
        }
    }
}
