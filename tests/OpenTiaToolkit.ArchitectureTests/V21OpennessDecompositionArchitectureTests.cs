using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    [TestMethod]
    public void ProductionComposition_RegistersFocusedCapabilitiesWithoutBroadFacade()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo composition = index.Files["src/OpenTiaToolkit.Core/ServiceCollectionExtensions.cs"];
        string[] registrations = composition.Root.DescendantNodes().OfType<GenericNameSyntax>()
            .Where(name => name.Identifier.ValueText == "AddSingleton")
            .SelectMany(name => name.TypeArgumentList.Arguments.Select(argument => argument.ToString()))
            .ToArray();

        foreach (string capability in new[]
        {
            "V21TypeServiceResolver", "V21EngineeringObjectAccess", "V21SoftwareDiscovery",
            "V21PlcTagStructureAccess", "V21CompilationAccess", "V21OnlineVerificationAccess",
            "V21DocumentAccess", "V21HardwareAdapter", "V21PlcTagRepository"
        })
        {
            CollectionAssert.Contains(registrations, capability);
        }

        Assert.IsFalse(composition.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Any(creation => creation.Type.ToString() == "V21OpennessAdapter"));
        CollectionAssert.DoesNotContain(registrations, "V21OpennessAdapter");
    }

    [TestMethod]
    public void ProductionServices_DoNotStoreOrConstructBroadFacade()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        SourceFileInfo[] productionServices = index.Files.Values
            .Where(file => file.Path.StartsWith("src/OpenTiaToolkit.Core/Services/", StringComparison.Ordinal))
            .Where(file => !file.Path.EndsWith("V21OpennessAdapter.cs", StringComparison.Ordinal))
            .ToArray();

        string[] fields = productionServices
            .SelectMany(file => file.Root.DescendantNodes().OfType<FieldDeclarationSyntax>()
                .Where(field => field.Declaration.Type.ToString().Contains("V21OpennessAdapter", StringComparison.Ordinal))
                .Select(_ => file.Path))
            .ToArray();
        string[] creations = productionServices
            .SelectMany(file => file.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Where(creation => creation.Type.ToString() == "V21OpennessAdapter")
                .Select(_ => file.Path))
            .ToArray();

        Assert.AreEqual(0, fields.Length, "Broad facade fields: " + string.Join(", ", fields));
        Assert.AreEqual(0, creations.Length, "Broad facade construction: " + string.Join(", ", creations));
    }

    [TestMethod]
    public void FeatureConstructors_DeclareOnlyTheirFocusedV21Dependencies()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        AssertV21Constructor(index, "PortalInspectionService", "V21TypeServiceResolver", "V21SoftwareDiscovery");
        AssertV21Constructor(index, "PlcCompileService", "V21CompilationAccess");
        AssertV21Constructor(index, "PlcVerificationService", "V21TypeServiceResolver", "V21OnlineVerificationAccess");
        AssertV21Constructor(index, "PlcBlockMutationService", "V21DocumentAccess", "V21EngineeringObjectAccess");
        AssertV21Constructor(index, "PlcMemberRenameService", "V21PlcTagStructureAccess", "V21EngineeringObjectAccess");
        AssertV21Constructor(index, "PlcImportExportService", "V21DocumentAccess", "V21EngineeringObjectAccess");
        AssertV21Constructor(index, "PlcSurfaceService", "V21TypeServiceResolver", "V21PlcTagStructureAccess", "V21EngineeringObjectAccess");
        AssertV21Constructor(index, "V21PlcTagRepository", "V21PlcTagStructureAccess", "V21EngineeringObjectAccess");
        AssertV21Constructor(index, "V21HardwareAdapter", "V21TypeServiceResolver");
        AssertV21Constructor(index, "HardwareTagMappingAuditWorkflow", "V21HardwareAdapter", "V21SoftwareDiscovery");
        AssertV21Constructor(index, "PlcCodeTargetRepository", "V21DocumentAccess", "V21EngineeringObjectAccess");
    }

    private static void AssertV21Constructor(ProductionSourceIndex index, string typeName, params string[] expected)
    {
        ClassDeclarationSyntax type = index.Files.Values
            .SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.ValueText == typeName);
        string[][] dependencySets = type.Members.OfType<ConstructorDeclarationSyntax>()
            .Select(constructor => constructor.ParameterList.Parameters
                .Select(parameter => (parameter.Type?.ToString().TrimEnd('?') ?? string.Empty).Split('.').Last())
                .Where(parameterType => parameterType.StartsWith("V21", StringComparison.Ordinal))
                .ToArray())
            .ToArray();

        Assert.IsTrue(dependencySets.Any(actual => actual.SequenceEqual(expected)),
            $"{typeName} V21 constructor dependencies were: {string.Join(" | ", dependencySets.Select(set => string.Join(", ", set)))}");
        Assert.IsFalse(dependencySets.SelectMany(set => set).Contains("V21OpennessAdapter"));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
