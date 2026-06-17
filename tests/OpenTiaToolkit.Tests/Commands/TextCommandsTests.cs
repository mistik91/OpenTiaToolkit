using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Text;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class TextCommandsTests
    {
        [TestMethod]
        public async Task ListProjectLanguages_ReturnsLanguageSettings()
        {
            var service = new Mock<ITextService>();
            service.Setup(s => s.GetProjectLanguagesAsync())
                .ReturnsAsync(new ProjectLanguageSettingsInfo
                {
                    Languages = new[]
                    {
                        new ProjectLanguageInfo { CultureName = "en-US", IsActive = true },
                        new ProjectLanguageInfo { CultureName = "de-DE", IsActive = false }
                    },
                    ActiveLanguages = new[]
                    {
                        new ProjectLanguageInfo { CultureName = "en-US", IsActive = true }
                    }
                });
            var command = new ListProjectLanguagesCommand(service.Object);

            var result = await command.ExecuteAsync(CommandArgs.Empty, NullLogger.Instance);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Message, "Found 2 project language(s), 1 active");
        }

        [TestMethod]
        public async Task ExportTexts_RequiresTargetCultures()
        {
            var service = new Mock<ITextService>();
            var command = new ExportTextsCommand(service.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["output-path"] = "texts.xlsx",
                ["source-culture"] = "en-US"
            }), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "target-cultures");
            service.Verify(
                s => s.ExportTextsAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExportTexts_RequiresObjects()
        {
            var service = new Mock<ITextService>();
            var command = new ExportTextsCommand(service.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["output-path"] = "texts.xlsx",
                ["source-culture"] = "en-US",
                ["target-cultures"] = new[] { "de-DE" }
            }), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "objects");
            service.Verify(
                s => s.ExportTextsAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExportTexts_ForwardsOptionsAndReturnsWorkbookArtifact()
        {
            var service = new Mock<ITextService>();
            service.Setup(s => s.ExportTextsAsync(
                    It.IsAny<string>(),
                    "en-US",
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>()))
                .ReturnsAsync((string outputPath, string _, IReadOnlyList<string> targetCultures, IReadOnlyList<string>? categories, IReadOnlyList<string>? objects) =>
                    new ProjectTextExportResult
                    {
                        OutputPath = outputPath,
                        BytesWritten = 42,
                        SourceCulture = "en-US",
                        TargetCultures = targetCultures.ToList(),
                        CategoryIdentifiers = categories?.ToList() ?? new List<string>(),
                        ObjectNames = objects?.ToList() ?? new List<string>()
                    });
            var command = new ExportTextsCommand(service.Object);
            var outputFolder = Path.Combine(Path.GetTempPath(), "OpenTiaToolkitTests");

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["output-path"] = outputFolder,
                ["source-culture"] = "en-US",
                ["target-cultures"] = new[] { "de-DE", "it-IT" },
                ["categories"] = "alarms,screens",
                ["objects"] = new[] { "PLC_1" }
            }), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            var artifact = (CommandArtifactResult)result.Data!;
            Assert.AreEqual("xlsx", artifact.Format);
            Assert.AreEqual("project-texts", artifact.DataKind);
            StringAssert.EndsWith(artifact.OutputPath, "project_texts.xlsx");
            service.Verify(s => s.ExportTextsAsync(
                It.Is<string>(path => path.EndsWith("project_texts.xlsx")),
                "en-US",
                It.Is<IReadOnlyList<string>>(values => values.SequenceEqual(new[] { "de-DE", "it-IT" })),
                It.Is<IReadOnlyList<string>?>(values => values != null && values.SequenceEqual(new[] { "alarms", "screens" })),
                It.Is<IReadOnlyList<string>?>(values => values != null && values.SequenceEqual(new[] { "PLC_1" }))),
                Times.Once);
        }

        [TestMethod]
        public async Task ExportTexts_ContextualExecution_ReportsProgressStages()
        {
            var service = new Mock<ITextService>();
            service.Setup(s => s.ExportTextsAsync(
                    It.IsAny<string>(),
                    "en-US",
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>()))
                .ReturnsAsync((string outputPath, string _, IReadOnlyList<string> targetCultures, IReadOnlyList<string>? categories, IReadOnlyList<string>? objects) =>
                    new ProjectTextExportResult
                    {
                        OutputPath = outputPath,
                        BytesWritten = 42,
                        SourceCulture = "en-US",
                        TargetCultures = targetCultures.ToList(),
                        CategoryIdentifiers = categories?.ToList() ?? new List<string>(),
                        ObjectNames = objects?.ToList() ?? new List<string>()
                    });
            var command = new ExportTextsCommand(service.Object);
            var updates = new List<CommandProgressUpdate>();

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["output-path"] = Path.Combine(Path.GetTempPath(), "OpenTiaToolkitTests"),
                    ["source-culture"] = "en-US",
                    ["target-cultures"] = new[] { "de-DE" },
                    ["objects"] = new[] { "PLC_1" }
                }),
                NullLogger.Instance,
                new CommandExecutionContext(CancellationToken.None, new InlineProgress(update => updates.Add(update))));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(updates.Any(update => update.Stage == "starting"));
            Assert.IsTrue(updates.Any(update => update.Stage == "prepare-artifact"));
            Assert.IsTrue(updates.Any(update => update.Stage == "export-texts"));
            Assert.IsTrue(updates.Any(update => update.Stage == "completed"));
        }

        [TestMethod]
        public async Task ExportTexts_ContextualExecution_HonorsCancellationBeforeServiceCall()
        {
            var service = new Mock<ITextService>();
            var command = new ExportTextsCommand(service.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                await command.ExecuteAsync(
                    new CommandArgs(new Dictionary<string, object?>
                    {
                        ["output-path"] = "texts.xlsx",
                        ["source-culture"] = "en-US",
                        ["target-cultures"] = new[] { "de-DE" },
                        ["objects"] = new[] { "PLC_1" }
                    }),
                    NullLogger.Instance,
                    new CommandExecutionContext(cancellation.Token)));

            service.Verify(
                s => s.ExportTextsAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ImportTexts_DefaultsToDryRun()
        {
            var service = new Mock<ITextService>();
            service.Setup(s => s.ImportTextsAsync("texts.xlsx", false, true))
                .ReturnsAsync(new ProjectTextImportResult
                {
                    InputPath = "texts.xlsx",
                    DryRun = true,
                    UpdateSourceLanguage = false
                });
            var command = new ImportTextsCommand(service.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["input-path"] = "texts.xlsx"
            }), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Message, "validated");
            service.Verify(s => s.ImportTextsAsync("texts.xlsx", false, true), Times.Once);
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
