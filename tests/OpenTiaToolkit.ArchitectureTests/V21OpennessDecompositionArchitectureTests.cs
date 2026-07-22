using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class V21OpennessDecompositionArchitectureTests
{
    [TestMethod]
    public void CompatibilityFacade_IsSmallNonPartialComposition()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "V21OpennessAdapter.cs");
        string source = File.ReadAllText(path);
        Assert.IsTrue(File.ReadLines(path).Count() < 200);
        StringAssert.Contains(source, "internal sealed class V21OpennessAdapter");
        Assert.IsFalse(source.Contains("partial class", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("BindingFlags", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FocusedComponents_DoNotDependBackOnFacadeOrCrossScreeningThreshold()
    {
        string directory = Path.Combine(FindRepositoryRoot(), "src", "OpenTiaToolkit.Core", "Services", "V21");
        foreach (string path in Directory.GetFiles(directory, "*.cs"))
        {
            string source = File.ReadAllText(path);
            Assert.IsFalse(source.Contains("V21OpennessAdapter", StringComparison.Ordinal), Path.GetFileName(path));
            Assert.IsFalse(source.Contains("partial class", StringComparison.Ordinal), Path.GetFileName(path));
            Assert.IsTrue(File.ReadLines(path).Count() <= 1000, Path.GetFileName(path));
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
