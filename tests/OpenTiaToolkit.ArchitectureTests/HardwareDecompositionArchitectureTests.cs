using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class HardwareDecompositionArchitectureTests
{
    private static readonly string[] HandlerTypes =
    {
        "HardwareAttributePatchHandler", "HardwareDrivePatchHandler", "HardwareGsdPatchHandler",
        "HardwareNetworkPatchHandler", "HardwareStructurePatchHandler"
    };

    [TestMethod]
    public void HardwareFacade_RemainsSmallSealedNonPartialAndDelegationOnly()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo file = index.Files["src/OpenTiaToolkit.Core/Services/HardwareService.cs"];
        ClassDeclarationSyntax facade = FindClass(file, "HardwareService");

        Assert.IsTrue(file.PhysicalLines < 200, "The compatibility facade must remain below 200 physical lines.");
        Assert.IsTrue(facade.Modifiers.Any(SyntaxKind.SealedKeyword));
        Assert.IsFalse(facade.Modifiers.Any(SyntaxKind.PartialKeyword));
        Assert.AreEqual(0, facade.DescendantNodes().Count(IsLoop));
        Assert.AreEqual(0, facade.DescendantNodes().OfType<ForEachStatementSyntax>().Count());
        AssertNoIdentifiers(facade, "ExclusiveAccess", "CommitOnDispose", "BindingFlags", "MethodInfo", "PropertyInfo");
        Assert.IsTrue(facade.Members.OfType<MethodDeclarationSyntax>().All(method => method.ExpressionBody != null),
            "Facade methods must remain direct expression-bodied delegation.");
    }

    [TestMethod]
    public void ExtractedHardwareCollaborators_StayFocusedAndUseComposition()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo[] extracted = index.Files.Values
            .Where(file => file.Path.StartsWith("src/OpenTiaToolkit.Core/Services/Hardware/", StringComparison.Ordinal))
            .ToArray();

        foreach (SourceFileInfo file in extracted)
        {
            Assert.IsTrue(file.PhysicalLines < 500, file.Path + " must stay comfortably below the global screen.");
            foreach (ClassDeclarationSyntax type in file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                string[] bases = type.BaseList?.Types.Select(item => item.Type.ToString()).ToArray() ?? Array.Empty<string>();
                Assert.IsTrue(bases.All(name => name.StartsWith("I", StringComparison.Ordinal)),
                    type.Identifier.Text + " must use composition rather than inherit another hardware collaborator.");
            }
        }
    }

    [TestMethod]
    public void HardwarePatchWorkflow_AloneOwnsTransactions()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        var transactionOwners = index.Files.Values
            .Where(file => file.Path.Contains("/Services/Hardware", StringComparison.Ordinal))
            .SelectMany(file => file.Root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => identifier.Identifier.ValueText is "ExclusiveAccess" or "CommitOnDispose")
                .Select(identifier => (file.Path, Owner: identifier.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText)))
            .ToArray();

        Assert.IsTrue(transactionOwners.Length >= 2);
        Assert.IsTrue(transactionOwners.All(owner =>
            owner.Path.EndsWith("HardwarePatchInfrastructure.cs", StringComparison.Ordinal) && owner.Owner == "HardwarePatchWorkflow"),
            string.Join(Environment.NewLine, transactionOwners.Select(owner => owner.ToString())));
    }

    [TestMethod]
    public void PatchRegistration_RemainsCentralizedDuplicateCheckedAndFiveWay()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo infrastructure = index.Files["src/OpenTiaToolkit.Core/Services/Hardware/Patching/HardwarePatchInfrastructure.cs"];
        ClassDeclarationSyntax catalog = FindClass(infrastructure, "HardwarePatchOperationCatalog");
        ConstructorDeclarationSyntax constructor = catalog.Members.OfType<ConstructorDeclarationSyntax>().Single();

        AssertHasInvocation(constructor, "NormalizeOperation");
        AssertHasInvocation(constructor, "ContainsKey");
        Assert.IsTrue(constructor.DescendantNodes().OfType<ThrowStatementSyntax>()
            .Any(statement => statement.Expression?.ToString().Contains("Duplicate hardware patch operation registration", StringComparison.Ordinal) == true));

        ClassDeclarationSyntax workflow = FindClass(infrastructure, "HardwarePatchWorkflow");
        string[] registered = workflow.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(HandlerTypes.Contains)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(HandlerTypes.OrderBy(name => name, StringComparer.Ordinal).ToArray(), registered);

        string[] implementations = index.Files.Values
            .Where(file => file.Path.StartsWith("src/OpenTiaToolkit.Core/Services/Hardware/Patching/", StringComparison.Ordinal))
            .SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => type.BaseList?.Types.Any(item => item.Type.ToString() == "IHardwarePatchOperationHandler") == true)
            .Select(type => type.Identifier.ValueText)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(HandlerTypes.OrderBy(name => name, StringComparer.Ordinal).ToArray(), implementations);

        ClassDeclarationSyntax facade = FindClass(index.Files["src/OpenTiaToolkit.Core/Services/HardwareService.cs"], "HardwareService");
        Assert.IsFalse(facade.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Any(creation => HandlerTypes.Contains(creation.Type.ToString())));
    }

    [TestMethod]
    public void V21HardwareAdapter_RemainsExplicitReviewedBoundary()
    {
        string root = RepositoryPaths.FindRoot();
        ProductionSourceIndex index = ProductionSourceIndex.Create(root);
        SourceFileInfo adapter = index.Files["src/OpenTiaToolkit.Core/Services/V21HardwareAdapter.cs"];
        Assert.IsTrue(FindClass(adapter, "V21HardwareAdapter").Modifiers.Any(SyntaxKind.SealedKeyword));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "tests", "OpenTiaToolkit.ArchitectureTests", "god-class-debt-baseline.json")));
        int ceiling = document.RootElement.GetProperty("reviewedFileGrowthCeilings")
            .GetProperty("src/OpenTiaToolkit.Core/Services/V21HardwareAdapter.cs").GetInt32();
        Assert.AreEqual(adapter.PhysicalLines, ceiling, "The explicit adapter review ceiling must match, not inflate, its measured size.");
    }

    private static ClassDeclarationSyntax FindClass(SourceFileInfo file, string name) =>
        file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);

    private static void AssertNoIdentifiers(SyntaxNode node, params string[] forbidden)
    {
        var found = node.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(identifier => identifier.Identifier.ValueText)
            .Where(forbidden.Contains).Distinct().ToArray();
        Assert.AreEqual(0, found.Length, "Forbidden facade identifiers: " + string.Join(", ", found));
    }

    private static void AssertHasInvocation(SyntaxNode node, string methodName) =>
        Assert.IsTrue(node.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == methodName));

    private static bool IsLoop(SyntaxNode node) => node is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
}
