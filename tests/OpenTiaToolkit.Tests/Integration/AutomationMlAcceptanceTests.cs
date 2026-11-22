using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Core.Services.AutomationMl;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public class AutomationMlAcceptanceTests
{
    [TestMethod]
    public async Task PublicFixture_ImportsHardwareTopologyAndTags_ThenReapplyIsANoOp()
    {
        var sourcePath = Environment.GetEnvironmentVariable("OPEN_TIA_AUTOMATIONML_FIXTURE");
        if (string.IsNullOrWhiteSpace(sourcePath))
            Assert.Inconclusive("Set OPEN_TIA_AUTOMATIONML_FIXTURE to the EPLAN-to-TIA AML from Siemens example 109748224.");
        var workerPath = Environment.GetEnvironmentVariable("OPEN_TIA_ACCEPTANCE_WORKER_EXE");
        if (string.IsNullOrWhiteSpace(workerPath))
            Assert.Inconclusive("Set OPEN_TIA_ACCEPTANCE_WORKER_EXE to the registered V21 worker.");

        using (var source = File.OpenRead(sourcePath!))
        {
            var fixture = AmlReader.Read(source);
            Assert.AreEqual(0, fixture.Diagnostics.Count, string.Join(Environment.NewLine, fixture.Diagnostics));
            Assert.IsTrue(fixture.Objects.Any(item => item.Kind == "Device"));
            Assert.IsTrue(fixture.Objects.Any(item => item.Kind == "Subnet"));
            Assert.IsTrue(fixture.Objects.Any(item => item.Kind == "Tag"));
            Assert.IsTrue(fixture.Connections.Any(item => item.Kind == "NetworkConnection"));
        }

        var root = Path.Combine(Path.GetTempPath(), "ott-aml-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(root);
        using var session = await ToolkitSession.StartAsync(workerPath!, Path.Combine(root, "audit.jsonl"), CancellationToken.None);

        async Task<JsonElement> Call(string name, Dictionary<string, object>? arguments = null)
        {
            var result = await session.ExecuteAsync(new CommandInvocation
            {
                CommandName = name,
                Arguments = (arguments ?? new Dictionary<string, object>()).ToDictionary(
                    pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value))
            }, CancellationToken.None);
            Assert.IsTrue(result.Success, $"{name}: {result.Message} {result.Error?.Message} {result.Data}");
            return result.Data.GetValueOrDefault();
        }

        await Call("open-tia-portal", new() { ["with-ui"] = false });
        try
        {
            await Call("create-project", new()
            {
                ["project-path"] = Path.Combine(root, "AutomationMlAcceptance.ap21"),
                ["confirm"] = true
            });
            Assert.IsTrue((await Call("get-project-info")).GetProperty("IsToolkitOwned").GetBoolean());
            Assert.AreEqual(0, (await Call("list-devices")).GetArrayLength());

            var previewArgs = new Dictionary<string, object> { ["source-path"] = sourcePath! };
            var preview = await Call("apply-automationml", previewArgs);
            Assert.IsTrue(preview.GetProperty("canApply").GetBoolean());
            Assert.IsFalse(preview.GetProperty("plan").GetProperty("Matches").GetBoolean());
            Assert.AreEqual(0, (await Call("list-devices")).GetArrayLength());

            var applyArgs = new Dictionary<string, object>(previewArgs)
            {
                ["dry-run"] = false, ["confirm"] = true
            };
            var applied = await Call("apply-automationml", applyArgs);
            Assert.IsTrue(applied.GetProperty("verification").GetProperty("Matches").GetBoolean());
            Assert.IsTrue((await Call("list-devices")).GetArrayLength() > 0);

            var repeated = await Call("apply-automationml", applyArgs);
            Assert.IsTrue(repeated.GetProperty("plan").GetProperty("Matches").GetBoolean());
            Assert.AreEqual(0, repeated.GetProperty("plan").GetProperty("Differences").GetArrayLength());
            Assert.IsFalse(repeated.TryGetProperty("transfer", out _), "A no-op must return before invoking CAx import.");

            var plcs = (await Call("list-plcs")).EnumerateArray()
                .Where(plc => plc.GetProperty("IsConfirmedPlc").GetBoolean()).ToArray();
            Assert.IsTrue(plcs.Length > 0);
            foreach (var plc in plcs)
                await Call("compile-software", new() { ["plc-name"] = plc.GetProperty("Name").GetString()! });
        }
        finally
        {
            await Call("close-tia-portal", new() { ["confirm"] = true });
        }
    }
}
