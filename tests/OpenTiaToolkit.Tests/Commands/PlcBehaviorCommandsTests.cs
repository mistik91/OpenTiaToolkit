using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public sealed class PlcBehaviorCommandsTests
{
    [TestMethod]
    public async Task DiscoverSimulatorCapabilities_ForwardsExactSelectionAndRefresh()
    {
        var runtime = new Mock<IPlcSimulationRuntime>(MockBehavior.Strict);
        var discovery = new PlcSimulatorCapabilityDiscovery { Available = true, RequestedPlcName = "PLC_1", RequestedInstanceName = "Sim_1" };
        runtime.Setup(item => item.Discover("PLC_1", "Sim_1", true)).Returns(discovery);

        CommandResult result = await new DiscoverPlcSimulatorCapabilitiesCommand(runtime.Object).ExecuteAsync(
            Args(("plc-name", "PLC_1"), ("instance-name", "Sim_1"), ("refresh-tags", true)), NullLogger.Instance);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreSame(discovery, result.Data);
        runtime.VerifyAll();
    }

    [TestMethod]
    public async Task RunBehaviorProbe_RequiresExplicitConfirmationBeforeServiceUse()
    {
        var command = new RunPlcCodeBehaviorProbeCommand(null!);

        CommandResult result = await command.ExecuteAsync(Args(("confirm", false)), NullLogger.Instance);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "confirm=true");
    }

    [TestMethod]
    public async Task RecoverBehaviorProbe_RequiresExplicitConfirmationBeforeServiceUse()
    {
        var command = new RecoverPlcCodeBehaviorProbeCommand(null!);

        CommandResult result = await command.ExecuteAsync(Args(("confirm", false)), NullLogger.Instance);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "confirm=true");
    }

    [TestMethod]
    public void BehaviorExecutionSchema_DefaultsToOnlyChangesAndRequiresConfirmation()
    {
        CommandSchema schema = QueryPlcBehaviorEvidenceCommand.Common(includeExecution: true);

        Assert.IsTrue(schema.Parameters.Single(item => item.Name == "confirm").Required);
        Assert.AreEqual(true, schema.Parameters.Single(item => item.Name == "only-changes").DefaultValue);
    }

    private static CommandArgs Args(params (string Name, object? Value)[] values)
    {
        var arguments = new Dictionary<string, object?>();
        foreach ((string name, object? value) in values) arguments[name] = value;
        return new CommandArgs(arguments);
    }
}
