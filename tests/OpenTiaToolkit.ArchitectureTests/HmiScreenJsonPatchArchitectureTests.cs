using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class HmiScreenJsonPatchArchitectureTests
{
    [TestMethod]
    public void Applier_RemainsSmallStaticNonPartialFacade()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo file = index.Files["src/OpenTiaToolkit.Core/Services/HmiScreenJsonPatchApplier.cs"];
        ClassDeclarationSyntax facade = FindClass(file, "HmiScreenJsonPatchApplier");

        Assert.IsTrue(file.PhysicalLines < 100);
        Assert.IsTrue(facade.Modifiers.Any(SyntaxKind.StaticKeyword));
        Assert.IsFalse(facade.Modifiers.Any(SyntaxKind.PartialKeyword));
        Assert.AreEqual(0, facade.DescendantNodes().Count(IsLoop));
        Assert.AreEqual(0, facade.DescendantNodes().OfType<ForEachStatementSyntax>().Count());
    }

    [TestMethod]
    public void PurePatchFiles_RemainVendorFreeAndFocused()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo[] patchFiles = PatchFiles(index).ToArray();

        foreach (SourceFileInfo file in patchFiles)
        {
            Assert.IsTrue(file.PhysicalLines < 250, file.Path + " must remain a focused patch component.");
            string[] namespaces = file.Root.Usings.Select(item => item.Name?.ToString() ?? string.Empty).ToArray();
            Assert.IsFalse(namespaces.Any(name => name.StartsWith("Siemens", StringComparison.Ordinal)), file.Path);
            Assert.IsFalse(file.Root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Any(identifier => identifier.Identifier.ValueText.StartsWith("V21", StringComparison.Ordinal)), file.Path);
        }
    }

    [TestMethod]
    public void CatalogDefaultRegistryAndAgentSupport_StaySynchronized()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo catalogFile = index.Files["src/OpenTiaToolkit.Core/Services/HmiScreenJsonPatch/HmiScreenJsonPatchOperationCatalog.cs"];
        FieldDeclarationSyntax operationsField = FindClass(catalogFile, "HmiScreenJsonPatchOperationCatalog")
            .Members.OfType<FieldDeclarationSyntax>()
            .Single(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "Operations"));
        string[] canonical = operationsField.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText)
            .ToArray();

        SourceFileInfo engineFile = index.Files["src/OpenTiaToolkit.Core/Services/HmiScreenJsonPatch/HmiScreenJsonPatchEngine.cs"];
        MethodDeclarationSyntax createDefault = FindClass(engineFile, "HmiScreenJsonPatchEngine").Members
            .OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "CreateDefault");
        string[] defaultHandlers = createDefault.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(name => name.EndsWith("Handler", StringComparison.Ordinal) || name.EndsWith("HandlerOperations", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

        ClassDeclarationSyntax[] handlerTypes = PatchFiles(index)
            .SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => type.BaseList?.Types.Any(item => item.Type.ToString() == "IHmiScreenJsonPatchOperationHandler") == true)
            .ToArray();
        string[] declaredOperations = handlerTypes
            .SelectMany(type => type.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                .Where(property => property.Identifier.ValueText == "OperationNames")
                .SelectMany(property => property.DescendantNodes().OfType<LiteralExpressionSyntax>())
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => literal.Token.ValueText))
            .ToArray();

        CollectionAssert.AreEqual(
            canonical.OrderBy(operation => operation, StringComparer.Ordinal).ToArray(),
            declaredOperations.OrderBy(operation => operation, StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(handlerTypes.Select(type => type.Identifier.ValueText).OrderBy(name => name, StringComparer.Ordinal).ToArray(), defaultHandlers);
        Assert.AreEqual(canonical.Length, canonical.Distinct(StringComparer.Ordinal).Count());

        SourceFileInfo agentSupport = index.Files["src/OpenTiaToolkit.Core/Services/HmiScreenJsonAgentSupport.cs"];
        PropertyDeclarationSyntax supported = FindClass(agentSupport, "HmiScreenJsonAgentSupport").Members
            .OfType<PropertyDeclarationSyntax>().Single(property => property.Identifier.ValueText == "SupportedPatchOperations");
        StringAssert.Contains(supported.ExpressionBody?.Expression.ToString(), "HmiScreenJsonPatchOperationCatalog.SupportedOperations");
        Assert.IsTrue(createDefault.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression.ToString().EndsWith("ValidateCatalogCoverage", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Dispatcher_NormalizesAndDuplicateChecksAllRegistrations()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo engineFile = index.Files["src/OpenTiaToolkit.Core/Services/HmiScreenJsonPatch/HmiScreenJsonPatchEngine.cs"];
        ClassDeclarationSyntax dispatcher = FindClass(engineFile, "HmiScreenJsonPatchDispatcher");
        ConstructorDeclarationSyntax constructor = dispatcher.Members.OfType<ConstructorDeclarationSyntax>().Single();

        Assert.IsTrue(constructor.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression.ToString().EndsWith("Normalize", StringComparison.Ordinal)));
        Assert.IsTrue(constructor.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression.ToString().EndsWith("ContainsKey", StringComparison.Ordinal)));
        Assert.IsTrue(constructor.DescendantNodes().OfType<ThrowStatementSyntax>()
            .Any(statement => statement.Expression?.ToString().Contains("Duplicate patch operation handler registration", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void Handlers_CannotBypassCentralDispatcherOwnership()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        string[] handlerNames = PatchFiles(index)
            .SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => type.BaseList?.Types.Any(item => item.Type.ToString() == "IHmiScreenJsonPatchOperationHandler") == true)
            .Select(type => type.Identifier.ValueText).ToArray();

        var creationSites = index.Files.Values.SelectMany(file => file.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(creation => handlerNames.Contains(creation.Type.ToString()))
            .Select(creation => (file.Path, Method: creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText)))
            .ToArray();
        Assert.AreEqual(handlerNames.Length, creationSites.Length);
        Assert.IsTrue(creationSites.All(site =>
            site.Path.EndsWith("HmiScreenJsonPatchEngine.cs", StringComparison.Ordinal) && site.Method == "CreateDefault"));

        foreach (SourceFileInfo file in PatchFiles(index).Where(file => file.Path.Contains("Handler", StringComparison.Ordinal)))
        {
            Assert.IsFalse(file.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Any(creation => creation.Type.ToString() is "HmiScreenJsonPatchDispatcher" or "HmiScreenJsonPatchEngine"), file.Path);
        }
    }

    private static IEnumerable<SourceFileInfo> PatchFiles(ProductionSourceIndex index) => index.Files.Values
        .Where(file => file.Path.StartsWith("src/OpenTiaToolkit.Core/Services/HmiScreenJsonPatch/", StringComparison.Ordinal));

    private static ClassDeclarationSyntax FindClass(SourceFileInfo file, string name) =>
        file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);

    private static bool IsLoop(SyntaxNode node) => node is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
}
