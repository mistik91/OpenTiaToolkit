using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;

namespace OpenTiaToolkit.ArchitectureTests;

[TestClass]
public class MutationPolicyTests
{
    [TestMethod]
    public void MutatingCommand_IsDeniedWithoutExplicitIntent()
    {
        var policy = new MutationPolicy();
        PolicyDecision decision = policy.Evaluate(
            new CommandDescriptor { Name = "write", Risk = CommandRisk.Mutating },
            new CommandInvocation { CommandName = "write" });
        Assert.IsFalse(decision.Allowed);
    }

    [TestMethod]
    public void Preview_IsAllowedWithoutApplyConfirmation()
    {
        using JsonDocument document = JsonDocument.Parse("true");
        var policy = new MutationPolicy();
        PolicyDecision decision = policy.Evaluate(
            new CommandDescriptor { Name = "write", Risk = CommandRisk.Mutating, SupportsPreview = true },
            new CommandInvocation
            {
                CommandName = "write",
                Arguments = new Dictionary<string, JsonElement> { ["dry-run"] = document.RootElement.Clone() }
            });
        Assert.IsTrue(decision.Allowed);
        Assert.IsTrue(decision.Preview);
    }
}
