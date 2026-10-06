using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class VendorBoundaryTests
{
    [DataTestMethod]
    [DataRow("src/OpenTiaToolkit.Contracts/OpenTiaToolkit.Contracts.csproj")]
    [DataRow("src/OpenTiaToolkit.Application/OpenTiaToolkit.Application.csproj")]
    [DataRow("src/OpenTiaToolkit.CLI/OpenTiaToolkit.CLI.csproj")]
    [DataRow("src/OpenTiaToolkit.MCP/OpenTiaToolkit.MCP.csproj")]
    public void VendorFreeProject_DoesNotReferenceCoreOrSiemens(string relativePath)
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, relativePath));
        string[] references = project.Descendants()
            .Where(element => element.Name.LocalName is "Reference" or "ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();

        Assert.IsFalse(references.Any(value => value.Contains("OpenTiaToolkit.Core", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(references.Any(value => value.Contains("Siemens.Engineering", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void OnlyWorkerAndCoreMayReferenceSiemensAssemblies()
    {
        string root = FindRepositoryRoot();
        string[] offenders = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains("OpenTiaToolkit.Core") && !path.Contains("OpenTiaToolkit.Worker.V21"))
            .Where(path => File.ReadAllText(path).Contains("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        CollectionAssert.AreEqual(Array.Empty<string>(), offenders);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
