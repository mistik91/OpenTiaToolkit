using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class GodClassDebtBaselineTests
{
    [TestMethod]
    public void ProductionSources_DoNotIncreaseKnownArchitectureDebt()
    {
        string root = FindRepositoryRoot();
        DebtBaseline baseline = LoadBaseline(root);
        IReadOnlyDictionary<string, int> sources = MeasureProductionSources(root);

        IReadOnlyList<string> violations = FindViolations(sources, baseline);

        Assert.AreEqual(
            0,
            violations.Count,
            "Architecture-debt ratchet violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void RatchetRules_RejectNewLargeFilesAndPartialClassAggregateGrowth()
    {
        DebtBaseline baseline = LoadBaseline(FindRepositoryRoot());
        var newLargeFile = new Dictionary<string, int>
        {
            ["src/OpenTiaToolkit.Core/Services/NewLargeService.cs"] = baseline.ScreeningThreshold + 1
        };
        IReadOnlyList<string> screeningViolations = FindViolations(newLargeFile, baseline);
        Assert.IsTrue(screeningViolations.Any(message => message.Contains("NewLargeService.cs", StringComparison.Ordinal)));

        var spreadAcrossPartials = new Dictionary<string, int>
        {
            ["src/OpenTiaToolkit.Core/Services/PlcDocumentWorkflowService.cs"] = 737,
            ["src/OpenTiaToolkit.Core/Services/PlcDocumentWorkflowService.One.cs"] = 736,
            ["src/OpenTiaToolkit.Core/Services/PlcDocumentWorkflowService.Two.cs"] = 736,
            ["src/OpenTiaToolkit.Core/Services/PlcDocumentWorkflowService.Three.cs"] = 737
        };
        var aggregateFixture = new DebtBaseline
        {
            ScreeningThreshold = baseline.ScreeningThreshold,
            Hotspots = baseline.Hotspots,
            Aggregate = new AggregateBaseline
            {
                Name = "Synthetic partial aggregate",
                PathPrefix = "src/OpenTiaToolkit.Core/Services/PlcDocumentWorkflowService",
                PathSuffix = ".cs",
                Baseline = 2945
            }
        };
        IReadOnlyList<string> aggregateViolations = FindViolations(spreadAcrossPartials, aggregateFixture);
        Assert.IsTrue(aggregateViolations.Any(message => message.Contains(aggregateFixture.Aggregate!.Name, StringComparison.Ordinal)));

        Assert.AreEqual(0, FindViolations(new Dictionary<string, int>(), baseline).Count,
            "Removing a hotspot during decomposition must not fail the ratchet.");
    }

    private static IReadOnlyList<string> FindViolations(
        IReadOnlyDictionary<string, int> sources,
        DebtBaseline baseline)
    {
        var violations = new List<string>();
        const string reviewGuidance = "The baseline should only be raised after explicit architecture review.";

        foreach ((string path, int allowed) in baseline.Hotspots)
        {
            if (sources.TryGetValue(path, out int measured) && measured > allowed)
            {
                violations.Add($"{path}: measured {measured} physical lines; allowed baseline {allowed}. {reviewGuidance}");
            }
        }

        foreach ((string path, int measured) in sources)
        {
            if (!baseline.Hotspots.ContainsKey(path) && measured > baseline.ScreeningThreshold)
            {
                violations.Add(
                    $"{path}: measured {measured} physical lines; allowed screening baseline {baseline.ScreeningThreshold}. " +
                    $"This threshold screens new large files; it does not by itself classify a god class. {reviewGuidance}");
            }
        }

        if (baseline.Aggregate != null)
        {
            int aggregateLines = sources
                .Where(source => source.Key.StartsWith(baseline.Aggregate.PathPrefix, StringComparison.Ordinal) &&
                                 source.Key.EndsWith(baseline.Aggregate.PathSuffix, StringComparison.Ordinal))
                .Sum(source => source.Value);
            if (aggregateLines > baseline.Aggregate.Baseline)
            {
                violations.Add(
                    $"{baseline.Aggregate.Name}: measured {aggregateLines} physical lines; allowed baseline {baseline.Aggregate.Baseline}. " +
                    $"Splitting a class across partial files does not reduce shared state or coupling. {reviewGuidance}");
            }
        }

        return violations;
    }

    private static IReadOnlyDictionary<string, int> MeasureProductionSources(string root)
    {
        string sourceRoot = Path.Combine(root, "src");
        return Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"))
            .ToDictionary(
                path => NormalizePath(Path.GetRelativePath(root, path)),
                path => File.ReadLines(path).Count(),
                StringComparer.Ordinal);
    }

    private static bool HasDirectorySegment(string path, string segment)
    {
        string normalized = NormalizePath(path);
        return normalized.Contains("/" + segment + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static DebtBaseline LoadBaseline(string root)
    {
        string path = Path.Combine(root, "tests", "OpenTiaToolkit.ArchitectureTests", "god-class-debt-baseline.json");
        return JsonSerializer.Deserialize<DebtBaseline>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Could not read architecture-debt baseline: {path}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class DebtBaseline
    {
        public int ScreeningThreshold { get; set; }
        public Dictionary<string, int> Hotspots { get; set; } = new(StringComparer.Ordinal);
        public AggregateBaseline? Aggregate { get; set; }
    }

    private sealed class AggregateBaseline
    {
        public string Name { get; set; } = string.Empty;
        public string PathPrefix { get; set; } = string.Empty;
        public string PathSuffix { get; set; } = string.Empty;
        public int Baseline { get; set; }
    }
}
