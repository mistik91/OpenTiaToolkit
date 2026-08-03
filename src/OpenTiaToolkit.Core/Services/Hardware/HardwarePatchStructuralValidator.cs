using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal static class HardwarePatchStructuralValidator
    {
        private static readonly HashSet<string> ReferenceProducers = new HashSet<string>(new[]
        {
            "create-device", "create-device-with-item", "plug-item"
        }, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<HardwarePatchValidationIssue> Validate(
            IReadOnlyList<HardwarePatchOperation> operations)
        {
            var issues = new List<HardwarePatchValidationIssue>();
            if (operations == null || operations.Count == 0)
            {
                issues.Add(Issue(-1, "empty-patch", "At least one hardware operation is required."));
                return issues;
            }

            var definitions = CollectResultDefinitions(operations, issues);
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                if (operation == null)
                {
                    issues.Add(Issue(index, "null-operation", $"Operation {index} is null."));
                    continue;
                }

                var op = Normalize(operation.Op);
                if (string.IsNullOrEmpty(op))
                {
                    issues.Add(Issue(index, "missing-op", $"Operation {index} has no op value."));
                    continue;
                }

                foreach (var issue in HardwarePatchOperationContractValidator.Validate(operation, op, index))
                    issues.Add(issue);
                var producer = ResolveTargetReference(operation, index, definitions, issues);
                ValidateExplicitIndex(operation, op, index, issues);
                ValidateCreatedChildBinding(operation, op, index, producer, issues);
                ValidateGeneratedIoBinding(operations, operation, op, index, producer, issues);
            }

            return issues;
        }

        public static HardwarePatchResult CreateBlockedResult(
            IReadOnlyList<HardwarePatchOperation> operations,
            bool dryRun,
            string? description,
            IReadOnlyList<HardwarePatchValidationIssue>? issues = null)
        {
            var validationIssues = issues ?? Validate(operations);
            return new HardwarePatchResult
            {
                DryRun = dryRun,
                Applied = false,
                RolledBack = false,
                SafetyBlocked = true,
                ValidationBlocked = true,
                SafetyMessage = $"Hardware patch structural validation failed with {validationIssues.Count} issue(s).",
                TransactionDescription = string.IsNullOrWhiteSpace(description)
                    ? "OpenTiaToolkit hardware patch"
                    : description,
                ValidationIssues = validationIssues.ToList()
            };
        }

        private static Dictionary<string, ResultDefinition> CollectResultDefinitions(
            IReadOnlyList<HardwarePatchOperation> operations,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            var definitions = new Dictionary<string, ResultDefinition>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                if (operation == null || string.IsNullOrWhiteSpace(operation.ResultId)) continue;

                var id = operation.ResultId!.Trim();
                var op = Normalize(operation.Op);
                if (!ReferenceProducers.Contains(op))
                {
                    issues.Add(Issue(
                        index,
                        "invalid-result-id-producer",
                        $"Operation {index} ({op}) declares resultId '{id}', but only create-device, create-device-with-item, and plug-item produce reusable hardware targets."));
                    continue;
                }

                if (definitions.TryGetValue(id, out var existing))
                {
                    issues.Add(Issue(
                        index,
                        "duplicate-result-id",
                        $"Operation {index} reuses resultId '{id}' already declared by operation {existing.Index}."));
                    continue;
                }

                definitions[id] = new ResultDefinition(index, op);
            }
            return definitions;
        }

        private static ResultDefinition? ResolveTargetReference(
            HardwarePatchOperation operation,
            int index,
            IReadOnlyDictionary<string, ResultDefinition> definitions,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(operation.TargetRef)) return null;

            var targetRef = operation.TargetRef!.Trim();
            if (!definitions.TryGetValue(targetRef, out var producer))
            {
                issues.Add(Issue(
                    index,
                    "unknown-target-ref",
                    $"Operation {index} references unknown targetRef '{targetRef}'. Declare that resultId on an earlier create or plug operation in the same patch."));
                return null;
            }

            if (producer.Index >= index)
            {
                issues.Add(Issue(
                    index,
                    "forward-target-ref",
                    $"Operation {index} references targetRef '{targetRef}' from operation {producer.Index}; targetRef must point to an earlier operation."));
                return null;
            }

            return producer;
        }

        private static void ValidateExplicitIndex(
            HardwarePatchOperation operation,
            string op,
            int index,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if ((op == "connect-node-to-subnet" || op == "disconnect-node") && !operation.NodeIndex.HasValue)
            {
                issues.Add(Issue(index, "missing-node-index", $"Operation {index} ({op}) requires an explicit nodeIndex, including when it is zero."));
            }
            if (op == "create-io-system" && !operation.ControllerIndex.HasValue)
            {
                issues.Add(Issue(index, "missing-controller-index", $"Operation {index} ({op}) requires an explicit controllerIndex, including when it is zero."));
            }
            if ((op == "connect-io-connector" || op == "disconnect-io-connector") && !operation.ConnectorIndex.HasValue)
            {
                issues.Add(Issue(index, "missing-connector-index", $"Operation {index} ({op}) requires an explicit connectorIndex, including when it is zero."));
            }
        }

        private static void ValidateCreatedChildBinding(
            HardwarePatchOperation operation,
            string op,
            int index,
            ResultDefinition? producer,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (producer == null || producer.Op != "create-device-with-item") return;
            if (op != "plug-item" &&
                op != "connect-node-to-subnet" &&
                op != "connect-io-connector" &&
                op != "configure-profinet-device" &&
                op != "set-io-address-start") return;

            if (operation.Target?.ItemPath == null || operation.Target.ItemPath.Length == 0)
            {
                issues.Add(Issue(
                    index,
                    "missing-relative-item-path",
                    $"Operation {index} ({op}) targets create-device-with-item result '{operation.TargetRef}' at the device root. Supply the non-empty relative target.itemPath for the intended parent or child."));
            }
        }

        private static void ValidateGeneratedIoBinding(
            IReadOnlyList<HardwarePatchOperation> operations,
            HardwarePatchOperation operation,
            string op,
            int index,
            ResultDefinition? producer,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (op != "set-io-address-start") return;

            if (producer != null && producer.Op == "plug-item" && operation.ExpectedStartAddress.HasValue)
            {
                issues.Add(Issue(
                    index,
                    "unobservable-generated-address-guard",
                    $"Operation {index} sets expectedStartAddress on an IO child generated by plug result '{operation.TargetRef}'. Its pre-patch address cannot be independently observed; omit the guard."));
            }

            var path = operation.Target?.ItemPath ?? Array.Empty<string>();
            if (path.Length == 0) return;

            var matchingPlugs = operations
                .Take(index)
                .Where(candidate => candidate != null && Normalize(candidate.Op) == "plug-item" &&
                    !string.IsNullOrWhiteSpace(candidate.ItemName) &&
                    path.Any(segment => string.Equals(segment, candidate.ItemName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (matchingPlugs.Count != 1) return;

            var plug = matchingPlugs[0];
            if (string.IsNullOrWhiteSpace(plug.ResultId))
            {
                issues.Add(Issue(
                    index,
                    "missing-plug-result-id",
                    $"Operation {index} targets IO child '{plug.ItemName}', but its earlier plug-item has no resultId. Add a resultId to the plug and bind this operation through it."));
                return;
            }

            if (!string.Equals(operation.TargetRef, plug.ResultId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    index,
                    "generated-child-reference-mismatch",
                    $"Operation {index} targets IO child '{plug.ItemName}' but targetRef is '{operation.TargetRef ?? "<missing>"}'. Use the producing plug resultId '{plug.ResultId}'."));
            }
        }

        private static string Normalize(string? operation) =>
            string.IsNullOrWhiteSpace(operation)
                ? string.Empty
                : operation!.Trim().Replace("_", "-").ToLowerInvariant();

        private static HardwarePatchValidationIssue Issue(int index, string code, string message) =>
            new HardwarePatchValidationIssue
            {
                OperationIndex = index,
                Code = code,
                Message = message
            };

        private sealed class ResultDefinition
        {
            public ResultDefinition(int index, string op)
            {
                Index = index;
                Op = op;
            }

            public int Index { get; }
            public string Op { get; }
        }
    }
}
