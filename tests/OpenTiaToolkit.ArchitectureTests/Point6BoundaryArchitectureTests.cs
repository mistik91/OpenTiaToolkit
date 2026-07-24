using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class Point6BoundaryArchitectureTests
{
    [TestMethod]
    public void CompatibilityFacades_RemainSmallAndDelegationOnly()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        AssertFacade(index, "src/OpenTiaToolkit.Core/Services/V21HardwareAdapter.cs", "V21HardwareAdapter", 150);
        AssertFacade(index, "src/OpenTiaToolkit.Core/Services/PlcImportExportService.cs", "PlcImportExportService", 110);
        AssertFacade(index, "src/OpenTiaToolkit.Core/Services/PlcSurfaceService.cs", "PlcSurfaceService", 110);
    }

    [TestMethod]
    public void ExtractedComponents_AreFocusedAndDoNotDependBackOnFacades()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        string[] files =
        {
            "src/OpenTiaToolkit.Core/Services/V21/V21HardwareCatalogAccess.cs",
            "src/OpenTiaToolkit.Core/Services/V21/V21HardwareAttributeAccess.cs",
            "src/OpenTiaToolkit.Core/Services/V21/V21HardwareDriveAccess.cs",
            "src/OpenTiaToolkit.Core/Services/V21/V21HardwareNetworkAccess.cs",
            "src/OpenTiaToolkit.Core/Services/PlcXmlTransferService.cs",
            "src/OpenTiaToolkit.Core/Services/PlcDocumentTransferService.cs",
            "src/OpenTiaToolkit.Core/Services/PlcTagExportService.cs",
            "src/OpenTiaToolkit.Core/Services/PlcCrossReferenceService.cs",
            "src/OpenTiaToolkit.Core/Services/PlcAncillarySurfaceReader.cs",
            "src/OpenTiaToolkit.Core/Services/PlcExternalSourceService.cs"
        };

        foreach (string path in files)
        {
            SourceFileInfo file = index.Files[path];
            Assert.IsTrue(file.PhysicalLines <= 500, $"{path} must remain at or below 500 physical lines.");
            string source = file.Root.ToFullString();
            Assert.IsFalse(source.Contains("V21HardwareAdapter", StringComparison.Ordinal), path);
            Assert.IsFalse(source.Contains("PlcImportExportService", StringComparison.Ordinal), path);
            Assert.IsFalse(source.Contains("PlcSurfaceService", StringComparison.Ordinal), path);
            Assert.IsFalse(file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Any(type => type.Modifiers.Any(modifier => modifier.ValueText == "partial")), path);
        }
    }

    [TestMethod]
    public void ProductionHardwareConsumers_DoNotDependOnCompatibilityAdapter()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        string[] consumers = index.Files.Values
            .Where(file => file.Path.Contains("/Services/Hardware", StringComparison.Ordinal)
                || file.Path.EndsWith("/Services/HardwareService.cs", StringComparison.Ordinal))
            .SelectMany(file => file.Root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => identifier.Identifier.ValueText == "V21HardwareAdapter")
                .Select(_ => file.Path))
            .ToArray();
        Assert.AreEqual(0, consumers.Length, "Broad hardware adapter consumers: " + string.Join(", ", consumers));
    }

    [TestMethod]
    public void ResponsibilityOwners_StayExplicit()
    {
        ProductionSourceIndex index = ProductionSourceIndex.Create(RepositoryPaths.FindRoot());
        AssertOwns(index, "PlcCrossReferenceService", "FindPotentialTargets", "FlattenEntry", "WithTargetDetail");
        AssertOwns(index, "PlcExternalSourceService", "TryCreateExternalSource", "TryGenerateSourceFile");
        AssertOwns(index, "PlcDocumentTransferService", "EnsureSupported", "ParseImportOption");
        AssertOwns(index, "V21HardwareDriveAccess", "ConfigureDriveTelegram", "GetDriveParameters");
        AssertOwns(index, "V21HardwareNetworkAccess", "GetNetworkInfo", "ConnectIoConnector");
        AssertOwns(index, "V21HardwareAttributeAccess", "GetGsdParameterData", "SetIoAddressStart");
    }

    private static void AssertFacade(ProductionSourceIndex index, string path, string typeName, int maximumLines)
    {
        SourceFileInfo file = index.Files[path];
        ClassDeclarationSyntax type = file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == typeName);
        Assert.IsTrue(file.PhysicalLines < maximumLines, $"{path} must remain below {maximumLines} lines.");
        Assert.IsTrue(type.Modifiers.Any(modifier => modifier.ValueText == "sealed"));
        Assert.AreEqual(0, type.DescendantNodes().OfType<ForEachStatementSyntax>().Count());
        Assert.AreEqual(0, type.DescendantNodes().OfType<WhileStatementSyntax>().Count());
        Assert.AreEqual(0, type.DescendantNodes().OfType<ForStatementSyntax>().Count());
        Assert.AreEqual(0, type.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Count(identifier => identifier.Identifier.ValueText is "BindingFlags" or "MethodInfo" or "PropertyInfo"));
        Assert.IsTrue(type.Members.OfType<MethodDeclarationSyntax>().All(method => method.ExpressionBody != null),
            $"{typeName} methods must remain direct expression-bodied delegation.");
    }

    private static void AssertOwns(ProductionSourceIndex index, string typeName, params string[] methods)
    {
        ClassDeclarationSyntax type = index.Files.Values
            .SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(candidate => candidate.Identifier.ValueText == typeName);
        string[] owned = type.Members.OfType<MethodDeclarationSyntax>()
            .Select(method => method.Identifier.ValueText)
            .ToArray();
        foreach (string method in methods) CollectionAssert.Contains(owned, method, $"{typeName} must own {method}.");
    }
}
