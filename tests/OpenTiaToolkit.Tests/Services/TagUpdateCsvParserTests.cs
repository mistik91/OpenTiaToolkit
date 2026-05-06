using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class TagUpdateCsvParserTests
    {
        [TestMethod]
        public void Parse_AllowsMissingCommentColumn()
        {
            var csvPath = WriteCsv(
                "TagName,Address,DataType",
                "23F1,%M0.0,Bool");

            try
            {
                var rows = TagUpdateCsvParser.Parse(csvPath);

                Assert.AreEqual(1, rows.Count);
                Assert.AreEqual("23F1", rows[0].TagName);
                Assert.AreEqual("%M0.0", rows[0].Address);
                Assert.AreEqual("Bool", rows[0].DataType);
                Assert.IsNull(rows[0].Comment);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public void Parse_DetectsSemicolonDelimiterAndUnescapesQuotes()
        {
            var csvPath = WriteCsv(
                "tagname;address;datatype;comment",
                "101Motor;%M1.0;Bool;\"Motor says \"\"ready\"\"\"");

            try
            {
                var row = TagUpdateCsvParser.Parse(csvPath).Single();

                Assert.AreEqual("101Motor", row.TagName);
                Assert.AreEqual("%M1.0", row.Address);
                Assert.AreEqual("Bool", row.DataType);
                Assert.AreEqual("Motor says \"ready\"", row.Comment);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public void Parse_NormalizesGermanInputOutputAddressPrefixes()
        {
            var csvPath = WriteCsv(
                "TagName,Address,DataType",
                "114K1,%A1928.3,Bool",
                "178PT240,%EW306,Word");

            try
            {
                var rows = TagUpdateCsvParser.Parse(csvPath);

                Assert.AreEqual("%Q1928.3", rows[0].Address);
                Assert.AreEqual("%IW306", rows[1].Address);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public void Parse_RequiresCoreColumns()
        {
            var csvPath = WriteCsv(
                "TagName,Address,Comment",
                "23F1,%M0.0,Ready");

            try
            {
                var exception = Assert.ThrowsException<ArgumentException>(() => TagUpdateCsvParser.Parse(csvPath));

                StringAssert.Contains(exception.Message, "Missing columns: DataType");
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public void Parse_FallsBackToWindowsDefaultEncoding()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
            File.WriteAllText(
                csvPath,
                "TagName,Address,DataType,Comment\r\n40K1_NC,%E1.4,,Safety Relé Not Restored\r\n",
                Encoding.Default);

            try
            {
                var row = TagUpdateCsvParser.Parse(csvPath).Single();

                Assert.AreEqual("Safety Relé Not Restored", row.Comment);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        private static string WriteCsv(params string[] lines)
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
            File.WriteAllLines(csvPath, lines);
            return csvPath;
        }
    }
}
