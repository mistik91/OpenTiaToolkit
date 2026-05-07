using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class AuditHmiLabelProximityCommandTests
    {
        [TestMethod]
        public async Task Execute_ReportsNearestLabelAndDuplicateUse()
        {
            var inputPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateDocument()));
                var command = new AuditHmiLabelProximityCommand();

                var result = await command.ExecuteAsync(
                    new CommandArgs(new Dictionary<string, object?>
                    {
                        ["input-path"] = inputPath,
                        ["screen-path"] = "Main",
                        ["label-texts"] = new[] { "V116" }
                    }),
                    NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var report = result.Data as HmiLabelProximityAuditReport;
                Assert.IsNotNull(report);
                Assert.AreEqual(2, report!.MappingCount);
                Assert.AreEqual(2, report.DuplicateLabelMatchCount);
                Assert.AreEqual("Label_1", report.Mappings[0].MatchedLabelItemName);
            }
            finally
            {
                File.Delete(inputPath);
            }
        }

        [TestMethod]
        public async Task Execute_WritesReportWhenOutputPathIsProvided()
        {
            var inputPath = Path.GetTempFileName();
            var outputPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateDocument()));
                var command = new AuditHmiLabelProximityCommand();

                var result = await command.ExecuteAsync(
                    new CommandArgs(new Dictionary<string, object?>
                    {
                        ["input-path"] = inputPath,
                        ["output-path"] = outputPath
                    }),
                    NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                Assert.IsTrue(File.ReadAllText(outputPath).Contains("Mappings"));
            }
            finally
            {
                File.Delete(inputPath);
                File.Delete(outputPath);
            }
        }

        private static HmiJsonDocument CreateDocument()
        {
            return new HmiJsonDocument
            {
                Screens =
                {
                    new HmiJsonScreen
                    {
                        Path = "Main",
                        Name = "Main",
                        Items =
                        {
                            CreateItem("Valve_1", "HmiFaceplateContainer", 100, 100, 50, 50),
                            CreateItem("Valve_2", "HmiFaceplateContainer", 120, 100, 50, 50),
                            CreateText("Label_1", "V116", 90, 80, 40, 20),
                            CreateDynamicText("Label_2", "Valve_V117_GEN.AV", 300, 300, 40, 20)
                        }
                    }
                }
            };
        }

        private static HmiJsonItem CreateItem(string name, string type, int left, int top, int width, int height)
        {
            return new HmiJsonItem
            {
                Name = name,
                Type = type,
                Properties =
                {
                    ["Left"] = left.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Top"] = top.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }

        private static HmiJsonItem CreateText(string name, string text, int left, int top, int width, int height)
        {
            var item = CreateItem(name, "HmiText", left, top, width, height);
            item.Properties["Text"] = text;
            return item;
        }

        private static HmiJsonItem CreateDynamicText(string name, string tag, int left, int top, int width, int height)
        {
            var item = CreateItem(name, "HmiText", left, top, width, height);
            item.Dynamizations = new List<HmiJsonDynamization>
            {
                new HmiJsonDynamization
                {
                    Property = "ForeColor",
                    Type = "Tag",
                    Binding = new HmiJsonDynamizationBinding { Tag = tag }
                }
            };
            return item;
        }
    }
}
