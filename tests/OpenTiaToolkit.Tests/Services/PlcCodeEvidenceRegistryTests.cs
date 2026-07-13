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
        registry.Save(verified);
        registry.Save(failed);
        registry.Save(quarantined);

        Assert.AreEqual(1, registry.FindVerified("environment-a", "shape-a").Count);
        Assert.AreEqual(0, registry.FindVerified("environment-b", "shape-a").Count);
        Assert.AreEqual(0, registry.FindVerified("environment-a", "shape-b").Count);
        Assert.AreEqual(3, registry.List().Count);
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

    private static PlcCodeEvidenceRecord Record(string status, string environment, string shape) => new PlcCodeEvidenceRecord
    {
        EvidenceId = Guid.NewGuid().ToString("N"),
        Status = status,
        StartedAtUtc = DateTimeOffset.UtcNow,
        Compatibility = new PlcCodeEvidenceCompatibility { Fingerprint = environment },
        QualificationSignature = shape
    };
}
