using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Multiuser;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public class MultiuserCommandsTests
{
    [TestMethod]
    public async Task CommitSurfacesUnsupportedLocalSessionCheckIn()
    {
        var service = new Mock<IMultiuserService>();
        service.Setup(x => x.CommitSessionAsync("agent change", true, true))
            .ThrowsAsync(new NotSupportedException("Use TIA Portal for local-session check-in."));
        var command = new CommitMultiuserSessionCommand(service.Object);
        var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?> { ["comment"] = "agent change" }), NullLogger.Instance);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "TIA Portal");
        service.Verify(x => x.CommitSessionAsync("agent change", true, true), Times.Once);
    }

    [TestMethod]
    public async Task StatusDelegatesToMultiuserService()
    {
        var service = new Mock<IMultiuserService>();
        service.Setup(x => x.GetStatusAsync()).ReturnsAsync(new MultiuserSessionStatus { IsMultiuser = true, IsOpen = true, IsUpToDate = true });
        var result = await new GetMultiuserSessionStatusCommand(service.Object).ExecuteAsync(new CommandArgs(new Dictionary<string, object?>()), NullLogger.Instance);
        Assert.IsTrue(result.Success);
        service.Verify(x => x.GetStatusAsync(), Times.Once);
    }
}
