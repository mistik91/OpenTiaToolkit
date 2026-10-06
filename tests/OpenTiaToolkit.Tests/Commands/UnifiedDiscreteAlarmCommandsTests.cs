using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public sealed class UnifiedDiscreteAlarmCommandsTests
{
    [TestMethod]
    public void Commands_UseRequiredKebabCaseSchemasAndSafeDefaults()
    {
        var service = new Mock<IHmiService>();
        var list = new ListUnifiedDiscreteAlarmsCommand(service.Object);
        var get = new GetUnifiedDiscreteAlarmCommand(service.Object);
        var apply = new ApplyUnifiedDiscreteAlarmsCommand(service.Object);
        var delete = new DeleteUnifiedDiscreteAlarmCommand(service.Object);

        CollectionAssert.AreEqual(new[] { "list-unified-discrete-alarms", "get-unified-discrete-alarm", "apply-unified-discrete-alarms", "delete-unified-discrete-alarm" }, new[] { list.Name, get.Name, apply.Name, delete.Name });
        Assert.IsTrue(list.Schema.Parameters.Single(x => x.Name == "hmi-name").Required);
        Assert.IsTrue(get.Schema.Parameters.Single(x => x.Name == "alarm-name").Required);
        Assert.AreEqual(true, apply.Schema.Parameters.Single(x => x.Name == "dry-run").DefaultValue);
        Assert.AreEqual(false, apply.Schema.Parameters.Single(x => x.Name == "apply").DefaultValue);
        Assert.AreEqual(true, apply.Schema.Parameters.Single(x => x.Name == "fail-on-warning").DefaultValue);
        Assert.IsTrue(delete.Schema.Parameters.Single(x => x.Name == "expected-hash").Required);
        Assert.AreEqual(true, delete.Schema.Parameters.Single(x => x.Name == "dry-run").DefaultValue);
    }

    [TestMethod]
    public async Task ApplyWithoutAllFlags_IsDeniedBeforeOpeningOrMutation()
    {
        var service = new Mock<IHmiService>();
        var result = await new ApplyUnifiedDiscreteAlarmsCommand(service.Object).ExecuteAsync(Args(
            ("hmi-name", "HMI_1"), ("input-path", "alarms.json"), ("dry-run", false), ("apply", true)), NullLogger.Instance);
        Assert.IsFalse(result.Success);
        service.Verify(x => x.OpenUnifiedHmiAsync(It.IsAny<string>()), Times.Never);
        service.Verify(x => x.ApplyDiscreteAlarmsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteWithoutConfirmation_IsDeniedBeforeServiceMutation()
    {
        var service = OpenService();
        var result = await new DeleteUnifiedDiscreteAlarmCommand(service.Object).ExecuteAsync(Args(
            ("hmi-name", "HMI_1"), ("alarm-name", "Alarm"), ("expected-hash", "abc"), ("dry-run", false)), NullLogger.Instance);
        Assert.IsFalse(result.Success);
        service.Verify(x => x.DeleteDiscreteAlarmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task ApplyDefaultsToDryRunAndPreservesStructuredBlockedResult()
    {
        var service = OpenService();
        service.Setup(x => x.ApplyDiscreteAlarmsAsync("alarms.json", true, true)).ReturnsAsync(new HmiDiscreteAlarmApplyResult { DryRun = true, SafetyBlocked = true, ErrorCode = "preflight-blocked", Message = "blocked" });
        var result = await new ApplyUnifiedDiscreteAlarmsCommand(service.Object).ExecuteAsync(Args(("hmi-name", "HMI_1"), ("input-path", "alarms.json")), NullLogger.Instance);
        Assert.IsFalse(result.Success);
        Assert.IsInstanceOfType<HmiDiscreteAlarmApplyResult>(result.Data);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task ListAndGetSerializeCanonicalResults()
    {
        var service = OpenService();
        var snapshot = new HmiDiscreteAlarmSnapshot { Name = "Alarm", ContentHash = "hash" };
        service.Setup(x => x.ListDiscreteAlarmsAsync(false)).ReturnsAsync(new[] { snapshot });
        service.Setup(x => x.GetDiscreteAlarmAsync("Alarm", true)).ReturnsAsync(snapshot);
        var listed = await new ListUnifiedDiscreteAlarmsCommand(service.Object).ExecuteAsync(Args(("hmi-name", "HMI_1")), NullLogger.Instance);
        var got = await new GetUnifiedDiscreteAlarmCommand(service.Object).ExecuteAsync(Args(("hmi-name", "HMI_1"), ("alarm-name", "Alarm")), NullLogger.Instance);
        StringAssert.Contains(listed.ToJson(), "contentHash");
        StringAssert.Contains(got.ToJson(), "Alarm");
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Get_MapsUnifiedLicenseFailureToStableCode()
    {
        var service = OpenService();
        service.Setup(x => x.GetDiscreteAlarmAsync("Alarm", true)).ThrowsAsync(new LicenseNotFoundException());
        var result = await new GetUnifiedDiscreteAlarmCommand(service.Object).ExecuteAsync(Args(("hmi-name", "HMI_1"), ("alarm-name", "Alarm")), NullLogger.Instance);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ToJson(), "unified-license-required");
    }

    private static Mock<IHmiService> OpenService()
    {
        var service = new Mock<IHmiService>();
        service.SetupGet(x => x.IsHmiOpen).Returns(true);
        service.SetupGet(x => x.CurrentHmiName).Returns("HMI_1");
        return service;
    }

    private static CommandArgs Args(params (string Name, object Value)[] entries) => new(entries.ToDictionary(x => x.Name, x => (object?)x.Value));

    private sealed class LicenseNotFoundException : System.Exception { }
}
