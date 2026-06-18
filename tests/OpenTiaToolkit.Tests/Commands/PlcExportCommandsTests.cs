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
    public class PlcExportCommandsTests
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
        public async Task ExportBlocksAsDocuments_ReturnsTypedCommandResult()
        {
            _plcService
                .Setup(service => service.ExportBlocksAsDocumentsAsync("FB_.*", @"C:\Export"))
                .ReturnsAsync(CreateExportResult());

            var command = new ExportBlocksAsDocumentsCommand(_plcService.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["export-path"] = @"C:\Export",
                ["filter-regex"] = "FB_.*"
            }), _logger.Object);

            Assert.IsTrue(result.Success);
            var payload = result.Data as PlcExportCommandResult;
            Assert.IsNotNull(payload);
            Assert.AreEqual(2, payload!.Summary.Total);
            Assert.AreEqual(1, payload.Summary.Succeeded);
            Assert.AreEqual(1, payload.Summary.Failed);
            CollectionAssert.AreEqual(new[] { "FB_One" }, payload.Exported);
            CollectionAssert.AreEqual(new[] { "DB_Bad" }, payload.Failed);
        }

        [TestMethod]
        public async Task ExportTypes_ReturnsTypedCommandResult()
        {
            _plcService
                .Setup(service => service.ExportTypesAsync("UDT_.*", @"C:\Types"))
                .ReturnsAsync(CreateExportResult());

            var command = new ExportTypesCommand(_plcService.Object);

            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["export-path"] = @"C:\Types",
                ["filter-regex"] = "UDT_.*"
            }), _logger.Object);

            Assert.IsTrue(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(PlcExportCommandResult));
        }

        private static ExportResult CreateExportResult()
        {
            return new ExportResult
            {
                TotalCount = 2,
                SuccessCount = 1,
                FailedCount = 1,
                ExportedItems = new List<string> { "FB_One" },
                FailedItems = new List<string> { "DB_Bad" },
                Errors = new List<string> { "Failed to export: DB_Bad" }
            };
        }
    }
}
