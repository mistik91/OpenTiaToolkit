using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Blueprints;
using OpenTiaToolkit.Contracts.Blueprints;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Tests.Execution;

[TestClass]
public sealed class BlueprintStepExecutorTests
{
    [DataTestMethod]
    [DataRow("{\"SafetyBlocked\":true,\"Applied\":false}", "PolicyDenied")]
    [DataRow("{\"Applied\":false,\"SafetyBlocked\":true}", "PolicyDenied")]
    [DataRow("{\"safetyblocked\":true}", "PolicyDenied")]
    [DataRow("{\"ValidationBlocked\":true,\"Applied\":false}", "ValidationFailed")]
    [DataRow("{\"RolledBack\":true}", "PartialFailure")]
    [DataRow("{\"DryRun\":true}", "PartialFailure")]
    [DataRow("{\"Applied\":false}", "PartialFailure")]
    [DataRow("{\"Success\":false}", "PartialFailure")]
    [DataRow("{\"Verified\":false}", "PartialFailure")]
    [DataRow("{\"StateMatched\":false}", "PartialFailure")]
    [DataRow("{\"Applied\":true,\"applied\":false}", "PartialFailure")]
    [DataRow("{\"SafetyBlocked\":true,\"safetyblocked\":false}", "PolicyDenied")]
    [DataRow("{\"SafetyBlocked\":\"true\"}", "ValidationFailed")]
    [DataRow("{\"Applied\":1}", "ValidationFailed")]
    [DataRow("{\"Verified\":null}", "ValidationFailed")]
    [DataRow("{\"DryRun\":{}}", "ValidationFailed")]
    public async Task StructuredIncompleteOutcome_StopsSuffixAndRetainsSuccessfulPrefix(string json, string errorCode)
    {
        var calls = new List<string>();
        CommandOutcome blocked = Outcome(json);
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", Steps(), invocation =>
        {
            calls.Add(invocation.CommandName);
            return Task.FromResult(invocation.CommandName == "middle" ? blocked : Outcome("{\"Applied\":true}"));
        });

        CollectionAssert.AreEqual(new[] { "first", "middle" }, calls);
        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.StateMatched);
        Assert.AreEqual("plan", result.PlanId);
        Assert.AreEqual(2, result.Steps.Count);
        Assert.IsTrue(result.Steps[0].Success);
        Assert.IsFalse(result.Steps[1].Success);
        Assert.IsTrue(result.Steps[1].TransportSuccess);
        Assert.AreEqual(errorCode, result.Steps[1].ErrorCode);
        Assert.AreEqual(json, result.Steps[1].Data!.Value.GetRawText());
        Assert.IsTrue(blocked.Success, "Do not mutate the standalone command outcome.");
        Assert.AreEqual("command response", blocked.Message);
    }

    [DataTestMethod]
    [DataRow("{\"SafetyBlocked\":true}")]
    [DataRow("{\"Applied\":false}")]
    [DataRow("{\"Success\":false}")]
    public async Task ExplicitContinuation_RunsSuffixButNeverReportsWholeBlueprintApplied(string json)
    {
        var calls = new List<string>();
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", Steps(continueOnError: true), invocation =>
        {
            calls.Add(invocation.CommandName);
            return Task.FromResult(Outcome(invocation.CommandName == "middle" ? json : "{}"));
        });
        CollectionAssert.AreEqual(new[] { "first", "middle", "last" }, calls);
        Assert.IsFalse(result.Applied);
        Assert.AreEqual(3, result.Steps.Count);
        Assert.IsFalse(result.Steps[1].Success);
        Assert.IsTrue(result.Steps[2].Success);
    }

    [DataTestMethod]
    [DataRow("{\"Applied\":true,\"SafetyBlocked\":false,\"ValidationBlocked\":false,\"DryRun\":false,\"RolledBack\":false,\"Verified\":true,\"StateMatched\":true}")]
    [DataRow("{\"Success\":true}")]
    [DataRow("{\"Applied\":true,\"Preflight\":{\"RolledBack\":true,\"DryRun\":true},\"Unrelated\":{\"Success\":false}}")]
    [DataRow("{\"Created\":true,\"Name\":\"Screen\"}")]
    [DataRow("{\"connected\":false}")]
    [DataRow("[1,2,3]")]
    [DataRow("\"legacy result\"")]
    [DataRow("null")]
    public async Task CompletedAndLegacyOutcomes_RemainSuccessful(string json)
    {
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", Steps(), _ => Task.FromResult(Outcome(json)));
        Assert.IsTrue(result.Applied);
        Assert.AreEqual(3, result.Steps.Count);
        foreach (BlueprintStepOutcome step in result.Steps)
        {
            Assert.IsTrue(step.Success);
            Assert.IsTrue(step.TransportSuccess);
            Assert.IsNull(step.ErrorCode);
        }
    }

    [TestMethod]
    public async Task CommandFailure_PreservesErrorAndDoesNotOverwriteItWithStructuredData()
    {
        var failure = Outcome("{\"Applied\":true}");
        failure.Success = false;
        failure.Error = new ErrorDetail { Code = ErrorCode.SiemensRejected, Message = "vendor rejected" };
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", Steps(), _ => Task.FromResult(failure));
        Assert.IsFalse(result.Applied);
        Assert.AreEqual(1, result.Steps.Count);
        Assert.IsFalse(result.Steps[0].TransportSuccess);
        Assert.AreEqual("SiemensRejected", result.Steps[0].ErrorCode);
        Assert.AreEqual("command response", result.Steps[0].Message);
    }

    [TestMethod]
    public async Task ApplyArguments_AreConfirmedAndNoLongerDryRunWithoutEditingStoredBlueprint()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["dry-run"] = JsonSerializer.SerializeToElement(true),
            ["target"] = JsonSerializer.SerializeToElement("ExactTarget")
        };
        var step = new BlueprintStep { Id = "apply", Command = "mutation", Arguments = arguments };
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", new[] { step }, invocation =>
        {
            Assert.IsTrue(invocation.Arguments["confirm"].GetBoolean());
            Assert.IsFalse(invocation.Arguments["dry-run"].GetBoolean());
            Assert.AreEqual("ExactTarget", invocation.Arguments["target"].GetString());
            return Task.FromResult(Outcome("{}"));
        });
        Assert.IsTrue(result.Applied);
        Assert.IsTrue(arguments["dry-run"].GetBoolean());
        Assert.IsFalse(arguments.ContainsKey("confirm"));
    }

    [TestMethod]
    public async Task LegacyCommandWithoutData_RetainsTransportSemantics()
    {
        var outcome = new CommandOutcome { Success = true, Message = "completed" };
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", Steps(), _ => Task.FromResult(outcome));
        Assert.IsTrue(result.Applied);
        Assert.IsNull(result.Steps[0].Data);
    }

    [TestMethod]
    public async Task EmptySteps_DoNotClaimApplied()
    {
        BlueprintApplyResult result = await BlueprintStepExecutor.ExecuteAsync("plan", Array.Empty<BlueprintStep>(), _ => throw new InvalidOperationException("must not execute"));
        Assert.IsFalse(result.Applied);
        Assert.AreEqual(0, result.Steps.Count);
    }

    [TestMethod]
    public void AddedEvidenceFields_RoundTripAndOldPayloadsStillLoad()
    {
        var old = JsonSerializer.Deserialize<BlueprintStepOutcome>("{\"Id\":\"old\",\"Success\":true}")!;
        Assert.IsTrue(old.Success);
        Assert.IsNull(old.Data);
        var current = new BlueprintStepOutcome { Success = false, TransportSuccess = true, Data = Outcome("{\"SafetyBlocked\":true}").Data };
        var restored = JsonSerializer.Deserialize<BlueprintStepOutcome>(JsonSerializer.Serialize(current))!;
        Assert.IsFalse(restored.Success);
        Assert.IsTrue(restored.TransportSuccess);
        Assert.IsTrue(restored.Data!.Value.GetProperty("SafetyBlocked").GetBoolean());
    }

    private static BlueprintStep[] Steps(bool continueOnError = false) => new[]
    {
        new BlueprintStep { Id = "prefix", Command = "first" },
        new BlueprintStep { Id = "blocked", Command = "middle", ContinueOnError = continueOnError },
        new BlueprintStep { Id = "suffix", Command = "last" }
    };

    private static CommandOutcome Outcome(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new CommandOutcome { Success = true, Message = "command response", Data = document.RootElement.Clone() };
    }
}
