using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public sealed class PlcTagWorkflowArchitectureTests
{
    [TestMethod]
    public void CompatibilityFacade_IsSmallNonPartialAndDelegationOnly()
    {
        string root = FindRepositoryRoot();
        string services = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services");
        string path = Path.Combine(services, "PlcTagService.cs");
        string facade = File.ReadAllText(path);

        StringAssert.Contains(facade, "internal sealed class PlcTagService");
        Assert.IsFalse(facade.Contains("partial class PlcTagService", StringComparison.Ordinal));
        Assert.IsTrue(File.ReadLines(path).Count() < 200);
        Assert.AreEqual(1, Directory.GetFiles(services, "PlcTagService*.cs").Length);
    }

    [TestMethod]
    public void Workflows_UseRepositoryBoundaryAndRemainBelowScreeningThreshold()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "PlcTags");
        foreach (string path in Directory.GetFiles(directory, "*.cs"))
        {
            Assert.IsTrue(
                File.ReadLines(path).Count() < 1000,
                Path.GetFileName(path) + " must remain below the god-class screening threshold.");

            if (!path.EndsWith("V21PlcTagRepository.cs", StringComparison.Ordinal))
            {
                Assert.IsFalse(
                    File.ReadAllText(path).Contains("V21OpennessAdapter", StringComparison.Ordinal),
                    Path.GetFileName(path) + " must use the PLC-tag repository boundary.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
