using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class ModifyTagsCommandTests
    {
        [TestMethod]
        public async Task ExecuteAsync_UsesInlineJsonOperations()
        {
            var service = new Mock<IPlcService>();
            service.SetupGet(item => item.IsSoftwareOpen).Returns(true);
            service.SetupGet(item => item.CurrentPlcName).Returns("PLC_1");
            service
                .Setup(item => item.ModifyTagsAsync(
                    It.IsAny<IReadOnlyList<TagModificationOperation>>(),
                    true,
                    It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<BatchOperationProgress>>()))
                .ReturnsAsync(new TagModificationResult
                {
                    DryRun = true,
                    Summary = new TagModificationSummary { Total = 1, Planned = 1 }
                });

            using var document = JsonDocument.Parse(
                "[{\"action\":\"create\",\"set\":{\"tagTable\":\"INPUT\",\"name\":\"Signal\",\"dataType\":\"Bool\"}}]");
            var command = new ModifyTagsCommand(service.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["plc-name"] = "PLC_1",
                    ["operations"] = document.RootElement.Clone()
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            service.Verify(
                item => item.ModifyTagsAsync(
                    It.Is<IReadOnlyList<TagModificationOperation>>(operations =>
                        operations.Count == 1 &&
                        operations[0].Action == "create" &&
                        operations[0].Set.Name == "Signal"),
                    true,
                    It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<BatchOperationProgress>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_RequiresConfirmationForAppliedReplace()
        {
            var service = new Mock<IPlcService>();
            using var document = JsonDocument.Parse(
                "[{\"action\":\"replace\",\"match\":{\"tagTable\":\"INPUT\",\"name\":\"Old\"},\"set\":{\"name\":\"New\"}}]");
            var command = new ModifyTagsCommand(service.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["plc-name"] = "PLC_1",
                    ["operations"] = document.RootElement.Clone(),
                    ["dry-run"] = false
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "confirm=true");
            service.Verify(
                item => item.ModifyTagsAsync(
                    It.IsAny<IReadOnlyList<TagModificationOperation>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<BatchOperationProgress>>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsFailureWhenAnyOperationFails()
        {
            var service = new Mock<IPlcService>();
            service.SetupGet(item => item.IsSoftwareOpen).Returns(true);
            service.SetupGet(item => item.CurrentPlcName).Returns("PLC_1");
            service
                .Setup(item => item.ModifyTagsAsync(
                    It.IsAny<IReadOnlyList<TagModificationOperation>>(),
                    true,
                    It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<BatchOperationProgress>>()))
                .ReturnsAsync(new TagModificationResult
                {
                    DryRun = true,
                    Summary = new TagModificationSummary { Total = 1, Failed = 1 }
                });

            using var document = JsonDocument.Parse(
                "[{\"action\":\"delete\",\"match\":{\"name\":\"Missing\"}}]");
            var command = new ModifyTagsCommand(service.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["plc-name"] = "PLC_1",
                    ["operations"] = document.RootElement.Clone()
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "failed: 1");
        }

        [TestMethod]
        public async Task ExecuteAsync_ReportsServiceProgressAndPropagatesCancellation()
        {
            var service = new Mock<IPlcService>();
            service.SetupGet(item => item.IsSoftwareOpen).Returns(true);
            service.SetupGet(item => item.CurrentPlcName).Returns("PLC_1");
            service
                .Setup(item => item.ModifyTagsAsync(
                    It.IsAny<IReadOnlyList<TagModificationOperation>>(),
                    true,
                    It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<BatchOperationProgress>>()))
                .Callback<IReadOnlyList<TagModificationOperation>, bool, CancellationToken, IProgress<BatchOperationProgress>>(
                    (_, _, cancellationToken, progress) =>
                    {
                        Assert.IsFalse(cancellationToken.IsCancellationRequested);
                        progress.Report(new BatchOperationProgress("Processed operation.", 1, 2, "apply-tag-modifications"));
                    })
                .ThrowsAsync(new OperationCanceledException());

            var updates = new List<CommandProgressUpdate>();
            using var document = JsonDocument.Parse(
                "[{\"action\":\"create\",\"set\":{\"tagTable\":\"INPUT\",\"name\":\"Signal\",\"dataType\":\"Bool\"}}]");
            var command = new ModifyTagsCommand(service.Object);

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                await command.ExecuteAsync(
                    new CommandArgs(new Dictionary<string, object?>
                    {
                        ["plc-name"] = "PLC_1",
                        ["operations"] = document.RootElement.Clone()
                    }),
                    NullLogger.Instance,
                    new CommandExecutionContext(CancellationToken.None, new InlineProgress(update => updates.Add(update)))));

            Assert.IsTrue(updates.Exists(update => update.Message == "Processed operation."));
            Assert.IsTrue(updates.Exists(update => update.Percentage == 70));
        }

        private sealed class InlineProgress : IProgress<CommandProgressUpdate>
        {
            private readonly Action<CommandProgressUpdate> _report;

            public InlineProgress(Action<CommandProgressUpdate> report)
            {
                _report = report;
            }

            public void Report(CommandProgressUpdate value)
            {
                _report(value);
            }
        }
    }
}
