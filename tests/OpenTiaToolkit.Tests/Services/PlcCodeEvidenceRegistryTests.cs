using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcCodeEvidenceRegistryTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public void Registry_PromotesOnlyVerifiedExactFingerprintAndSignature()
    {
        var registry = new PlcCodeEvidenceRegistry(_root);
        var verified = Record("Verified", "environment-a", "shape-a");
        var failed = Record("Failed", "environment-a", "shape-a");
        var quarantined = Record("Quarantined", "environment-a", "shape-a");
        var unclean = Record("Verified", "environment-a", "shape-a");
        unclean.CleanupProven = false;
        registry.Save(verified);
        registry.Save(failed);
        registry.Save(quarantined);
        registry.Save(unclean);

        Assert.AreEqual(1, registry.FindVerified("environment-a", "shape-a").Count);
        Assert.AreEqual(0, registry.FindVerified("environment-b", "shape-a").Count);
        Assert.AreEqual(0, registry.FindVerified("environment-a", "shape-b").Count);
        Assert.AreEqual(4, registry.List().Count);
        Assert.AreEqual(verified.EvidenceId, registry.Get(verified.EvidenceId).EvidenceId);
    }

    [TestMethod]
    public void Registry_BehaviorEvidenceRequiresVectorAndSimulatorFingerprint()
    {
        var registry = new PlcCodeEvidenceRegistry(_root);
        var behavior = Record("Verified", "environment-a", "shape-a");
        behavior.EvidenceKind = "Behavior";
        behavior.Behavior = new PlcCodeBehaviorEvidence { VectorHash = "vector-a", SimulatorFingerprint = "sim-a" };
        registry.Save(behavior);

        Assert.AreEqual(0, registry.FindVerified("environment-a", "shape-a").Count);
        Assert.AreEqual(1, registry.FindVerifiedBehavior("environment-a", "shape-a", "vector-a", "sim-a").Count);
        Assert.AreEqual(0, registry.FindVerifiedBehavior("environment-a", "shape-a", "vector-b", "sim-a").Count);
        Assert.AreEqual(0, registry.FindVerifiedBehavior("environment-a", "shape-a", "vector-a", "sim-b").Count);
    }

    [TestMethod]
    public void CompatibilityFingerprint_ChangesForEveryPromotionBoundary()
    {
        var validation = new PlcCodeArtifactValidationResult { Format = "S7DCL-LAD" };
        string baseline = PlcCodeEvidenceRegistry.BuildCompatibility(new[] { Capability() }, validation).Fingerprint;

        PlcCodeCapabilityDiscovery tia = Capability(); tia.Environment.TiaBuild = "21.0.3";
        PlcCodeCapabilityDiscovery plc = Capability(); plc.Environment.PlcTypeIdentifier = "CPU 1515";
        PlcCodeCapabilityDiscovery route = Capability(); route.SelectedRoute = "SimaticML";
        PlcCodeCapabilityDiscovery safety = Capability(); safety.Safety.IsFailSafeTarget = true;
        PlcCodeCapabilityDiscovery shape = Capability(); shape.Request.BlockType = "FB";

        Assert.AreNotEqual(baseline, PlcCodeEvidenceRegistry.BuildCompatibility(new[] { tia }, validation).Fingerprint, "TIA build");
        Assert.AreNotEqual(baseline, PlcCodeEvidenceRegistry.BuildCompatibility(new[] { plc }, validation).Fingerprint, "PLC identity");
        Assert.AreNotEqual(baseline, PlcCodeEvidenceRegistry.BuildCompatibility(new[] { route }, validation).Fingerprint, "route");
        Assert.AreNotEqual(baseline, PlcCodeEvidenceRegistry.BuildCompatibility(new[] { safety }, validation).Fingerprint, "safety context");
        Assert.AreNotEqual(baseline, PlcCodeEvidenceRegistry.BuildCompatibility(new[] { shape }, validation).Fingerprint, "object shape");

        var dependent = new PlcCodeArtifactValidationResult { Format = "S7DCL-LAD", Dependencies = new[] { "ExternalCall" } };
        PlcCodeCapabilityDiscovery otherProject = Capability(); otherProject.Environment.ProjectPath = "C:\\Projects\\Other.ap21";
        string projectA = PlcCodeEvidenceRegistry.BuildCompatibility(new[] { Capability() }, dependent).Fingerprint;
        string projectB = PlcCodeEvidenceRegistry.BuildCompatibility(new[] { otherProject }, dependent).Fingerprint;
        Assert.AreNotEqual(projectA, projectB, "project-bound dependency");
    }

    [TestMethod]
    public void CompatibilityFingerprint_RequiresExactTiaAndPlcEvidence()
    {
        PlcCodeCapabilityDiscovery missingTia = Capability(); missingTia.Environment.TiaBuild = string.Empty;
        PlcCodeCapabilityDiscovery missingPlc = Capability(); missingPlc.Environment.PlcFamily = string.Empty; missingPlc.Environment.PlcTypeIdentifier = string.Empty;

        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => PlcCodeEvidenceRegistry.BuildCompatibility(new[] { missingTia }, new PlcCodeArtifactValidationResult { Format = "SCL" })).Message, "TIA version");
        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => PlcCodeEvidenceRegistry.BuildCompatibility(new[] { missingPlc }, new PlcCodeArtifactValidationResult { Format = "SCL" })).Message, "PLC family");
    }

    private static PlcCodeCapabilityDiscovery Capability() => new PlcCodeCapabilityDiscovery
    {
        SelectedRoute = "DirectDocuments",
        Request = new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectKind = "Block", ObjectName = "Shape", BlockType = "FC", RequestedLanguage = "LAD" },
        Environment = new PlcCodeEnvironmentEvidence
        {
            TiaVersion = "V21 Update 2",
            TiaBuild = "21.0.2",
            ProjectName = "Project",
            ProjectPath = "C:\\Projects\\Project.ap21",
            PlcName = "PLC_1",
            PlcFamily = "S7-1500",
            PlcTypeIdentifier = "CPU 1511"
        },
        Safety = new PlcCodeSafetyEvidence()
    };

    private static PlcCodeEvidenceRecord Record(string status, string environment, string shape) => new PlcCodeEvidenceRecord
    {
        EvidenceId = Guid.NewGuid().ToString("N"),
        Status = status,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CleanupProven = status == "Verified",
        Compatibility = new PlcCodeEvidenceCompatibility { Fingerprint = environment },
        QualificationSignature = shape
    };
}
