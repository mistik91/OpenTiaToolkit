using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PlcTagServiceTests
    {
        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_PageBelow100_UpdatesAddressAndCommentWithoutRenaming()
        {
            var tag = new FakeTag
            {
                Name = "23F1",
                DataType = "Bool",
                LogicalAddress = "%M0.0"
            };
            tag.Comment.Items[0].Text = "Old comment";
            var service = CreateService(new FakeTagTable
            {
                Name = "Default tags",
                Tags = { tag }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "23F1,%M1.0,Bool,New comment");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual(0, result.Summary.TagsCreated);
                Assert.AreEqual("23F1", tag.Name);
                Assert.AreEqual("%M1.0", tag.LogicalAddress);
                Assert.AreEqual("New comment", tag.Comment.Items[0].Text);
                Assert.AreEqual("FullName", result.Updated.Single().MatchedBy);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_Page100OrAbove_RenamesSingleBaseMatch()
        {
            var tag = new FakeTag
            {
                Name = "100MotorRun",
                DataType = "Bool",
                LogicalAddress = "%M0.0"
            };
            var service = CreateService(new FakeTagTable
            {
                Name = "Page100",
                Tags = { tag }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "101MotorRun,%M2.0,Bool,Renamed by page");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual("101MotorRun", tag.Name);
                Assert.AreEqual("%M2.0", tag.LogicalAddress);
                Assert.AreEqual("Renamed by page", tag.Comment.Items[0].Text);
                Assert.AreEqual("BaseName", result.Updated.Single().MatchedBy);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_MultiplePage100BaseMatches_SkipsAsAmbiguous()
        {
            var first = new FakeTag { Name = "100MotorRun", DataType = "Bool", LogicalAddress = "%M0.0" };
            var second = new FakeTag { Name = "101MotorRun", DataType = "Bool", LogicalAddress = "%M1.0" };
            var service = CreateService(
                new FakeTagTable
                {
                    Name = "TableA",
                    Tags = { first }
                },
                new FakeTagTable
                {
                    Name = "TableB",
                    Tags = { second }
                });
            var csvPath = WriteCsv(
                "TagName,Address,DataType",
                "102MotorRun,%M2.0,Bool");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(1, result.Errors.Count);
                StringAssert.Contains(result.Errors[0].Error, "Multiple existing tags match base name");
                StringAssert.Contains(result.Errors[0].Details, "[TableA] 100MotorRun");
                StringAssert.Contains(result.Errors[0].Details, "[TableB] 101MotorRun");
                Assert.AreEqual(1, result.Summary.TagsSkipped);
                Assert.AreEqual(0, result.Summary.TagsUpdated);
                Assert.AreEqual(0, result.Summary.TagsCreated);
                Assert.AreEqual("100MotorRun", first.Name);
                Assert.AreEqual("101MotorRun", second.Name);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_Page100ExactNameMatch_DisambiguatesDuplicateBaseName()
        {
            var first = new FakeTag { Name = "110K1", DataType = "Bool", LogicalAddress = "%Q0.0" };
            var second = new FakeTag { Name = "114K1", DataType = "Bool", LogicalAddress = "%Q1.0" };
            var service = CreateService(new FakeTagTable
            {
                Name = "OUTPUT",
                Tags = { first, second }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "114K1,%Q2.0,Bool,Exact page match should update only");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual(0, result.Summary.TagsSkipped);
                Assert.AreEqual("110K1", first.Name);
                Assert.AreEqual("%Q0.0", first.LogicalAddress);
                Assert.AreEqual("114K1", second.Name);
                Assert.AreEqual("%Q2.0", second.LogicalAddress);
                Assert.AreEqual("FullName", result.Updated.Single().MatchedBy);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_DuplicateCsvBaseForPage100OrAbove_AllowsSeparateAddresses()
        {
            var tag = new FakeTag { Name = "100MotorRun", DataType = "Bool", LogicalAddress = "%M0.0" };
            var service = CreateService(new FakeTagTable
            {
                Name = "Page100",
                Tags = { tag }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType",
                "101MotorRun,%M1.0,Bool",
                "101MotorRun,%M2.0,Bool");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: true);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(0, result.Summary.TagsUpdated);
                Assert.AreEqual(2, result.Summary.TagsCreated);
                Assert.AreEqual(0, result.Summary.TagsSkipped);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_Page100ExactNameRows_DoNotCollideByBaseName()
        {
            var first = new FakeTag { Name = "110K1", DataType = "Bool", LogicalAddress = "%Q0.0" };
            var second = new FakeTag { Name = "114K1", DataType = "Bool", LogicalAddress = "%Q1.0" };
            var service = CreateService(new FakeTagTable
            {
                Name = "OUTPUT",
                Tags = { first, second }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "110K1,%Q2.0,Bool,Exact update one",
                "114K1,%Q3.0,Bool,Exact update two");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: true);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(2, result.Summary.TagsUpdated);
                Assert.AreEqual(0, result.Summary.TagsSkipped);
                Assert.IsTrue(result.Updated.All(entry => entry.MatchedBy == "FullName"));
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_GermanAddressNotation_UpdatesAsInternationalAddress()
        {
            var tag = new FakeTag { Name = "114K1", DataType = "Bool", LogicalAddress = "%Q1928.4" };
            var service = CreateService(new FakeTagTable
            {
                Name = "OUTPUT",
                Tags = { tag }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "114K1,%A1928.3,Bool,Updated from German notation");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual("%Q1928.3", tag.LogicalAddress);
                Assert.AreEqual("%Q1928.3", result.Updated.Single().NewAddress);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_NonPageDuplicateName_ReplacesUniqueAddressMatchWhenOldNameDisappears()
        {
            var first = new FakeTag { Name = "RESERVE", DataType = "Bool", LogicalAddress = "%I10.0" };
            var second = new FakeTag { Name = "RESERVE(1)", DataType = "Bool", LogicalAddress = "%I10.1" };
            var service = CreateService(new FakeTagTable
            {
                Name = "INPUT",
                Tags = { first, second }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "RESERVE,%E10.1,,Second reserve comment");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(0, result.Warnings.Count);
                Assert.AreEqual(0, result.Summary.TagsUpdated);
                Assert.AreEqual(1, result.Summary.TagsDeleted);
                Assert.AreEqual(1, result.Summary.TagsCreated);
                Assert.AreEqual("RESERVE(1)", result.Deleted.Single().TagName);
                Assert.AreEqual("RESERVE(1)", result.Created.Single().TagName);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_NonPageMissingAddress_CreatesInTableWithSameAddressByte()
        {
            var neighbor = new FakeTag { Name = "SAF_IN_BEF", DataType = "Bool", LogicalAddress = "%I1900.1" };
            var inputTable = new FakeTagTable
            {
                Name = "INPUT",
                Tags = { neighbor }
            };
            var service = CreateService(
                inputTable,
                new FakeTagTable
                {
                    Name = "OUTPUT",
                    Tags = { new FakeTag { Name = "40K1", DataType = "Bool", LogicalAddress = "%Q1900.1" } }
                });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "SIG_BEF,%E1900.5,,Emergency from Previous Machine");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsCreated);
                Assert.AreEqual("INPUT", result.Created.Single().TagTableName);
                Assert.AreEqual("Bool", result.Created.Single().DataType);
                Assert.AreEqual("%I1900.5", result.Created.Single().Address);
                Assert.IsTrue(inputTable.Tags.Any(tag => tag.Name == "SIG_BEF"));
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_BaseMatchNameAppearsElsewhere_CreatesInsteadOfStealingShiftedTag()
        {
            var shiftedTag = new FakeTag { Name = "112K2_NC", DataType = "Bool", LogicalAddress = "%I4.0" };
            var service = CreateService(new FakeTagTable
            {
                Name = "INPUT",
                Tags = { shiftedTag }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "116K2_NC,%E3.4,,New contact",
                "112K2_NC,%E4.0,,Existing contact");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: true);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsCreated);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual("116K2_NC", result.Created.Single().TagName);
                Assert.AreEqual("112K2_NC", result.Updated.Single().OldName);
                Assert.AreEqual("112K2_NC", result.Updated.Single().NewName);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_DuplicateCreatedNames_AutoSuffixesLikeTia()
        {
            var service = CreateService(new FakeTagTable
            {
                Name = "INPUT",
                Tags = { new FakeTag { Name = "RESERVE", DataType = "Bool", LogicalAddress = "%I0.0" } }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "RESERVE,%E1.0,,First new reserve",
                "RESERVE,%E1.1,,Second new reserve");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(2, result.Summary.TagsCreated);
                CollectionAssert.AreEqual(
                    new[] { "RESERVE(1)", "RESERVE(2)" },
                    result.Created.Select(entry => entry.TagName).ToArray());
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_DuplicateAddressExactSurvivor_DeletesObsoleteDuplicate()
        {
            var survivor = new FakeTag { Name = "283FP290", DataType = "Bool", LogicalAddress = "%I9.2" };
            var obsolete = new FakeTag { Name = "283FP195", DataType = "Bool", LogicalAddress = "%I9.2" };
            var service = CreateService(new FakeTagTable
            {
                Name = "INPUT",
                Tags = { survivor, obsolete }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "283FP290,%E9.2,,Hot water pump");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsDeleted);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual("283FP195", result.Deleted.Single().TagName);
                Assert.AreEqual("283FP290", result.Updated.Single().NewName);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        [TestMethod]
        public async Task UpdateTagsFromCsvAsync_AddressOccupiedByShiftedTag_CreatesNewTag()
        {
            var shifted = new FakeTag { Name = "118K1", DataType = "Bool", LogicalAddress = "%Q3.6" };
            var service = CreateService(new FakeTagTable
            {
                Name = "OUTPUT",
                Tags = { shifted }
            });
            var csvPath = WriteCsv(
                "TagName,Address,DataType,Comment",
                "118K1,%A1928.5,,Shifted contact",
                "880K1,%A3.6,,New signal");

            try
            {
                var result = await service.UpdateTagsFromCsvAsync(csvPath, dryRun: false);

                Assert.AreEqual(0, result.Errors.Count);
                Assert.AreEqual(1, result.Summary.TagsUpdated);
                Assert.AreEqual(1, result.Summary.TagsCreated);
                Assert.AreEqual("880K1", result.Created.Single().TagName);
                Assert.AreEqual("%Q1928.5", shifted.LogicalAddress);
            }
            finally
            {
                File.Delete(csvPath);
            }
        }

        private static PlcTagService CreateService(params FakeTagTable[] tagTables)
        {
            var plcSoftware = new FakePlcSoftware
            {
                TagTableGroup = new FakeTagTableGroup()
            };
            plcSoftware.TagTableGroup.TagTables.AddRange(tagTables);

            var context = new EngineeringSessionContext
            {
                PlcSoftware = plcSoftware,
                CurrentPlcName = "PLC_1"
            };

            return new PlcTagService(context, logger: null);
        }

        private static string WriteCsv(params string[] lines)
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
            File.WriteAllLines(csvPath, lines);
            return csvPath;
        }

        private sealed class FakePlcSoftware
        {
            public FakeTagTableGroup? TagTableGroup { get; set; }
        }

        private sealed class FakeTagTableGroup
        {
            public List<FakeTagTable> TagTables { get; } = new List<FakeTagTable>();
        }

        private sealed class FakeTagTable
        {
            public string Name { get; set; } = string.Empty;

            public FakeTagCollection Tags { get; } = new FakeTagCollection();
        }

        private sealed class FakeTagCollection : List<FakeTag>
        {
            public FakeTag Create(string name, string dataType, string logicalAddress)
            {
                if (this.Any(tag => !tag.IsDeleted && string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Tag '{name}' already exists.");
                }

                var tag = new FakeTag
                {
                    Name = name,
                    DataType = dataType,
                    LogicalAddress = logicalAddress
                };

                Add(tag);
                return tag;
            }
        }

        private sealed class FakeTag
        {
            public string Name { get; set; } = string.Empty;

            public string DataType { get; set; } = string.Empty;

            public string LogicalAddress { get; set; } = string.Empty;

            public FakeComment Comment { get; } = new FakeComment();

            public bool IsDeleted { get; private set; }

            public void Delete()
            {
                IsDeleted = true;
            }
        }

        private sealed class FakeComment
        {
            public List<FakeCommentItem> Items { get; } = new List<FakeCommentItem>
            {
                new FakeCommentItem("en-US")
            };
        }

        private sealed class FakeCommentItem
        {
            public FakeCommentItem(string cultureName)
            {
                Culture = new FakeCulture(cultureName);
            }

            public FakeCulture Culture { get; }

            public string Text { get; set; } = string.Empty;
        }

        private sealed class FakeCulture
        {
            public FakeCulture(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }
    }
}
