using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class UnifiedHmiDecompositionArchitectureTests
{
    [TestMethod]
    public void CompatibilityFacades_AreNonPartialAndBelowScreeningThreshold()
    {
        string root = FindRepositoryRoot();
        AssertFacade(root, "V21UnifiedHmiAdapter.cs");
        AssertFacade(root, "HmiJsonImportService.cs");
    }

    [TestMethod]
    public void ExtractedUnifiedHmiSources_RemainBelowScreeningThreshold()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "UnifiedHmi");

        foreach (string path in Directory.GetFiles(directory, "*.cs"))
        {
            Assert.IsTrue(File.ReadLines(path).Count() < 900, Path.GetFileName(path) + " must remain comfortably below 1,000 physical lines.");
        }
    }

    [TestMethod]
    public void ImportTransaction_IsOwnedOnlyByCoordinator()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "UnifiedHmi");
        var owners = Directory.GetFiles(directory, "*.cs")
            .Where(path => File.ReadAllText(path).Contains("CommitOnDispose", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        CollectionAssert.AreEqual(new[] { "HmiJsonImportBoundaries.cs" }, owners);
    }

    [TestMethod]
    public void ExtractedComponents_UseCompositionInsteadOfInheritanceChains()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "UnifiedHmi");
        string text = string.Join("\n", Directory.GetFiles(directory, "*.cs").Select(File.ReadAllText));

        Assert.IsFalse(text.Contains("abstract class", StringComparison.Ordinal), "Unified HMI collaborators must be concrete composition units, not inheritance slices.");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"class\s+\w+\s*:\s*V21"), "V21 collaborators must not inherit other V21 collaborators.");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"class\s+\w+\s*:\s*HmiJson"), "Import collaborators must not inherit other import collaborators.");
    }

    [TestMethod]
    public void AdapterFacade_ComposesFocusedOwnersAndContainsOnlyDelegation()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "V21UnifiedHmiAdapter.cs");
        string source = File.ReadAllText(path);

        StringAssert.Contains(source, "V21UnifiedHmiAccess _access");
        StringAssert.Contains(source, "V21HmiCompositeAccess _composites");
        StringAssert.Contains(source, "V21HmiScreenCopyEngine _screenCopy");
        Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("foreach", StringComparison.Ordinal));
        Assert.IsTrue(File.ReadLines(path).Count() < 200);
    }

    [TestMethod]
    public void ImportProcessors_DoNotOwnTransactions()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "UnifiedHmi");
        var processors = Directory.GetFiles(directory, "*Importer.cs")
            .Concat(Directory.GetFiles(directory, "*Executor.cs"))
            .Concat(Directory.GetFiles(directory, "*Coordinator.cs"));

        foreach (string path in processors)
        {
            string source = File.ReadAllText(path);
            Assert.IsFalse(source.Contains("ExclusiveAccess", StringComparison.Ordinal), Path.GetFileName(path));
            Assert.IsFalse(source.Contains("CommitOnDispose", StringComparison.Ordinal), Path.GetFileName(path));
        }
    }

    [TestMethod]
    public void CompositeRegistration_RemainsDuplicateChecked()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "UnifiedHmi");
        string eventSource = File.ReadAllText(Path.Combine(directory, "V21HmiEventSynchronizer.cs"));
        string dynamizationSource = File.ReadAllText(Path.Combine(directory, "V21HmiDynamizationSynchronizer.cs"));

        StringAssert.Contains(eventSource, "duplicate event type");
        StringAssert.Contains(eventSource, "duplicate handler");
        StringAssert.Contains(dynamizationSource, "duplicate property");
    }

    [TestMethod]
    public void PurePatchComponents_DoNotReferenceSiemensOrLiveAdapter()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "HmiScreenJsonPatch");
        string text = string.Join("\n", Directory.GetFiles(directory, "*.cs").Select(File.ReadAllText));

        Assert.IsFalse(text.Contains("Siemens.Engineering", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("V21UnifiedHmi", StringComparison.Ordinal));
    }

    private static void AssertFacade(string root, string fileName)
    {
        string path = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", fileName);
        string source = File.ReadAllText(path);
        Assert.IsTrue(File.ReadLines(path).Count() < 1000);
        Assert.IsFalse(source.Contains("partial class", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
