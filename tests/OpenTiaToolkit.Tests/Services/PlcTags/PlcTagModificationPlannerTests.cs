using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.PlcTags;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Services.PlcTags
{
    [TestClass]
    public sealed class PlcTagModificationPlannerTests
    {
        private readonly PlcTagModificationPlanner _planner = new PlcTagModificationPlanner();

        [TestMethod]
        public void FindSingleTag_UsesTableAndNormalizedAddress()
        {
            var tags = Catalog(
                ("INPUT", "Signal", "%I0.0"),
                ("OUTPUT", "Signal", "%Q0.0"));

            var match = _planner.FindSingleTag(tags, new TagMatchCriteria
            {
                TagTable = "input",
                LogicalAddress = "%E0.0"
            });

            Assert.AreEqual("INPUT", match.Table.Name);
            Assert.AreEqual("Signal", match.Name);
        }

        [TestMethod]
        public void FindSingleTag_ReportsZeroAndAmbiguousMatches()
        {
            var tags = Catalog(
                ("INPUT", "Signal", "%I0.0"),
                ("OUTPUT", "Signal", "%Q0.0"));

            StringAssert.Contains(
                Assert.ThrowsException<InvalidOperationException>(() =>
                    _planner.FindSingleTag(tags, new TagMatchCriteria { Name = "Missing" })).Message,
                "No existing tag");
            StringAssert.Contains(
                Assert.ThrowsException<InvalidOperationException>(() =>
                    _planner.FindSingleTag(tags, new TagMatchCriteria { Name = "Signal" })).Message,
                "ambiguous");
        }

        [TestMethod]
        public void EnsureNoNameCollision_IsCaseInsensitiveAndUsesPlannedState()
        {
            var tags = Catalog(("INPUT", "Signal", "%I0.0"));

            StringAssert.Contains(
                Assert.ThrowsException<InvalidOperationException>(() =>
                    _planner.EnsureNoNameCollision(tags, "input", "SIGNAL")).Message,
                "already exists");

            tags[0].IsDeleted = true;
            _planner.EnsureNoNameCollision(tags, "INPUT", "signal");
        }

        [TestMethod]
        public void ApplyPatch_ReplaceInheritsUnspecifiedValues()
        {
            var source = new TagSnapshot
            {
                TagTable = "INPUT",
                Name = "Old",
                DataType = "Bool",
                LogicalAddress = "%I0.0",
                Comment = "Original"
            };

            var result = _planner.ApplyPatch(source, new TagValuePatch
            {
                HasName = true,
                Name = "New"
            });

            Assert.AreEqual("New", result.Name);
            Assert.AreEqual("Bool", result.DataType);
            Assert.AreEqual("%I0.0", result.LogicalAddress);
            Assert.AreEqual("Original", result.Comment);
        }

        private static List<PlcTagState> Catalog(params (string Table, string Name, string Address)[] values)
        {
            var tables = new Dictionary<string, PlcTagTableState>(StringComparer.OrdinalIgnoreCase);
            var tags = new List<PlcTagState>();
            foreach (var value in values)
            {
                if (!tables.TryGetValue(value.Table, out var table))
                {
                    table = new PlcTagTableState { Name = value.Table, TableObject = new object() };
                    tables.Add(value.Table, table);
                }

                tags.Add(new PlcTagState
                {
                    Table = table,
                    Name = value.Name,
                    DataType = "Bool",
                    LogicalAddress = value.Address
                });
            }

            return tags;
        }
    }
}
