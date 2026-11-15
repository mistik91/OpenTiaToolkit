using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class PlcDocumentExportWorkerIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RecursiveBulkExport_MatchesSingleDocumentsAndIsolatesFailuresThroughWorker()
    {
        if (!bool.TryParse(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_DOCUMENT_EXPORT_DISPOSABLE"), out var disposable) || !disposable)
            Assert.Inconclusive("Opt in to disposable document export qualification first.");
        var archive = Path.GetFullPath(Required("OPEN_TIA_TEST_DOCUMENT_EXPORT_ARCHIVE_PATH"));
        var worker = Path.GetFullPath(Required("OPEN_TIA_TEST_DOCUMENT_EXPORT_WORKER_PATH"));
        var root = Path.Combine(Path.GetFullPath(Required("OPEN_TIA_TEST_DOCUMENT_EXPORT_ROOT")), "docs-" + Guid.NewGuid().ToString("N"));
        Assert.IsTrue(File.Exists(archive));
        Assert.IsTrue(File.Exists(worker));
        Assert.IsFalse(Directory.Exists(root));
        Directory.CreateDirectory(root);
        var archiveHash = Hash(archive);
        File.WriteAllText(Path.Combine(root, "environment.json"), JsonSerializer.Serialize(new { worker, workerHash = Hash(worker), archive, archiveHash }, Json));
        var projectPath = string.Empty;
        var before = string.Empty;
        var sequence = 0;
        try
        {
            // A fresh worker and portal on the second pass prove exports did not mutate the saved fixture.
            for (var pass = 0; pass < 2; pass++)
            {
                using var session = await ToolkitSession.StartAsync(worker, Path.Combine(root, $"audit-{pass}.jsonl"), CancellationToken.None);
                var ownsPortal = false;
                async Task<CommandOutcome> Send(string command, params (string Name, object Value)[] args)
                {
                    var outcome = await session.ExecuteAsync(new CommandInvocation
                    {
                        CommandName = command,
                        Arguments = args.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value))
                    }, CancellationToken.None);
                    var path = Path.Combine(root, $"{++sequence:D3}-{command}.json");
                    File.WriteAllText(path, JsonSerializer.Serialize(outcome, Json));
                    TestContext.AddResultFile(path);
                    return outcome;
                }
                async Task<T> Read<T>(string command, params (string Name, object Value)[] args)
                {
                    var outcome = await Send(command, args);
                    Assert.IsTrue(outcome.Success, outcome.Message);
                    return Deserialize<T>(outcome);
                }
                try
                {
                    Assert.IsTrue((await Send("list-instances")).Success);
                    Assert.IsTrue((await Send("open-tia-portal", ("with-ui", false))).Success);
                    ownsPortal = true;
                    if (pass == 0)
                        Assert.IsTrue((await Send("retrieve-project", ("archive-path", archive), ("target-directory", Path.Combine(root, "retrieved")), ("confirm", true))).Success);
                    else Assert.IsTrue((await Send("open-project", ("project-path", projectPath))).Success);
                    var project = await Read<ProjectInfo>("get-project-info");
                    Assert.IsTrue(project.IsToolkitOwned);
                    Assert.IsFalse(project.IsPortalVisible);
                    Assert.IsTrue(Path.GetFullPath(project.Path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                    projectPath = project.Path;
                    var plc = (await Read<PlcInfo[]>("list-plcs")).Single().Name;
                    var tree = (await Read<TreeResult>("get-software-tree", ("plc-name", plc))).Tree;
                    var baseline = await Read<CompilationResult>("compile-software", ("plc-name", plc));
                    Assert.AreEqual(0, baseline.ErrorCount);
                    if (pass == 1)
                    {
                        Assert.AreEqual(before, JsonSerializer.Serialize(tree, Json));
                        continue;
                    }
                    before = JsonSerializer.Serialize(tree, Json);
                    var blocks = tree.Children.Single(node => node.Type == "BlockContainer");
                    var targets = Descendants(blocks).Where(node => new[] { "FC", "FB", "OB", "DB", "GlobalDB", "InstanceDB" }.Contains(node.Type.Split(' ')[0])).ToArray();
                    Assert.IsTrue(targets.Any(node => !node.Path.Substring(blocks.Path.Length + 1).Contains('/')), "Fixture needs a root block.");
                    Assert.IsTrue(targets.Any(node => node.Path.Substring(blocks.Path.Length + 1).Count(c => c == '/') >= 2), "Fixture needs a block at least two groups deep.");
                    Assert.IsTrue(targets.Select(node => node.Type).Distinct().Count() >= 2, "Fixture needs multiple block types.");
                    Assert.AreEqual(targets.Length, targets.Select(node => node.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                    var singles = Path.Combine(root, "single");
                    var supported = new List<string>();
                    foreach (var target in targets)
                        if ((await Send("export-block-as-docs", ("plc-name", plc), ("block-name", target.Name), ("export-path", singles))).Success) supported.Add(target.Name);
                    Assert.IsTrue(supported.Count >= 2, "Fixture needs two exportable blocks.");
                    var bulkPath = Path.Combine(root, "bulk");
                    var bulk = Deserialize<PlcExportCommandResult>(await Send("export-blocks-as-docs", ("plc-name", plc), ("export-path", bulkPath)));
                    Assert.AreEqual(targets.Length, bulk.Summary.Total);
                    Assert.AreEqual(supported.Count, bulk.Summary.Succeeded);
                    CollectionAssert.AreEquivalent(supported.ToArray(), bulk.Exported.ToArray());
                    CollectionAssert.AreEquivalent(targets.Select(node => node.Name).Except(supported).ToArray(), bulk.Failed.ToArray());
                    foreach (var file in Directory.GetFiles(singles))
                        Assert.AreEqual(Hash(file), Hash(Path.Combine(bulkPath, Path.GetFileName(file))), "Single/bulk document bytes differ: " + file);
                    Assert.AreEqual(Directory.GetFiles(singles).Length, Directory.GetFiles(bulkPath).Length);
                    var nested = targets.First(node => supported.Contains(node.Name) && node.Path.Substring(blocks.Path.Length + 1).Contains('/')).Name;
                    var filtered = await Read<PlcExportCommandResult>("export-blocks-as-docs", ("plc-name", plc), ("filter-regex", "^" + Regex.Escape(nested.ToLowerInvariant()) + "$"), ("export-path", Path.Combine(root, "filtered")));
                    Assert.AreEqual(1, filtered.Summary.Total);
                    CollectionAssert.AreEqual(new[] { nested }, filtered.Exported.ToArray());
                    foreach (var filter in new[] { "^OTT_NoBlock_" + Guid.NewGuid().ToString("N") + "$", "[" })
                    {
                        var path = Path.Combine(root, filter == "[" ? "invalid" : "missing");
                        var outcome = await Send("export-blocks-as-docs", ("plc-name", plc), ("filter-regex", filter), ("export-path", path));
                        Assert.IsFalse(outcome.Success);
                        var result = Deserialize<PlcExportCommandResult>(outcome);
                        Assert.AreEqual(0, result.Summary.Total);
                        Assert.AreEqual(0, result.Summary.Succeeded);
                        if (filter == "[") { Assert.AreEqual(1, result.Errors.Count); Assert.IsFalse(Directory.Exists(path)); }
                        else { Assert.AreEqual(0, result.Errors.Count); Assert.AreEqual(0, Directory.GetFiles(path).Length); }
                    }
                    var failurePath = Path.Combine(root, "partial");
                    var blockedName = supported[0];
                    Directory.CreateDirectory(Path.Combine(failurePath, blockedName + ".s7dcl"));
                    var partial = await Send("export-blocks-as-docs", ("plc-name", plc), ("filter-regex", "^(" + string.Join("|", supported.Select(Regex.Escape)) + ")$"), ("export-path", failurePath));
                    Assert.IsFalse(partial.Success);
                    var partialResult = Deserialize<PlcExportCommandResult>(partial);
                    Assert.AreEqual(supported.Count - 1, partialResult.Summary.Succeeded);
                    Assert.AreEqual(1, partialResult.Summary.Failed);
                    CollectionAssert.AreEqual(new[] { blockedName }, partialResult.Failed.ToArray());
                    var after = await Read<CompilationResult>("compile-software", ("plc-name", plc));
                    Assert.AreEqual(baseline.ErrorCount, after.ErrorCount);
                    Assert.AreEqual(baseline.WarningCount, after.WarningCount);
                    Assert.AreEqual(before, JsonSerializer.Serialize((await Read<TreeResult>("get-software-tree", ("plc-name", plc))).Tree, Json));
                }
                finally
                {
                    if (ownsPortal)
                    {
                        try { Assert.IsTrue((await Send("close-project", ("save", false), ("confirm", true))).Success); }
                        finally { Assert.IsTrue((await Send("close-tia-portal", ("confirm", true))).Success); }
                    }
                }
            }
        }
        finally { Assert.AreEqual(archiveHash, Hash(archive)); }
    }

    private static T Deserialize<T>(CommandOutcome outcome) => JsonSerializer.Deserialize<T>(outcome.Data!.Value.GetRawText(), Json)!;
    private static IEnumerable<TreeNode> Descendants(TreeNode node) => node.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private sealed class TreeResult { public TreeNode Tree { get; set; } = new(); }
    private sealed class TreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<TreeNode> Children { get; set; } = new();
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream));
    }
    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive($"Set {name} for disposable document export qualification.");
        return value!;
    }
}
