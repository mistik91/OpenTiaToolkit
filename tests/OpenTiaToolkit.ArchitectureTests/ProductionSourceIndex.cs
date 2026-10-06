using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenTiaToolkit.ArchitectureTests;

internal sealed class ProductionSourceIndex
{
    private static readonly string[] ExcludedDirectorySegments =
    {
        "bin", "obj", "artifacts", "TestResults", "node_modules", ".git"
    };

    private ProductionSourceIndex(
        IReadOnlyDictionary<string, SourceFileInfo> files,
        IReadOnlyDictionary<string, NamedTypeInfo> types)
    {
        Files = files;
        Types = types;
    }

    public IReadOnlyDictionary<string, SourceFileInfo> Files { get; }

    public IReadOnlyDictionary<string, NamedTypeInfo> Types { get; }

    public static ProductionSourceIndex Create(string repositoryRoot)
    {
        string sourceRoot = Path.Combine(repositoryRoot, "src");
        var sources = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .ToDictionary(
                path => NormalizePath(Path.GetRelativePath(repositoryRoot, path)),
                File.ReadAllText,
                StringComparer.Ordinal);
        return CreateFromSources(sources);
    }

    internal static ProductionSourceIndex CreateFromSources(IReadOnlyDictionary<string, string> sources)
    {
        var normalizedSources = sources.ToDictionary(
            source => NormalizePath(source.Key), source => source.Value, StringComparer.Ordinal);
        var trees = normalizedSources
            .OrderBy(source => source.Key, StringComparer.Ordinal)
            .Select(source => CSharpSyntaxTree.ParseText(source.Value, path: source.Key))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "OpenTiaToolkit.SourceOnlyArchitectureIndex",
            trees,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var files = new SortedDictionary<string, SourceFileInfo>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, List<TypeDeclarationPart>>(StringComparer.Ordinal);

        foreach (SyntaxTree tree in trees)
        {
            string path = NormalizePath(tree.FilePath);
            string source = normalizedSources[path];
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            files.Add(path, new SourceFileInfo(path, CountPhysicalLines(source), root));
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

            foreach (BaseTypeDeclarationSyntax declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol) continue;
                string identity = GetTypeIdentity(symbol);
                FileLinePositionSpan span = declaration.GetLocation().GetLineSpan();
                int lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
                if (!declarations.TryGetValue(identity, out List<TypeDeclarationPart>? parts))
                {
                    parts = new List<TypeDeclarationPart>();
                    declarations.Add(identity, parts);
                }
                parts.Add(new TypeDeclarationPart(path, lines, declaration));
            }
        }

        var types = declarations
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                item => item.Key,
                item => new NamedTypeInfo(
                    item.Key,
                    item.Value.Sum(part => part.PhysicalLines),
                    item.Value.OrderBy(part => part.Path, StringComparer.Ordinal).ToArray()),
                StringComparer.Ordinal);
        return new ProductionSourceIndex(files, types);
    }

    private static bool IsProductionSource(string path)
    {
        string normalized = NormalizePath(path);
        if (ExcludedDirectorySegments.Any(segment =>
                normalized.Split('/').Contains(segment, StringComparer.OrdinalIgnoreCase))) return false;
        string fileName = Path.GetFileName(normalized);
        return !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTypeIdentity(INamedTypeSymbol symbol)
    {
        var containingTypes = new Stack<string>();
        for (INamedTypeSymbol? current = symbol; current != null; current = current.ContainingType)
            containingTypes.Push(current.MetadataName);

        string typePath = string.Join("+", containingTypes);
        string namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToDisplayString()
            : "<global>";
        return namespaceName + "." + typePath;
    }

    private static int CountPhysicalLines(string source)
    {
        if (source.Length == 0) return 0;
        int count = 0;
        using var reader = new StringReader(source);
        while (reader.ReadLine() != null) count++;
        return count;
    }

    internal static string NormalizePath(string path) => path.Replace('\\', '/');
}

internal sealed record SourceFileInfo(string Path, int PhysicalLines, CompilationUnitSyntax Root);

internal sealed record TypeDeclarationPart(string Path, int PhysicalLines, BaseTypeDeclarationSyntax Declaration);

internal sealed record NamedTypeInfo(string Identity, int PhysicalLines, IReadOnlyList<TypeDeclarationPart> Declarations)
{
    public IReadOnlyList<string> DeclarationFiles => Declarations
        .Select(declaration => declaration.Path)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();
}

internal static class RepositoryPaths
{
    public static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "OpenTiaToolkit.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
