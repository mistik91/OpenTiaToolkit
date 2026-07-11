using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class PlcImportCommandsTests
    {
        private Mock<IPlcService> _plcService = null!;
        private Mock<ILogger> _logger = null!;

        [TestInitialize]
        public void Setup()
        {
            _plcService = new Mock<IPlcService>();
            _logger = new Mock<ILogger>();
            _plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            _plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
        }

        [TestMethod]
        public async Task ImportBlocks_PartialResultFailsAndPreservesTypedData()
        {
            _plcService
                .Setup(service => service.ImportBlocksAsync(@"C:\Import", "FB_.*", "Program blocks"))
                .ReturnsAsync(CreateImportResult());

            var command = new ImportBlocksCommand(_plcService.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["import-directory"] = @"C:\Import",
                ["filter-regex"] = "FB_.*",
                ["group-path"] = "Program blocks"
            }), _logger.Object);

            Assert.IsFalse(result.Success);
            var payload = result.Data as PlcImportCommandResult;
            Assert.IsNotNull(payload);
            Assert.AreEqual("PLC_1", payload!.PlcName);
            Assert.AreEqual(@"C:\Import", payload.ImportDirectory);
            Assert.AreEqual("FB_.*", payload.FilterRegex);
            Assert.AreEqual("Program blocks", payload.GroupPath);
            Assert.AreEqual(2, payload.TotalCount);
            CollectionAssert.AreEqual(new[] { "FB_One" }, payload.ImportedItems);
        }

        [TestMethod]
        public async Task ImportTypes_PartialResultFailsAndPreservesTypedData()
        {
            _plcService
                .Setup(service => service.ImportTypesAsync(@"C:\Types", "UDT_.*", "Types"))
                .ReturnsAsync(CreateImportResult());

            var command = new ImportTypesCommand(_plcService.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["import-directory"] = @"C:\Types",
                ["filter-regex"] = "UDT_.*",
                ["group-path"] = "Types"
            }), _logger.Object);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(PlcImportCommandResult));
        }

        [TestMethod]
        public async Task ImportBlocksFromDocuments_PartialResultFailsAndPreservesTypedData()
        {
            _plcService
                .Setup(service => service.ImportBlocksFromDocumentsAsync(@"C:\Docs", string.Empty, string.Empty, "SkipInactiveCultures"))
                .ReturnsAsync(CreateImportResult());

            var command = new ImportBlocksFromDocumentsCommand(_plcService.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["import-directory"] = @"C:\Docs",
                ["import-option"] = "SkipInactiveCultures"
            }), _logger.Object);

            Assert.IsFalse(result.Success);
            var payload = result.Data as PlcImportCommandResult;
            Assert.IsNotNull(payload);
            Assert.AreEqual("SkipInactiveCultures", payload!.ImportOption);
        }

        private static ImportResult CreateImportResult()
        {
            return new ImportResult
            {
                TotalCount = 2,
                SuccessCount = 1,
                FailedCount = 1,
                ImportedItems = new List<string> { "FB_One" },
                FailedItems = new List<string> { "DB_Bad" },
                Errors = new List<string> { "Failed to import: DB_Bad" }
            };
        }
    }
}
