using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class PlcDocumentWorkflowArchitectureTests
{
    [TestMethod]
    public void CompatibilityFacade_IsNonPartialAndDelegationOnly()
    {
        string root = FindRepositoryRoot();
        string facade = File.ReadAllText(Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "PlcDocumentWorkflowService.cs"));
        StringAssert.Contains(facade, "public sealed class PlcDocumentWorkflowService");
        Assert.IsFalse(facade.Contains("partial class PlcDocumentWorkflowService", StringComparison.Ordinal));
        Assert.IsFalse(facade.Contains("V21OpennessAdapter", StringComparison.Ordinal));
        Assert.IsFalse(facade.Contains("Dictionary<", StringComparison.Ordinal));
        Assert.IsFalse(facade.Contains("JsonSerializerOptions", StringComparison.Ordinal));
        Assert.IsTrue(Directory.GetFiles(Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services"), "PlcDocumentWorkflowService*.cs").Length == 1);
    }

    [TestMethod]
    public void FormatService_NormalCompositionUsesDocumentValidator()
    {
        string root = FindRepositoryRoot();
        string registrations = File.ReadAllText(Path.Combine(root, "src", "OpenTiaToolkit.Core", "ServiceCollectionExtensions.cs"));
        StringAssert.Contains(registrations, "new PlcCodeFormatService(serviceProvider.GetRequiredService<PlcDocumentValidator>())");
        string collaborators = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "PlcDocumentWorkflow");
        foreach (string path in Directory.GetFiles(collaborators, "*.cs"))
            Assert.IsFalse(File.ReadAllText(path).Contains("PlcDocumentWorkflowService ", StringComparison.Ordinal), Path.GetFileName(path) + " must not depend back on the facade.");
    }

    [TestMethod]
    public void TransactionCoordinator_IsRoutingOnlyAndDistinctOwnersExist()
    {
        string root = FindRepositoryRoot();
        string collaborators = Path.Combine(root, "src", "OpenTiaToolkit.Core", "Services", "PlcDocumentWorkflow");
        foreach (string owner in new[] { "PlcCodeTransactionAuthoringService.cs", "PlcCodeTransactionPlanner.cs", "PlcCodeTransactionExecutor.cs" })
            Assert.IsTrue(File.Exists(Path.Combine(collaborators, owner)), owner + " must remain a distinct transaction owner.");

        string coordinatorPath = Path.Combine(collaborators, "PlcCodeTransactionCoordinator.cs");
        Assert.IsTrue(File.ReadLines(coordinatorPath).Count() < 100, "The transaction coordinator must remain a small routing component.");
        string coordinator = File.ReadAllText(coordinatorPath);
        Assert.IsFalse(coordinator.Contains("V21OpennessAdapter", StringComparison.Ordinal));
        Assert.IsFalse(coordinator.Contains("JsonSerializer", StringComparison.Ordinal));
        Assert.IsFalse(coordinator.Contains("CompileSoftwareAsync", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
