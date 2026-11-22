using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public class AutomationMlAdditiveAcceptanceTests
{
    [TestMethod]
    public Task AddsRemoteIoAndTag_PreservesBaseline_AndRollsBackAnUnpluggableModule() =>
        Exercise(multiplePlcs: false);

    [TestMethod]
    public Task AddsEt200MpWhenAnotherPlcUsesTheSameAddress_AndBlocksAnOverlapOnTheOwningPlc() =>
        Exercise(multiplePlcs: true);

    private static async Task Exercise(bool multiplePlcs)
    {
        var archive = Fixture(multiplePlcs ? "OPEN_TIA_AUTOMATIONML_MULTIPLC_BASELINE" : "OPEN_TIA_AUTOMATIONML_BASELINE");
        var source = Fixture(multiplePlcs ? "OPEN_TIA_AUTOMATIONML_MULTIPLC_FIXTURE" : "OPEN_TIA_AUTOMATIONML_ADDITIVE_FIXTURE");
        var baselineDeviceCount = multiplePlcs ? 3 : 1;
        var worker = Fixture("OPEN_TIA_ACCEPTANCE_WORKER_EXE");
        Assert.IsTrue((File.GetAttributes(archive) & FileAttributes.ReadOnly) != 0, "Use an immutable baseline archive.");
        var originalHash = Hash(archive);
        var root = Path.Combine(Path.GetTempPath(), "ott-aml-add-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(root);

        using var session = await ToolkitSession.StartAsync(worker, Path.Combine(root, "audit.jsonl"), CancellationToken.None);
        async Task<CommandOutcome> Invoke(string command, Dictionary<string, object>? args = null)
        {
            var result = await session.ExecuteAsync(new CommandInvocation
            {
                CommandName = command,
                Arguments = (args ?? new Dictionary<string, object>()).ToDictionary(
                    item => item.Key, item => JsonSerializer.SerializeToElement(item.Value))
            }, CancellationToken.None);
            return result;
        }

        async Task<JsonElement> Call(string command, Dictionary<string, object>? args = null)
        {
            var result = await Invoke(command, args);
            Assert.IsTrue(result.Success, $"{command}: {result.Message} {result.Error?.Message} {result.Data}");
            return result.Data.GetValueOrDefault();
        }

        async Task<string> Retrieve(string folder)
        {
            await Call("retrieve-project", new()
            {
                ["archive-path"] = archive, ["target-directory"] = Path.Combine(root, folder), ["confirm"] = true
            });
            var project = await Call("get-project-info");
            Assert.IsTrue(project.GetProperty("IsToolkitOwned").GetBoolean());
            var path = project.GetProperty("Path").GetString()!;
            Assert.IsTrue(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(baselineDeviceCount, (await Call("list-devices")).GetArrayLength());
            return path;
        }

        var plc = new Dictionary<string, object> { ["plc-name"] = "SeedController" };
        Dictionary<string, object> Reconcile(string path, bool dryRun = true) =>
            new() { ["source-path"] = path, ["dry-run"] = dryRun, ["confirm"] = !dryRun };
        await Call("open-tia-portal", new() { ["with-ui"] = false });
        try
        {
            var successProject = await Retrieve("Success");
            await Call("compile-software", plc);
            var baselineBlocks = (await Call("get-blocks", plc)).GetRawText();
            var initialTags = (await Call("export-tags", plc)).GetProperty("Tags");
            Assert.AreEqual(1, initialTags.GetArrayLength());
            var originalTag = initialTags[0].GetRawText();

            var companion = new Dictionary<string, object> { ["plc-name"] = "CompanionController" };
            string? companionTags = null;
            if (multiplePlcs)
            {
                await Call("compile-software", companion);
                companionTags = (await Call("export-tags", companion)).GetProperty("Tags").GetRawText();
                StringAssert.Contains(companionTags, "CompanionReady");

                var conflictingSource = Path.Combine(root, "SamePlcOverlap.aml");
                MoveRemoteToCompanion(source, conflictingSource);
                var blocked = await Call("apply-automationml", Reconcile(conflictingSource));
                Assert.IsFalse(blocked.GetProperty("canApply").GetBoolean());
                StringAssert.Contains(blocked.GetProperty("additive").GetProperty("Conflicts").GetRawText(), "module I/O range overlaps");
                var denied = await Invoke("apply-automationml", Reconcile(conflictingSource, dryRun: false));
                Assert.IsFalse(denied.Success);
                Assert.AreEqual(baselineDeviceCount, (await Call("list-devices")).GetArrayLength());
            }

            var preview = await Call("apply-automationml", Reconcile(source));
            Assert.IsTrue(preview.GetProperty("canApply").GetBoolean(), preview.GetRawText());
            var planned = preview.GetProperty("additive");
            Assert.AreEqual(1, planned.GetProperty("Stations").GetArrayLength());
            if (multiplePlcs)
            {
                var station = planned.GetProperty("Stations")[0];
                Assert.AreEqual("System:Device.S71500", station.GetProperty("DeviceType").GetString());
                StringAssert.Contains(station.GetProperty("Head").GetProperty("TypeIdentifier").GetString(), "155-5AA01");
                Assert.AreEqual(20, station.GetProperty("Modules")[0].GetProperty("Addresses")[0].GetProperty("Start").GetInt32());
            }
            Assert.AreEqual(1, planned.GetProperty("Tags").GetArrayLength());
            Assert.AreEqual(baselineDeviceCount, (await Call("list-devices")).GetArrayLength());

            var applied = await Call("apply-automationml", Reconcile(source, dryRun: false));
            Assert.IsTrue(applied.GetProperty("verification").GetProperty("Matches").GetBoolean());
            Assert.IsTrue(applied.GetProperty("preservation").GetProperty("Matches").GetBoolean());
            Assert.AreEqual(baselineDeviceCount + 1, (await Call("list-devices")).GetArrayLength());
            var noOp = await Call("apply-automationml", Reconcile(source, dryRun: false));
            Assert.IsTrue(noOp.GetProperty("plan").GetProperty("Matches").GetBoolean());
            Assert.IsFalse(noOp.TryGetProperty("verification", out _));
            var finalTags = (await Call("export-tags", plc)).GetProperty("Tags").EnumerateArray().ToArray();
            Assert.AreEqual(2, finalTags.Length);
            Assert.IsTrue(finalTags.Any(tag => tag.GetRawText() == originalTag));
            Assert.AreEqual(baselineBlocks, (await Call("get-blocks", plc)).GetRawText());
            await Call("compile-software", plc);
            if (multiplePlcs)
            {
                Assert.AreEqual(companionTags, (await Call("export-tags", companion)).GetProperty("Tags").GetRawText());
                await Call("compile-software", companion);
            }
            await Call("save-project", new() { ["confirm"] = true });
            await Call("close-tia-portal", new() { ["confirm"] = true });
            await Call("open-tia-portal", new() { ["with-ui"] = false });
            await Call("open-project", new() { ["project-path"] = successProject, ["confirm"] = true });
            Assert.AreEqual(successProject, (await Call("get-project-info")).GetProperty("Path").GetString());
            var reopened = await Call("apply-automationml", Reconcile(source));
            Assert.IsTrue(reopened.GetProperty("plan").GetProperty("Matches").GetBoolean());
            Assert.AreEqual(baselineBlocks, (await Call("get-blocks", plc)).GetRawText());
            if (multiplePlcs)
                Assert.AreEqual(companionTags, (await Call("export-tags", companion)).GetProperty("Tags").GetRawText());
            await Call("close-project", new() { ["confirm"] = true });

            await Retrieve("Rollback");
            var unpluggable = Path.Combine(root, "InvalidSlot.aml");
            var document = XDocument.Load(source);
            var ns = document.Root!.Name.Namespace;
            var module = document.Descendants(ns + "InternalElement")
                .First(item => (string?)item.Attribute("Name") == "RemoteInputs" &&
                    item.Elements(ns + "Attribute").Any(attribute => (string?)attribute.Attribute("Name") == "TypeIdentifier"));
            module.Elements(ns + "Attribute").Single(attribute => (string?)attribute.Attribute("Name") == "PositionNumber")
                .Element(ns + "Value")!.Value = "9999";
            document.Save(unpluggable);
            var invalid = await Call("apply-automationml", Reconcile(unpluggable));
            Assert.IsTrue(invalid.GetProperty("canApply").GetBoolean(), invalid.GetRawText());
            var failure = await Invoke("apply-automationml", Reconcile(unpluggable, dryRun: false));
            Assert.IsFalse(failure.Success);
            Assert.IsTrue(failure.Data!.Value.GetProperty("rollbackVerified").GetBoolean(), failure.Data.Value.GetRawText());
            Assert.AreEqual(baselineDeviceCount, (await Call("list-devices")).GetArrayLength());
            Assert.AreEqual(originalTag, (await Call("export-tags", plc)).GetProperty("Tags")[0].GetRawText());
        }
        finally
        {
            try { await Call("close-tia-portal", new() { ["confirm"] = true }); }
            finally { Assert.AreEqual(originalHash, Hash(archive)); }
        }
    }

    private static void MoveRemoteToCompanion(string source, string destination)
    {
        var document = XDocument.Load(source);
        var ns = document.Root!.Name.Namespace;
        var hierarchy = document.Root.Element(ns + "InstanceHierarchy")!;
        var objects = hierarchy.Descendants(ns + "InternalElement").ToArray();
        XElement Find(string name) => objects.First(item => (string?)item.Attribute("Name") == name);
        var remote = Find("RemoteStation");
        var remoteIds = new HashSet<string>(remote.DescendantsAndSelf(ns + "InternalElement")
            .Select(item => (string?)item.Attribute("ID")).OfType<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var (from, to) in new[] { ("SeedIO", "CompanionIO"), ("SeedPN", "CompanionPN") })
        {
            var oldId = (string?)Find(from).Attribute("ID") ?? throw new InvalidDataException(from);
            var newId = (string?)Find(to).Attribute("ID") ?? throw new InvalidDataException(to);
            var endpoints = hierarchy.Descendants(ns + "InternalLink")
                .SelectMany(link => new[] { link.Attribute("RefPartnerSideA"), link.Attribute("RefPartnerSideB") }.OfType<XAttribute>())
                .Where(endpoint => endpoint.Value.StartsWith(oldId + ":", StringComparison.OrdinalIgnoreCase) &&
                    endpoint.Parent!.Attributes().Any(peer => peer != endpoint &&
                        remoteIds.Contains(peer.Value.Split(':')[0]))).ToArray();
            Assert.AreEqual(1, endpoints.Length, $"Expected one connection from the new station to {from}.");
            endpoints[0].Value = newId + endpoints[0].Value.Substring(oldId.Length);
        }

        remote.Descendants(ns + "Attribute").Single(attribute => (string?)attribute.Attribute("Name") == "NetworkAddress")
            .Element(ns + "Value")!.Value = "192.168.11.3";
        document.Save(destination);
    }

    private static string Fixture(string variable) =>
        Environment.GetEnvironmentVariable(variable) is string path && File.Exists(path)
            ? path : throw new AssertInconclusiveException($"Set {variable} to the local acceptance fixture.");

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(stream));
    }
}
