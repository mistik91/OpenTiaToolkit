using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    internal static class HmiScreenJsonAgentSupport
    {
        private const string TiaOnlineModeBlockedReason = "tia-online-mode";

        private static readonly string[] SupportedOperations =
        {
            "set-property",
            "set-properties",
            "move-item",
            "resize-item",
            "set-layout",
            "rename-item",
            "create-item",
            "delete-item",
            "duplicate-item",
            "set-event-handler",
            "remove-event-handler",
            "set-property-event-handler",
            "remove-property-event-handler",
            "set-dynamization",
            "remove-dynamization",
            "set-interface"
        };

        private static readonly string[] CompositeProperties =
        {
            "EventHandlers",
            "PropertyEventHandlers",
            "Dynamizations",
            "Interface"
        };

        public static IReadOnlyList<string> SupportedPatchOperations => SupportedOperations;

        public static IReadOnlyList<string> CompositePropertyNames => CompositeProperties;

        public static HmiScreenJsonCapabilities CreateCapabilities()
        {
            return new HmiScreenJsonCapabilities
            {
                SupportedPatchOperations = SupportedOperations,
                CompositeProperties = CompositeProperties,
                OperationHints = new[]
                {
                    Hint("set-property", "Set one scalar screen or item property.", new[] { "property", "value" }, Array.Empty<string>()),
                    Hint("set-properties", "Set several scalar screen or item properties.", new[] { "properties" }, Array.Empty<string>()),
                    Hint("move-item", "Set item Left and/or Top.", new[] { "item", "left and/or top" }, new[] { "Left", "Top" }),
                    Hint("resize-item", "Set item Width and/or Height.", new[] { "item", "width and/or height" }, new[] { "Width", "Height" }),
                    Hint("set-layout", "Set item geometry in one operation.", new[] { "item", "left/top/width/height" }, new[] { "Left", "Top", "Width", "Height" }),
                    Hint("rename-item", "Rename one existing item.", new[] { "item/source", "newName/targetName" }, Array.Empty<string>()),
                    Hint("create-item", "Create one item and optionally set geometry/properties.", new[] { "item", "itemType" }, new[] { "Left", "Top", "Width", "Height" }),
                    Hint("delete-item", "Remove one item from the screen.", new[] { "item" }, Array.Empty<string>()),
                    Hint("duplicate-item", "Clone a source item into a new item name with overrides.", new[] { "source", "item/targetName" }, new[] { "Left", "Top", "Width", "Height" }),
                    Hint("set-event-handler", "Create or replace an event handler on a screen or item.", new[] { "eventType", "scriptCode or eventHandler" }, new[] { "EventHandlers" }),
                    Hint("remove-event-handler", "Remove an event handler from a screen or item.", new[] { "eventType" }, new[] { "EventHandlers" }),
                    Hint("set-property-event-handler", "Create or replace a property event handler.", new[] { "property", "eventType", "scriptCode or propertyEventHandler" }, new[] { "PropertyEventHandlers" }),
                    Hint("remove-property-event-handler", "Remove a property event handler.", new[] { "property", "eventType" }, new[] { "PropertyEventHandlers" }),
                    Hint("set-dynamization", "Create or replace a property dynamization.", new[] { "property", "dynamizationType/tag or dynamization" }, new[] { "Dynamizations" }),
                    Hint("remove-dynamization", "Remove a property dynamization.", new[] { "property" }, new[] { "Dynamizations" }),
                    Hint("set-interface", "Set a faceplate interface value.", new[] { "propertyName/property", "value" }, new[] { "Interface" })
                }
            };
        }

        public static HmiScreenJsonAgentGuidance CreateGuidance(HmiScreenJsonEditResult result)
        {
            var guidance = new HmiScreenJsonAgentGuidance();

            if (result.SafetyBlocked &&
                !string.IsNullOrWhiteSpace(result.SafetyMessage) &&
                result.SafetyMessage!.IndexOf("base hash", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                guidance.RecommendedNextAction = "re-read-screen";
                guidance.ShouldReRead = true;
                AddDistinct(guidance.Reasons, "The supplied base hash does not match the current screen snapshot.");
                AddDistinct(guidance.SuggestedFixes, "Run get-hmi-screen-json or inspect-hmi-screen-json-capabilities with the same screen and filters, then retry with the returned baseHash.");
                guidance.RecommendedNextArgs = CreateRecommendedArgs(result, dryRun: true, backup: true);
                return FinalizeGuidance(guidance);
            }

            if (result.SafetyBlocked && IsOnlineModeBlock(result))
            {
                guidance.RecommendedNextAction = "take-hmi-offline-and-retry";
                guidance.ShouldInspectPreflight = false;
                guidance.ShouldApply = false;
                if (!string.IsNullOrWhiteSpace(result.SafetyMessage))
                {
                    AddDistinct(guidance.Reasons, result.SafetyMessage!);
                }

                AddDistinct(guidance.SuggestedFixes, "Take the HMI or project offline in TIA Portal, then replay the same apply command with dry-run=false, the same baseHash, the same filters, and the same operations.");
                return FinalizeGuidance(guidance);
            }

            if (result.PatchOperationFailedCount > 0)
            {
                guidance.RecommendedNextAction = "fix-patch-operations";
                AddPatchIssueGuidance(result, guidance);
                return FinalizeGuidance(guidance);
            }

            if (result.SafetyBlocked)
            {
                guidance.RecommendedNextAction = "inspect-safety-block";
                guidance.ShouldInspectPreflight = result.Preflight != null;
                if (!string.IsNullOrWhiteSpace(result.SafetyMessage))
                {
                    AddDistinct(guidance.Reasons, result.SafetyMessage!);
                }

                AddDistinct(guidance.SuggestedFixes, "Inspect preflight failures, skipped properties, and warnings before applying.");
                AddWarnings(result, guidance);
                return FinalizeGuidance(guidance);
            }

            if (result.DryRun && result.SafeToApply)
            {
                guidance.RecommendedNextAction = "apply-with-dry-run-false";
                guidance.ShouldApply = true;
                guidance.RecommendedNextArgs = CreateRecommendedArgs(result, dryRun: false, backup: true);
                AddDistinct(guidance.Reasons, "Dry-run completed cleanly and the patch is safe to apply.");
                AddDistinct(guidance.SuggestedFixes, "Replay the same operation request with dry-run=false, the same baseHash, and the same filters.");
                return FinalizeGuidance(guidance);
            }

            if (result.DryRun)
            {
                guidance.RecommendedNextAction = "inspect-preflight";
                guidance.ShouldInspectPreflight = result.Preflight != null;
                guidance.RecommendedNextArgs = CreateRecommendedArgs(result, dryRun: true, backup: true);
                AddDistinct(guidance.Reasons, "Dry-run did not report SafeToApply=true.");
                AddDistinct(guidance.SuggestedFixes, "Inspect preflight failures, skipped properties, and warnings; fix the request and run another dry-run.");
                AddWarnings(result, guidance);
                return FinalizeGuidance(guidance);
            }

            if (result.Applied)
            {
                guidance.RecommendedNextAction = "verify-result";
                guidance.ShouldVerify = true;
                AddDistinct(guidance.Reasons, string.IsNullOrWhiteSpace(result.VerifyExportPath)
                    ? "Patch applied; perform a fresh read to verify because no verify export path was returned."
                    : "Patch applied and verify export was created.");
                AddDistinct(guidance.SuggestedFixes, "Verify the changed screen from VerifiedScreen, VerifyExportPath, or a fresh get-hmi-screen-json read.");
                return FinalizeGuidance(guidance);
            }

            guidance.RecommendedNextAction = "inspect-result";
            guidance.ShouldInspectPreflight = result.Preflight != null;
            AddDistinct(guidance.SuggestedFixes, "Inspect the full command result before deciding whether to retry or re-read.");
            AddWarnings(result, guidance);
            return FinalizeGuidance(guidance);
        }

        private static HmiScreenJsonOperationHint Hint(
            string operation,
            string useWhen,
            IReadOnlyList<string> requiredFields,
            IReadOnlyList<string> recommendedProperties)
        {
            return new HmiScreenJsonOperationHint
            {
                Operation = operation,
                UseWhen = useWhen,
                RequiredFields = requiredFields,
                RecommendedProperties = recommendedProperties
            };
        }

        private static void AddPatchIssueGuidance(HmiScreenJsonEditResult result, HmiScreenJsonAgentGuidance guidance)
        {
            foreach (var operation in result.PatchOperations.Where(operation => !operation.Succeeded))
            {
                if (!string.IsNullOrWhiteSpace(operation.Message))
                {
                    AddDistinct(guidance.Reasons, operation.Message!);
                }

                foreach (var suggestion in operation.Suggestions.Where(suggestion => !string.IsNullOrWhiteSpace(suggestion)))
                {
                    AddDistinct(guidance.SuggestedFixes, suggestion);
                }

                if (!string.IsNullOrWhiteSpace(operation.ItemName))
                {
                    AddDistinct(guidance.RequiredItemNames, operation.ItemName!);
                }

                if (!string.IsNullOrWhiteSpace(operation.Property))
                {
                    AddDistinct(guidance.RequiredProperties, operation.Property!);
                }
            }

            if (guidance.RequiredItemNames.Count > 0 || guidance.RequiredProperties.Count > 0)
            {
                guidance.ShouldReRead = true;
                AddDistinct(guidance.SuggestedFixes, "Re-read the screen with the required item-names/properties so the base hash covers every patch operation.");
            }
        }

        private static void AddWarnings(HmiScreenJsonEditResult result, HmiScreenJsonAgentGuidance guidance)
        {
            foreach (var warning in result.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)))
            {
                AddDistinct(guidance.Reasons, warning);
            }

            if (result.Preflight != null)
            {
                foreach (var warning in result.Preflight.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)))
                {
                    AddDistinct(guidance.Reasons, warning);
                }
            }
        }

        private static bool IsOnlineModeBlock(HmiScreenJsonEditResult result)
        {
            if (string.Equals(result.BlockedReason, TiaOnlineModeBlockedReason, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var message = result.SafetyMessage ?? string.Empty;
            return message.IndexOf("online mode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("TIA Portal is online", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("not permitted in online", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HmiScreenJsonRecommendedPatchArgs? CreateRecommendedArgs(
            HmiScreenJsonEditResult result,
            bool dryRun,
            bool backup)
        {
            if (string.IsNullOrWhiteSpace(result.ScreenPath))
            {
                return null;
            }

            return new HmiScreenJsonRecommendedPatchArgs
            {
                HmiName = result.HmiName,
                ScreenPath = result.ScreenPath,
                BaseHash = FirstNonEmpty(result.ExpectedBaseHash, result.CurrentHash) ?? string.Empty,
                ItemNames = result.EffectiveFilters?.ItemNames ?? Array.Empty<string>(),
                Properties = result.EffectiveFilters?.Properties ?? Array.Empty<string>(),
                DryRun = dryRun,
                Backup = backup
            };
        }

        private static HmiScreenJsonAgentGuidance FinalizeGuidance(HmiScreenJsonAgentGuidance guidance)
        {
            guidance.Reasons = guidance.Reasons
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            guidance.SuggestedFixes = guidance.SuggestedFixes
                .Where(fix => !string.IsNullOrWhiteSpace(fix))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            guidance.RequiredItemNames = guidance.RequiredItemNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            guidance.RequiredProperties = guidance.RequiredProperties
                .Where(property => !string.IsNullOrWhiteSpace(property))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return guidance;
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (!values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(value);
            }
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }
    }
}
