using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcCodeCapabilityDiscoveryServiceTests
{
    [DataTestMethod]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(false, false, false)]
    public async System.Threading.Tasks.Task NestedTarget_UsesExactMetadataAndPreservesSafetyBlocks(bool readOnly, bool ambiguous, bool metadataAvailable)
    {
        var plc = new Mock<IPlcService>();
        plc.SetupGet(item => item.IsSoftwareOpen).Returns(true);
        plc.SetupGet(item => item.CurrentPlcName).Returns("PLC_1");
        plc.Setup(item => item.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(System.Array.Empty<BlockInfo>());
        var nodes = new System.Collections.Generic.List<SoftwareTreeNode> { new("Nested", "FC (SCL)", "PLC_1/Blocks/Area/Deep/Nested", System.Array.Empty<SoftwareTreeNode>()) };
        if (ambiguous) nodes.Add(new SoftwareTreeNode("Nested", "FC (SCL)", "PLC_1/Blocks/Other/Nested", System.Array.Empty<SoftwareTreeNode>()));
        plc.Setup(item => item.GetSoftwareTreeAsync(null)).ReturnsAsync(new SoftwareTreeNode("PLC_1", "PlcSoftware", "PLC_1", nodes));
        plc.Setup(item => item.GetBlockInfoAsync("Nested")).ReturnsAsync(metadataAvailable ? new BlockInfoDetail("Nested", "FC", "SCL", "42", readOnly, null, null, null, null, 0, "SCL") : null);
        plc.Setup(item => item.ExportBlockAsSourceAsync("Nested", It.IsAny<string>())).ReturnsAsync((string name, string path) =>
        {
            System.IO.File.WriteAllText(path, "FUNCTION \"Nested\" : Void\nBEGIN\nEND_FUNCTION\n");
            return new PlcSourceOperationResult(true, true, "source exported");
        });
        var context = new EngineeringSessionContext { CurrentPlcName = "PLC_1" };
        var service = new PlcCodeCapabilityDiscoveryService(plc.Object, new Mock<IVciService>().Object, context,
            environmentProvider: name => System.Threading.Tasks.Task.FromResult(new PlcCodeEnvironmentEvidence { TiaVersion = "V21", PlcName = name }), safetyProvider: _ => new PlcCodeSafetyEvidence());
        var result = await service.DiscoverAsync(new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectName = "Nested", GroupPath = "Area/Deep", BlockType = "FC", Operation = "Modify", RequestedLanguage = "SCL", RequestedFormat = "SCL" });
        Assert.AreEqual(metadataAvailable && !ambiguous, result.Target.Exists);
        Assert.AreEqual(ambiguous, result.Target.IdentityAmbiguous);
        Assert.AreEqual(readOnly || ambiguous || !metadataAvailable ? "Blocked" : "DirectScl", result.SelectedRoute);
        if (metadataAvailable && !ambiguous)
        {
            Assert.IsTrue(result.Target.ExactIdentityResolved);
            Assert.AreEqual("Area/Deep", result.Target.GroupPath);
            Assert.AreEqual("42", result.Target.Number);
            Assert.AreEqual(readOnly, result.Target.IsReadOnly);
        }
    }

    [TestMethod]
    public async System.Threading.Tasks.Task BoundTransaction_StoresFingerprintAndRejectsChangedTargetState()
    {
        var plc = new Mock<IPlcService>();
        plc.SetupGet(service => service.IsSoftwareOpen).Returns(true);
        plc.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
        bool exists = false;
        plc.Setup(service => service.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(() => exists
            ? new[] { new BlockInfo("Probe", "FC", "LAD", "10") }
            : System.Array.Empty<BlockInfo>());
        plc.Setup(service => service.GetBlockInfoAsync("Probe")).ReturnsAsync(new BlockInfoDetail("Probe", "FC", "LAD", "10", false, null, null, null, null, 0, "LAD"));
        var vci = new Mock<IVciService>();
        var context = new EngineeringSessionContext { AttachedPortalProcessId = 42, CurrentPlcName = "PLC_1" };
        var discoveryService = new PlcCodeCapabilityDiscoveryService(
            plc.Object,
            vci.Object,
            context,
            environmentProvider: plcName => System.Threading.Tasks.Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", PlcName = plcName }),
            safetyProvider: target => new PlcCodeSafetyEvidence());
        PlcCodeCapabilityDiscovery discovery = await discoveryService.DiscoverAsync(new PlcCodeCapabilityRequest
        {
            PlcName = "PLC_1",
            ObjectName = "Probe",
            ObjectKind = "Block",
            GroupPath = "PLC_1/Blocks",
            Operation = "Create",
            BlockType = "FC",
            RequestedLanguage = "LAD"
        });
        var workflow = new PlcDocumentWorkflowService(plc.Object, context, discoveryService);

        PlcCodeTransaction transaction = await workflow.BeginTransactionAsync("PLC_1", "test", capabilityId: discovery.CapabilityId);
        Assert.AreEqual(string.Empty, discovery.Request.GroupPath);
        Assert.AreEqual(discovery.Fingerprint, transaction.CapabilityBindings.Single().Fingerprint);

        exists = true;
        await Assert.ThrowsExceptionAsync<System.InvalidOperationException>(() => workflow.BeginTransactionAsync("PLC_1", "stale", capabilityId: discovery.CapabilityId));
        Assert.IsTrue(workflow.DiscardTransaction(transaction.TransactionId));
    }

    [TestMethod]
    public void EvaluateRoutes_MappedLadWithSourceControl_SelectsVciFirst()
    {
        PlcCodeRouteDecision selected = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "Main", RequestedLanguage = "LAD", PreferSourceControl = true },
            new PlcCodeTargetEvidence { Exists = true, ProgrammingLanguage = "LAD" },
            new PlcCodeVciEvidence { ExactMappingFound = true, MappingFormat = "SimaticSD", SupportedFormats = new[] { "SimaticSD" } },
            new PlcCodeImportExportEvidence { DocumentRouteObserved = true, SimaticMlRouteObserved = true }).First(route => route.Eligible);

        Assert.AreEqual("VCI", selected.Route);
    }

    [TestMethod]
    public void EvaluateRoutes_UnmappedLad_SelectsObservedDocuments()
    {
        PlcCodeRouteDecision selected = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "Main", RequestedLanguage = "LAD" },
            new PlcCodeTargetEvidence { Exists = true, ProgrammingLanguage = "LAD" },
            new PlcCodeVciEvidence(),
            new PlcCodeImportExportEvidence { DocumentRouteObserved = true, SimaticMlRouteObserved = true }).First(route => route.Eligible);

        Assert.AreEqual("DirectDocuments", selected.Route);
    }

    [TestMethod]
    public void EvaluateRoutes_NewSclTarget_SelectsNativeScl()
    {
        PlcCodeRouteDecision selected = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "Probe", RequestedLanguage = "SCL", Operation = "Create" },
            new PlcCodeTargetEvidence { Exists = false, ProgrammingLanguage = "SCL" },
            new PlcCodeVciEvidence(),
            new PlcCodeImportExportEvidence()).First(route => route.Eligible);

        Assert.AreEqual("DirectScl", selected.Route);
    }

    [TestMethod]
    public void EvaluateRoutes_ExplicitS7DclSclTarget_SelectsDocuments()
    {
        PlcCodeRouteDecision selected = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "CycleProbe", RequestedLanguage = "SCL", RequestedFormat = "S7DCL", Operation = "Create" },
            new PlcCodeTargetEvidence { Exists = false, ProgrammingLanguage = "SCL" },
            new PlcCodeVciEvidence(),
            new PlcCodeImportExportEvidence()).First(route => route.Eligible);

        Assert.AreEqual("DirectDocuments", selected.Route);
    }

    [TestMethod]
    public void EvaluateRoutes_FailsafeTarget_BlocksEveryMutationRoute()
    {
        var routes = PlcCodeCapabilityDiscoveryService.EvaluateRoutes(
            new PlcCodeCapabilityRequest { ObjectName = "Safety", RequestedLanguage = "F_LAD" },
            new PlcCodeTargetEvidence { Exists = true, ProgrammingLanguage = "F_LAD" },
            new PlcCodeSafetyEvidence { IsFailSafeTarget = true, MutationBlocked = true },
            new PlcCodeVciEvidence { ExactMappingFound = true },
            new PlcCodeImportExportEvidence { DocumentRouteObserved = true, SimaticMlRouteObserved = true });

        Assert.IsFalse(routes.Any(route => route.Eligible));
    }

    [TestMethod]
    public void EvaluateRoutes_AmbiguousIdentity_BlocksNameOnlyRoutes()
    {
        var routes = PlcCodeCapabilityDiscoveryService.EvaluateRoutes(
            new PlcCodeCapabilityRequest { ObjectName = "Duplicate", RequestedLanguage = "LAD" },
            new PlcCodeTargetEvidence { Exists = true, IdentityAmbiguous = true, ProgrammingLanguage = "LAD" },
            new PlcCodeSafetyEvidence(),
            new PlcCodeVciEvidence { ExactMappingFound = true },
            new PlcCodeImportExportEvidence { DocumentRouteObserved = true, SimaticMlRouteObserved = true });

        Assert.IsFalse(routes.Any(route => route.Eligible));
    }

    [TestMethod]
    public void EvaluateRoutes_ObservedXmlOnly_SelectsSimaticMlFallback()
    {
        PlcCodeRouteDecision selected = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "Legacy", RequestedLanguage = "LAD", Operation = "Modify" },
            new PlcCodeTargetEvidence { Exists = true, ProgrammingLanguage = "LAD" },
            new PlcCodeVciEvidence(),
            new PlcCodeImportExportEvidence { SimaticMlRouteObserved = true }).First(route => route.Eligible);

        Assert.AreEqual("SimaticML", selected.Route);
    }

    [TestMethod]
    public void EvaluateRoutes_ExplicitSimaticMl_SelectsXmlEvenWhenDocumentsAreObserved()
    {
        var routes = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "XmlPatch", RequestedLanguage = "LAD", RequestedFormat = "SimaticML", Operation = "Modify" },
            new PlcCodeTargetEvidence { Exists = true, ProgrammingLanguage = "LAD" },
            new PlcCodeVciEvidence(),
            new PlcCodeImportExportEvidence { DocumentRouteObserved = true, SimaticMlRouteObserved = true });

        Assert.AreEqual("SimaticML", routes.First(route => route.Eligible).Route);
        Assert.IsFalse(routes.Single(route => route.Route == "DirectDocuments").Eligible);
    }

    [TestMethod]
    public void EvaluateRoutes_UnresolvedOrMissingModifyTarget_BlocksAllRoutes()
    {
        var routes = Routes(
            new PlcCodeCapabilityRequest { ObjectName = "Missing", RequestedLanguage = "LAD", Operation = "Modify" },
            new PlcCodeTargetEvidence { Exists = false, ExactIdentityResolved = false, IdentityIssue = "missing" },
            new PlcCodeVciEvidence(),
            new PlcCodeImportExportEvidence());

        Assert.IsFalse(routes.Any(route => route.Eligible));
    }

    private static System.Collections.Generic.IReadOnlyList<PlcCodeRouteDecision> Routes(PlcCodeCapabilityRequest request, PlcCodeTargetEvidence target, PlcCodeVciEvidence vci, PlcCodeImportExportEvidence io) =>
        PlcCodeCapabilityDiscoveryService.EvaluateRoutes(request, target, new PlcCodeSafetyEvidence(), vci, io);
}
