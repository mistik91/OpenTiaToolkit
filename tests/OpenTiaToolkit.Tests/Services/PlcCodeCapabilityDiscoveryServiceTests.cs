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
            Operation = "Create",
            BlockType = "FC",
            RequestedLanguage = "LAD"
        });
        var workflow = new PlcDocumentWorkflowService(plc.Object, context, discoveryService);

        PlcCodeTransaction transaction = await workflow.BeginTransactionAsync("PLC_1", "test", capabilityId: discovery.CapabilityId);
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
