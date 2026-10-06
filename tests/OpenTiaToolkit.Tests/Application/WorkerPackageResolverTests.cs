using System;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Compatibility;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.Tests.Application;

[TestClass]
public class WorkerPackageResolverTests
{
    [TestMethod]
    public void Resolve_UsesVersionedManifestAndEnvironmentVariable()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "worker-resolver", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string executable = Path.Combine(root, "OpenTiaToolkit.Worker.V21.exe");
        File.WriteAllBytes(executable, new byte[] { 1 });
        File.WriteAllText(Path.Combine(root, "worker-manifest.json"), JsonSerializer.Serialize(new WorkerPackageManifest
        {
            WorkerId = "test-v21",
            Executable = Path.GetFileName(executable),
            ProtocolVersion = ProtocolConstants.Version,
            SupportedTiaVersions = new[] { "V21" }
        }, ContractJson.CreateOptions()));
        string? before = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_V21_EXE");
        try
        {
            Environment.SetEnvironmentVariable("OPEN_TIA_WORKER_V21_EXE", executable);
            Assert.AreEqual(Path.GetFullPath(executable), WorkerPackageResolver.Resolve(executable, "21"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPEN_TIA_WORKER_V21_EXE", before);
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Resolve_RejectsPackageForDifferentTiaVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "worker-resolver", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string executable = Path.Combine(root, "worker.exe");
        File.WriteAllBytes(executable, new byte[] { 1 });
        File.WriteAllText(Path.Combine(root, "worker-manifest.json"), JsonSerializer.Serialize(new WorkerPackageManifest
        {
            WorkerId = "test-v20",
            Executable = Path.GetFileName(executable),
            ProtocolVersion = ProtocolConstants.Version,
            SupportedTiaVersions = new[] { "V20" }
        }, ContractJson.CreateOptions()));
        string? versioned = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_V21_EXE");
        string? legacy = Environment.GetEnvironmentVariable("OPEN_TIA_WORKER_EXE");
        try
        {
            Environment.SetEnvironmentVariable("OPEN_TIA_WORKER_V21_EXE", executable);
            Environment.SetEnvironmentVariable("OPEN_TIA_WORKER_EXE", null);
            StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => WorkerPackageResolver.Resolve(executable, "V21")).Message, "does not support");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPEN_TIA_WORKER_V21_EXE", versioned);
            Environment.SetEnvironmentVariable("OPEN_TIA_WORKER_EXE", legacy);
            Directory.Delete(root, true);
        }
    }
}
