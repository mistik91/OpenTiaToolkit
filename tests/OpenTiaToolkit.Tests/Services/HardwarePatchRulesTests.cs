using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.Hardware;
using OpenTiaToolkit.Core.Services.Hardware.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HardwarePatchRulesTests
    {
        [TestMethod]
        public void AttributeValueConverter_PreservesSupportedJsonScalarsAndInvariantComparison()
        {
            using (var json = JsonDocument.Parse("{\"i\":12,\"l\":2147483648,\"d\":1.5,\"b\":true,\"s\":\"x\",\"n\":null}"))
            {
                Assert.AreEqual(12, HardwareAttributeValueConverter.ConvertJsonValue(json.RootElement.GetProperty("i")));
                Assert.AreEqual(2147483648L, HardwareAttributeValueConverter.ConvertJsonValue(json.RootElement.GetProperty("l")));
                Assert.AreEqual(1.5d, HardwareAttributeValueConverter.ConvertJsonValue(json.RootElement.GetProperty("d")));
                Assert.AreEqual(true, HardwareAttributeValueConverter.ConvertJsonValue(json.RootElement.GetProperty("b")));
                Assert.AreEqual("x", HardwareAttributeValueConverter.ConvertJsonValue(json.RootElement.GetProperty("s")));
                Assert.AreEqual(string.Empty, HardwareAttributeValueConverter.ConvertJsonValue(json.RootElement.GetProperty("n")));
            }
            Assert.IsTrue(HardwareAttributeValueConverter.ExpectedValueMatches(12, "12"));
            Assert.IsFalse(HardwareAttributeValueConverter.ExpectedValueMatches(12, "12.0"));
        }

        [TestMethod]
        public void OperationCatalog_RegistersEveryOperationExactlyOnceAndNormalizesAliases()
        {
            var handlers = new IHardwarePatchOperationHandler[]
            {
                new HardwareStructurePatchHandler(), new HardwareAttributePatchHandler(), new HardwareDrivePatchHandler(),
                new HardwareGsdPatchHandler(), new HardwareNetworkPatchHandler()
            };
            var catalog = new HardwarePatchOperationCatalog(handlers);
            Assert.AreEqual(26, catalog.OperationNames.Count);
            Assert.AreEqual("set-io-address-start", catalog.Resolve("  SET_IO_ADDRESS_START ").Name);
            Assert.ThrowsException<InvalidOperationException>(() =>
                new HardwarePatchOperationCatalog(new IHardwarePatchOperationHandler[] { handlers[0], handlers[0] }));
        }

        [TestMethod]
        public void SafetyPolicy_PreservesDestructiveConfirmationAndHardensUnguardedGsdApply()
        {
            var catalog = new HardwarePatchOperationCatalog(new IHardwarePatchOperationHandler[]
            {
                new HardwareStructurePatchHandler(), new HardwareGsdPatchHandler()
            });
            Assert.IsTrue(HardwarePatchSafetyPolicy.RequiresConfirmation(
                catalog.Resolve("delete-item"), new HardwarePatchOperation { Op = "delete-item" }, dryRun: true));
            var setGsd = catalog.Resolve("set-gsd-parameter-data");
            Assert.IsFalse(HardwarePatchSafetyPolicy.RequiresConfirmation(setGsd,
                new HardwarePatchOperation { Op = "set-gsd-parameter-data" }, dryRun: true));
            Assert.IsTrue(HardwarePatchSafetyPolicy.RequiresConfirmation(setGsd,
                new HardwarePatchOperation { Op = "set-gsd-parameter-data" }, dryRun: false));
            Assert.IsFalse(HardwarePatchSafetyPolicy.RequiresConfirmation(setGsd,
                new HardwarePatchOperation { Op = "set-gsd-parameter-data", ExpectedDataHex = "00" }, dryRun: false));
        }

        [TestMethod]
        public void TransactionPolicy_CommitsOnlySuccessfulApply()
        {
            Assert.IsFalse(HardwarePatchTransactionPolicy.ShouldCommit(dryRun: true, allOperationsSucceeded: true));
            Assert.IsFalse(HardwarePatchTransactionPolicy.ShouldCommit(dryRun: false, allOperationsSucceeded: false));
            Assert.IsTrue(HardwarePatchTransactionPolicy.ShouldCommit(dryRun: false, allOperationsSucceeded: true));
        }

        [TestMethod]
        public void ResultReferences_ArePatchLocalCaseInsensitiveAndRejectDuplicates()
        {
            var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HardwareResultReferenceRules.Add("CreatedDrive", "Drive/Item", references);
            Assert.AreEqual("Drive/Item", references["createddrive"]);
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                HardwareResultReferenceRules.Add("CREATEDDRIVE", "Other", references));
            StringAssert.Contains(ex.Message, "Duplicate resultId");
            Assert.AreEqual(0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase).Count,
                "A new patch execution starts with no references.");
        }

        [TestMethod]
        public void StructuralValidator_AcceptsCompleteGeneratedChildTopologyBatch()
        {
            var operations = new[]
            {
                new HardwarePatchOperation { Op = "create-device-with-item", ResultId = "station", TypeIdentifier = "OrderNumber:IM", DeviceName = "Station", ItemName = "Station" },
                new HardwarePatchOperation { Op = "plug-item", ResultId = "inputs", TargetRef = "station", Target = Path("Rail_0"), TypeIdentifier = "OrderNumber:DI", ItemName = "Inputs", PositionNumber = 2 },
                new HardwarePatchOperation { Op = "plug-item", ResultId = "outputs", TargetRef = "station", Target = Path("Rail_0"), TypeIdentifier = "OrderNumber:DQ", ItemName = "Outputs", PositionNumber = 4 },
                new HardwarePatchOperation { Op = "set-io-address-start", TargetRef = "inputs", Target = Path("Inputs"), AddressIndex = 0, StartAddress = 96 },
                new HardwarePatchOperation { Op = "set-io-address-start", TargetRef = "outputs", Target = Path("Outputs"), AddressIndex = 0, StartAddress = 152 },
                new HardwarePatchOperation { Op = "connect-node-to-subnet", TargetRef = "station", Target = Path("Station", "PROFINET interface"), NodeIndex = 0, SubnetName = "PN" },
                new HardwarePatchOperation { Op = "connect-io-connector", TargetRef = "station", Target = Path("Station", "PROFINET interface"), ConnectorIndex = 0, IoSystemName = "IO" },
                new HardwarePatchOperation { Op = "configure-profinet-device", TargetRef = "station", Target = Path("Station", "PROFINET interface"), IpAddress = "192.168.0.10" }
            };

            Assert.AreEqual(0, HardwarePatchStructuralValidator.Validate(operations).Count);
        }

        [TestMethod]
        public void StructuralValidator_ReturnsActionableIssuesForInvalidReferencesAndIndexes()
        {
            var operations = new[]
            {
                new HardwarePatchOperation { Op = "create-device-with-item", ResultId = "station", TypeIdentifier = "OrderNumber:IM", DeviceName = "Station", ItemName = "Station" },
                new HardwarePatchOperation { Op = "plug-item", ResultId = "inputs", TargetRef = "station", TypeIdentifier = "OrderNumber:DI", ItemName = "Inputs", PositionNumber = 2 },
                new HardwarePatchOperation { Op = "set-io-address-start", TargetRef = "station", Target = Path("Inputs", "Inputs"), AddressIndex = 0, StartAddress = 96 },
                new HardwarePatchOperation { Op = "connect-node-to-subnet", TargetRef = "station", Target = Path("Station", "PROFINET interface"), SubnetName = "PN" },
                new HardwarePatchOperation { Op = "connect-io-connector", TargetRef = "station", Target = Path("Station", "PROFINET interface"), IoSystemName = "IO" }
            };

            var issues = HardwarePatchStructuralValidator.Validate(operations);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "missing-relative-item-path",
                    "generated-child-reference-mismatch",
                    "missing-node-index",
                    "missing-connector-index"
                },
                issues.Select(issue => issue.Code).ToArray());
            Assert.IsTrue(issues.All(issue => issue.OperationIndex >= 1));
        }

        [TestMethod]
        public void StructuralValidator_RejectsForwardDuplicateAndUnobservableGeneratedGuards()
        {
            var operations = new[]
            {
                new HardwarePatchOperation { Op = "plug-item", ResultId = "module", TargetRef = "station", Target = Path("Rail"), ItemName = "Module", PositionNumber = 1 },
                new HardwarePatchOperation { Op = "create-device-with-item", ResultId = "station", DeviceName = "Station", ItemName = "Station" },
                new HardwarePatchOperation { Op = "plug-item", ResultId = "module", TargetRef = "station", Target = Path("Rail"), ItemName = "Other", PositionNumber = 2 },
                new HardwarePatchOperation { Op = "set-io-address-start", TargetRef = "module", Target = Path("Module"), AddressIndex = 0, StartAddress = 10, ExpectedStartAddress = 0 }
            };

            var codes = HardwarePatchStructuralValidator.Validate(operations)
                .Select(issue => issue.Code)
                .ToArray();

            CollectionAssert.Contains(codes, "forward-target-ref");
            CollectionAssert.Contains(codes, "duplicate-result-id");
            CollectionAssert.Contains(codes, "unobservable-generated-address-guard");
        }

        [TestMethod]
        public void StructuralValidator_AggregatesRequiredFieldsBeforeExecution()
        {
            var operations = new[]
            {
                new HardwarePatchOperation { Op = "create-device-with-item", ResultId = "station" },
                new HardwarePatchOperation { Op = "plug-item", TargetRef = "station", Target = Path("Rail_0") },
                new HardwarePatchOperation { Op = "connect-node-to-subnet", TargetRef = "station", Target = Path("Station", "PROFINET interface"), NodeIndex = 0 },
                new HardwarePatchOperation { Op = "connect-io-connector", TargetRef = "station", Target = Path("Station", "PROFINET interface"), ConnectorIndex = 0 },
                new HardwarePatchOperation { Op = "configure-profinet-device", TargetRef = "station", Target = Path("Station", "PROFINET interface") },
                new HardwarePatchOperation { Op = "set-io-address-start", TargetRef = "station", Target = Path("Inputs") },
                new HardwarePatchOperation { Op = "unsupported-op", ResultId = "bad" }
            };

            var issues = HardwarePatchStructuralValidator.Validate(operations);

            Assert.AreEqual(13, issues.Count);
            Assert.AreEqual(10, issues.Count(issue => issue.Code == "missing-required-field"));
            CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "missing-required-choice");
            CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "unsupported-operation");
            CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "invalid-result-id-producer");
            Assert.IsTrue(issues.Any(issue => issue.OperationIndex == 2 && issue.Message.Contains("subnetName")));
            Assert.IsTrue(issues.Any(issue => issue.OperationIndex == 3 && issue.Message.Contains("ioSystemName")));
        }

        [TestMethod]
        public void StructuralValidator_RejectsMissingTargetsAndInvalidPositiveFields()
        {
            var operations = new[]
            {
                new HardwarePatchOperation { Op = "set-attributes", Attributes = new Dictionary<string, object?> { ["Name"] = "Updated" } },
                new HardwarePatchOperation { Op = "get-gsd-parameter-data", Target = Path("Module"), DatasetNumber = 1, Length = 0 }
            };

            var issues = HardwarePatchStructuralValidator.Validate(operations);

            CollectionAssert.AreEquivalent(
                new[] { "missing-operation-target", "missing-operation-target", "invalid-required-field" },
                issues.Select(issue => issue.Code).ToArray());
        }

        [TestMethod]
        public void StructuralValidator_RequiredFieldContractsCoverEveryRegisteredOperation()
        {
            var catalog = new HardwarePatchOperationCatalog(new IHardwarePatchOperationHandler[]
            {
                new HardwareStructurePatchHandler(),
                new HardwareAttributePatchHandler(),
                new HardwareDrivePatchHandler(),
                new HardwareGsdPatchHandler(),
                new HardwareNetworkPatchHandler()
            });

            CollectionAssert.AreEquivalent(
                catalog.OperationNames.ToArray(),
                HardwarePatchOperationContractValidator.OperationNames.ToArray());
        }

        [TestMethod]
        public void TargetResolutionRules_MatchNamesAndTypesCaseInsensitively()
        {
            var candidates = new[] { "Interface", "interface", "Other" };
            var matches = HardwareTargetResolutionRules.FindNameMatches(candidates, value => value, "INTERFACE");
            Assert.AreEqual(2, matches.Count, "The resolver can detect an ambiguous relative path.");
            Assert.AreEqual(0, HardwareTargetResolutionRules.FindNameMatches(candidates, value => value, "Missing").Count);
            Assert.IsTrue(HardwareTargetResolutionRules.TypeMatches("OrderNumber:1", "ordernumber:1"));
            Assert.IsFalse(HardwareTargetResolutionRules.TypeMatches("OrderNumber:1", "OrderNumber:2"));
        }

        [TestMethod]
        public void AuditRules_ParseWidthsOverlapAndGroupBeforeTruncation()
        {
            var longTag = HardwareAuditRules.TryParseTagAddress("T", "Long", "%IL10");
            Assert.IsNotNull(longTag);
            Assert.AreEqual(8, longTag!.Length);
            Assert.AreEqual(17, longTag.EndAddress);
            Assert.IsTrue(HardwareAuditRules.RangesOverlap(10, 17, 17, 20));
            var groups = HardwareAuditRules.BuildUnmatchedRanges(new[]
            {
                longTag,
                HardwareAuditRules.TryParseTagAddress("T", "Bit", "%I20.1")!
            }).ToList();
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(10, groups[0].StartAddress);
            Assert.AreEqual(20, groups[0].EndAddress);
            Assert.AreEqual(2, groups[0].TagCount);
        }

        private sealed class DuplicateHandler : IHardwarePatchOperationHandler
        {
            public IReadOnlyCollection<string> Operations => new[] { "x", "X" };
            public HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context) =>
                throw new NotSupportedException();
        }

        private static HardwareObjectRef Path(params string[] itemPath) =>
            new HardwareObjectRef { ItemPath = itemPath };
    }
}
