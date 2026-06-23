using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.IO;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class TagModificationInputParserTests
    {
        [TestMethod]
        public void ParseJson_PreservesExplicitCommentClear()
        {
            var operation = TagModificationInputParser.ParseJson(
                "[{\"action\":\"update\",\"match\":{\"name\":\"Signal\"},\"set\":{\"comment\":null}}]")
                .Single();

            Assert.AreEqual("update", operation.Action);
            Assert.AreEqual("Signal", operation.Match.Name);
            Assert.IsTrue(operation.Set.HasComment);
            Assert.AreEqual(string.Empty, operation.Set.Comment);
        }

        [TestMethod]
        public void ParseCsv_ParsesReplaceAndNormalizesAddresses()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
            File.WriteAllLines(path, new[]
            {
                "Action,MatchTagTable,MatchName,MatchLogicalAddress,TagTable,Name,DataType,LogicalAddress,Comment",
                "replace,INPUT,Old,%E1.0,INPUT,New,Bool,%A2.0,Replacement"
            });

            try
            {
                var operation = TagModificationInputParser.ParseFile(path).Single();

                Assert.AreEqual("replace", operation.Action);
                Assert.AreEqual("%I1.0", operation.Match.LogicalAddress);
                Assert.AreEqual("%Q2.0", operation.Set.LogicalAddress);
                Assert.IsTrue(operation.Set.HasLogicalAddress);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
