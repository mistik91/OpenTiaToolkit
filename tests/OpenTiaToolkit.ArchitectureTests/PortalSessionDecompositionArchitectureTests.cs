using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class PortalSessionDecompositionArchitectureTests
{
    [TestMethod]
    public void SessionFacade_IsSmallNonPartialAndContainsNoSiemensAlgorithms()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "PortalSessionService.cs");
        string source = File.ReadAllText(path);
        Assert.IsTrue(File.ReadLines(path).Count() < 100);
        StringAssert.Contains(source, "internal sealed class PortalSessionService");
        Assert.IsFalse(source.Contains("partial class", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("TiaPortal.GetProcesses", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("new TiaPortal", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("SecureString", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PortalCollaborators_StayBelowScreeningThresholdAndDoNotDependOnFacadesOrCommands()
    {
        string directory = Path.Combine(FindRepositoryRoot(), "src", "OpenTiaToolkit.Core", "Services", "Portal");
        foreach (string path in Directory.GetFiles(directory, "*.cs"))
        {
            string source = File.ReadAllText(path);
            Assert.IsTrue(File.ReadLines(path).Count() <= 1000, Path.GetFileName(path));
            Assert.IsFalse(source.Contains("PortalSessionService", StringComparison.Ordinal), Path.GetFileName(path));
            Assert.IsFalse(source.Contains("PortalService ", StringComparison.Ordinal), Path.GetFileName(path));
            Assert.IsFalse(source.Contains("Command", StringComparison.Ordinal), Path.GetFileName(path));
        }
    }

    [TestMethod]
    public void StateAndSecurityPolicies_HaveSingleFocusedOwners()
    {
        string root = FindRepositoryRoot();
        string portalDirectory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "Portal");
        foreach (string workflow in new[] { "PortalInstanceWorkflow.cs", "PortalProjectOpenWorkflow.cs", "PortalProjectLifecycleWorkflow.cs" })
        {
            string source = File.ReadAllText(Path.Combine(portalDirectory, workflow));
            Assert.IsFalse(source.Contains("SetPassword", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("TiaPortal.GetProcesses", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("new TiaPortal(", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("_sessionContext.Clear", StringComparison.Ordinal), workflow + " must route selection clearing through PortalSessionStateCoordinator.");
        }
        Assert.IsTrue(File.Exists(Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "Portal", "PortalSessionStateCoordinator.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "Portal", "PortalSessionPolicy.cs")));
        string runtime = File.ReadAllText(Path.Combine(portalDirectory, "PortalRuntimeAdapter.cs"));
        StringAssert.Contains(runtime, "TiaPortal.GetProcesses()");
        StringAssert.Contains(runtime, "new TiaPortal(mode)");
        foreach (string workflow in new[] { "PortalInstanceWorkflow.cs", "PortalProjectOpenWorkflow.cs", "PortalProjectLifecycleWorkflow.cs", "PortalSessionStateCoordinator.cs" })
        {
            string source = File.ReadAllText(Path.Combine(portalDirectory, workflow));
            foreach (string rawOperation in new[] { ".Projects", ".LocalSessions", "project.Archive(", "project.Save(", "project.SaveAs(", "project.Close(", "session.Save(", "session.Close(", "portal.Dispose(", "targetPortal.Dispose(", "_portal.Dispose(" })
                Assert.IsFalse(source.Contains(rawOperation, StringComparison.Ordinal), workflow + " must route raw Siemens operation " + rawOperation + " through PortalRuntimeAdapter.");
        }

        string policy = File.ReadAllText(Path.Combine(portalDirectory, "PortalSessionPolicy.cs"));
        StringAssert.Contains(policy, "credentials.SetPassword(authentication.Password)");
        Assert.IsFalse(policy.Contains("SecureStringTo", StringComparison.Ordinal));
        Assert.IsFalse(policy.Contains("Password.ToString", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TestAssemblyResolution_IsDerivedFromTheConfiguredPublicApiPath()
    {
        string root = FindRepositoryRoot();
        string config = File.ReadAllText(Path.Combine(root, "tests", "OpenTiaToolkit.Tests", "App.config"));
        string project = File.ReadAllText(Path.Combine(root, "tests", "OpenTiaToolkit.Tests", "OpenTiaToolkit.Tests.csproj"));

        Assert.IsFalse(config.Contains("Program Files", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(project, "SetTestAssemblySiemensCodeBasePaths");
        StringAssert.Contains(project, "$(TiaPortalPublicApiPath)");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
