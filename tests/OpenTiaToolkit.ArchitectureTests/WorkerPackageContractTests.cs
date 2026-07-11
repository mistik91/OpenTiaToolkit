using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class WorkerPackageContractTests
{
    [TestMethod]
    public void EveryVersionedWorkerHasMatchingManifestAndApiBaseline()
    {
        string root = FindRepositoryRoot();
        string[] workerProjects = Directory.GetDirectories(Path.Combine(root, "src"), "OpenTiaToolkit.Worker.V*");
        Assert.IsTrue(workerProjects.Length > 0, "At least one versioned worker package is required.");
        foreach (string workerProject in workerProjects)
        {
            string version = Path.GetFileName(workerProject).Split('.').Last();
            string suffix = version.Substring(1);
            string manifestPath = Path.Combine(root, "compatibility", "worker-v" + suffix.ToLowerInvariant() + ".json");
            string apiPath = Path.Combine(root, "compatibility", "tia-v" + suffix.ToLowerInvariant() + ".api.json");
            Assert.IsTrue(File.Exists(manifestPath), manifestPath);
            Assert.IsTrue(File.Exists(apiPath), apiPath);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            CollectionAssert.Contains(manifest.RootElement.GetProperty("supportedTiaVersions").EnumerateArray().Select(item => item.GetString()).ToArray(), version);
            Assert.AreEqual("1.0", manifest.RootElement.GetProperty("protocolVersion").GetString());
        }
    }

    [TestMethod]
    public void V21ApiBaselineCoversPrioritizedOfficialSurfaces()
    {
        string root = FindRepositoryRoot();
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "compatibility", "tia-v21.api.json")));
        string[] assemblies = baseline.RootElement.GetProperty("assemblies").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!).ToArray();
        foreach (string required in new[] { "Siemens.Engineering.Base.dll", "Siemens.Engineering.Step7.dll", "Siemens.Engineering.WinCCUnified.dll", "Siemens.Engineering.Startdrive.dll", "Siemens.Engineering.Safety.dll", "Siemens.Engineering.SafetyValidation.dll" })
            CollectionAssert.Contains(assemblies, required);
        JsonElement probes = baseline.RootElement.GetProperty("featureProbes");
        foreach (string feature in new[] { "unified-alarms", "unified-runtime-settings", "unified-text-lists", "startdrive-telegrams", "safety-administration", "multiuser", "project-security", "plc-watch-tables", "plc-technology-objects", "plc-alarm-text-lists" })
            Assert.IsTrue(probes.GetProperty(feature).GetBoolean(), feature);
    }

    [TestMethod]
    public void InstallerPublishesVersionSpecificWorkerEnvironmentVariable()
    {
        string root = FindRepositoryRoot();
        string installer = File.ReadAllText(Path.Combine(root, "installer", "OpenTiaToolkit.iss"));
        StringAssert.Contains(installer, "OPEN_TIA_WORKER_V21_EXE");
        StringAssert.Contains(installer, "worker-v21\\OpenTiaToolkit.Worker.V21.exe");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
