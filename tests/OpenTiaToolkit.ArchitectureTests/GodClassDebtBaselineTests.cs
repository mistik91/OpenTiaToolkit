using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class GodClassDebtBaselineTests
{
    [TestMethod]
    public void ProductionSources_DoNotIncreaseKnownArchitectureDebt()
    {
        string root = RepositoryPaths.FindRoot();
        DebtBaseline baseline = LoadBaseline(root);
        ProductionSourceIndex index = ProductionSourceIndex.Create(root);

        IReadOnlyList<string> violations = ArchitectureDebtRatchet.FindViolations(index, baseline);

        Assert.AreEqual(
            0,
            violations.Count,
            "Architecture-debt ratchet violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void AggregateGuard_DiscoversPartialTypeWithoutManualConfiguration()
    {
        string first = PartialDeclaration("namespace Example; public partial class SplitService", 600);
        string second = PartialDeclaration("namespace Example; public partial class SplitService", 600);
        ProductionSourceIndex index = Index(("src/One.cs", first), ("src/Two.cs", second));

        IReadOnlyList<string> violations = ArchitectureDebtRatchet.FindViolations(index, EmptyBaseline());

        string violation = violations.Single(message => message.Contains("Example.SplitService", StringComparison.Ordinal));
        StringAssert.Contains(violation, "src/One.cs, src/Two.cs");
        StringAssert.Contains(violation, "allowed 1000");
        StringAssert.Contains(violation, "explicit architecture review");
        Assert.IsTrue(index.Files.Values.All(file => file.PhysicalLines < 1000));
    }

    [TestMethod]
    public void TypeIdentity_DistinguishesNamespacesNestedContainersAndGenericArity()
    {
        ProductionSourceIndex index = Index(
            ("src/Namespaces.cs", "namespace One { class Shared { } } namespace Two { class Shared { } }"),
            ("src/Nested.cs", "namespace N { class Left { class Shared { } } class Right { class Shared { } } }"),
            ("src/Generics.cs", "namespace N; class Generic<T> { } class Generic<T1, T2> { }"));

        string[] expected =
        {
            "One.Shared", "Two.Shared", "N.Left+Shared", "N.Right+Shared", "N.Generic`1", "N.Generic`2"
        };
        foreach (string identity in expected) Assert.IsTrue(index.Types.ContainsKey(identity), identity);
        Assert.AreEqual(expected.Length + 2, index.Types.Count, "The two containing types are indexed independently too.");
    }

    [TestMethod]
    public void SyntaxAnalysis_IgnoresPartialClassTextInCommentsAndStrings()
    {
        ProductionSourceIndex index = Index(("src/Text.cs", """
            namespace Example;
            // partial class Phantom { }
            class Real
            {
                private const string Text = "partial class AlsoPhantom { }";
            }
            """));

        CollectionAssert.AreEqual(new[] { "Example.Real" }, index.Types.Keys.ToArray());
    }

    [TestMethod]
    public void TypeIndex_DiscoversAllNamedTypeKindsAndNamespaceForms()
    {
        ProductionSourceIndex index = Index(
            ("src/BlockScoped.cs", "namespace Block.Scoped { class ClassType { } record RecordType; }"),
            ("src/FileScoped.cs", "namespace File.Scoped; struct StructType { } interface InterfaceType { }"));

        CollectionAssert.AreEqual(
            new[] { "Block.Scoped.ClassType", "Block.Scoped.RecordType", "File.Scoped.InterfaceType", "File.Scoped.StructType" },
            index.Types.Keys.ToArray());
    }

    [TestMethod]
    public void PerFileScreen_StillRejectsNewUnlistedLargeFile()
    {
        string source = string.Join(Environment.NewLine, Enumerable.Repeat("// review signal", 1001));
        IReadOnlyList<string> violations = ArchitectureDebtRatchet.FindViolations(
            Index(("src/NewLargeService.cs", source)), EmptyBaseline());

        StringAssert.Contains(violations.Single(), "src/NewLargeService.cs");
        StringAssert.Contains(violations.Single(), "allowed screening threshold 1000");
    }

    [TestMethod]
    public void ShrinkingOrRemovingReviewedCandidates_RemainsAllowed()
    {
        var baseline = EmptyBaseline();
        baseline.ReviewedFileGrowthCeilings["src/FormerCandidate.cs"] = 900;
        baseline.TypeAggregateExceptions["Example.FormerPartial"] = 1200;

        Assert.AreEqual(0, ArchitectureDebtRatchet.FindViolations(Index(), baseline).Count);
        Assert.AreEqual(0, ArchitectureDebtRatchet.FindViolations(
            Index(("src/FormerCandidate.cs", "namespace Example; class FormerPartial { }")), baseline).Count);
    }

    private static ProductionSourceIndex Index(params (string Path, string Source)[] sources) =>
        ProductionSourceIndex.CreateFromSources(sources.ToDictionary(item => item.Path, item => item.Source, StringComparer.Ordinal));

    private static string PartialDeclaration(string declaration, int bodyLines) =>
        declaration + Environment.NewLine + "{" + Environment.NewLine +
        string.Join(Environment.NewLine, Enumerable.Repeat("    // declaration body", bodyLines)) +
        Environment.NewLine + "}";

    private static DebtBaseline EmptyBaseline() => new() { ScreeningThreshold = 1000 };

    private static DebtBaseline LoadBaseline(string root)
    {
        string path = Path.Combine(root, "tests", "OpenTiaToolkit.ArchitectureTests", "god-class-debt-baseline.json");
        return JsonSerializer.Deserialize<DebtBaseline>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Could not read architecture-debt baseline: {path}");
    }
}

internal static class ArchitectureDebtRatchet
{
    private const string ReviewGuidance =
        "A baseline increase requires explicit architecture review and must never be generated automatically.";

    public static IReadOnlyList<string> FindViolations(ProductionSourceIndex index, DebtBaseline baseline)
    {
        var violations = new List<string>();
        var fileAllowances = baseline.HistoricalHotspots
            .Concat(baseline.ReviewedFileGrowthCeilings)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        foreach ((string path, int allowed) in fileAllowances.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (index.Files.TryGetValue(path, out SourceFileInfo? file) && file.PhysicalLines > allowed)
                violations.Add($"{path}: measured {file.PhysicalLines} physical lines; allowed reviewed ceiling {allowed}. {ReviewGuidance}");
        }

        foreach (SourceFileInfo file in index.Files.Values.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            if (!fileAllowances.ContainsKey(file.Path) && file.PhysicalLines > baseline.ScreeningThreshold)
            {
                violations.Add(
                    $"{file.Path}: measured {file.PhysicalLines} physical lines; allowed screening threshold {baseline.ScreeningThreshold}. " +
                    $"This is a review signal, not an automatic god-class classification. {ReviewGuidance}");
            }
        }

        foreach (NamedTypeInfo type in index.Types.Values.OrderBy(type => type.Identity, StringComparer.Ordinal))
        {
            int allowed = baseline.TypeAggregateExceptions.TryGetValue(type.Identity, out int exception)
                ? exception
                : baseline.ScreeningThreshold;
            if (type.PhysicalLines > allowed)
            {
                violations.Add(
                    $"{type.Identity}: declarations [{string.Join(", ", type.DeclarationFiles)}]; " +
                    $"measured aggregate {type.PhysicalLines} physical declaration lines; allowed {allowed}. " +
                    $"Splitting a type across partial files does not evade aggregate review. {ReviewGuidance}");
            }
        }

        return violations;
    }
}

internal sealed class DebtBaseline
{
    public int ScreeningThreshold { get; set; }
    public Dictionary<string, int> HistoricalHotspots { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ReviewedFileGrowthCeilings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> TypeAggregateExceptions { get; set; } = new(StringComparer.Ordinal);
}
