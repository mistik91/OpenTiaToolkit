using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Insights;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public class EngineeringInsightCommandsTests
{
    [TestMethod]
    public async Task ListUnifiedAlarms_BoundsInlineResultAndReportsTotal()
    {
        var service = new Mock<IEngineeringInsightsService>();
        service.Setup(item => item.ListUnifiedAlarmsAsync()).ReturnsAsync(new[]
        {
            new EngineeringInsightItem { Name = "A" },
            new EngineeringInsightItem { Name = "B" },
            new EngineeringInsightItem { Name = "C" }
        });
        var command = new ListUnifiedAlarmsCommand(service.Object);
        CommandResult result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
        {
            ["max-results"] = JsonSerializer.SerializeToElement(2)
        }), NullLogger.Instance);
        Assert.IsTrue(result.Success);
        string json = JsonSerializer.Serialize(result.Data);
        StringAssert.Contains(json, "\"totalCount\":3");
        StringAssert.Contains(json, "\"truncated\":true");
    }

    [TestMethod]
    public async Task ValidateSafety_ReturnsStructuredUnavailableResultWithoutMutation()
    {
        var service = new Mock<IEngineeringInsightsService>();
        service.Setup(item => item.ValidateSafetyAsync()).ReturnsAsync(new EngineeringValidationResult
        {
            Domain = "Safety",
            Available = false,
            Valid = false,
            Warnings = new[] { "No F-PLC" }
        });
        CommandResult result = await new ValidateSafetyConfigurationCommand(service.Object)
            .ExecuteAsync(CommandArgs.Empty, NullLogger.Instance);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(JsonSerializer.Serialize(result.Data), "No F-PLC");
    }
}
