using OpenTiaToolkit.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class V21OpennessAdapterTests
    {
        [TestMethod]
        public void GetProgrammingLanguageValue_ResolvesMappedEnumName()
        {
            // Arrange
            var adapter = new V21OpennessAdapter();

            // Act
            var result = adapter.GetProgrammingLanguageValue("ProDiag");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("ProDiag", result!.ToString());
        }

        [TestMethod]
        public void TryExportEngineeringObject_UsesFileInfoAndOptionOverloadWhenAvailable()
        {
            // Arrange
            var exportable = new FakeExportableObject();
            var adapter = new V21OpennessAdapter();
            var exportPath = Path.Combine(Path.GetTempPath(), "adapter-test.xml");

            // Act
            var success = adapter.TryExportEngineeringObject(exportable, exportPath, "WithDefaults");

            // Assert
            Assert.IsTrue(success);
            Assert.AreEqual(exportPath, exportable.LastExportPath);
            Assert.AreEqual(ExportOptions.WithDefaults, exportable.LastExportOption);
        }

        [TestMethod]
        public void TryImport_UsesEnumImportOverloadWhenAvailable()
        {
            // Arrange
            var collection = new FakeImportableCollection();
            var adapter = new V21OpennessAdapter();
            var importFile = new FileInfo(Path.Combine(Path.GetTempPath(), "adapter-import.xml"));

            // Act
            var success = adapter.TryImport(collection, importFile, 1, out var result);

            // Assert
            Assert.IsTrue(success);
            Assert.AreEqual(importFile.FullName, collection.LastImportPath);
            Assert.AreEqual(FakeImportOptions.Override, collection.LastImportOption);
            Assert.AreSame(collection.ImportResult, result);
        }

        [TestMethod]
        public void TryImportFromDocuments_UsesEnumImportDocumentOverloadWhenAvailable()
        {
            // Arrange
            var collection = new FakeDocumentImportableCollection();
            var adapter = new V21OpennessAdapter();
            var importDirectory = new DirectoryInfo(Path.GetTempPath());

            // Act
            var success = adapter.TryImportFromDocuments(collection, importDirectory, "Block_1", 1, out var result);

            // Assert
            Assert.IsTrue(success);
            Assert.AreEqual(importDirectory.FullName, collection.LastDirectoryPath);
            Assert.AreEqual("Block_1", collection.LastBaseName);
            Assert.AreEqual(FakeImportDocumentOptions.Override, collection.LastImportOption);
            Assert.AreSame(collection.ImportResult, result);
        }

        [TestMethod]
        public void GetTagTables_AndFindTagTable_ReturnExpectedTables()
        {
            // Arrange
            var adapter = new V21OpennessAdapter();
            var plcSoftware = new FakePlcSoftware
            {
                TagTableGroup = new FakeTagTableGroup
                {
                    TagTables =
                    {
                        new FakeTagTable { Name = "Default tags" },
                        new FakeTagTable { Name = "Page100" }
                    }
                }
            };

            // Act
            var tagTables = adapter.GetTagTables(plcSoftware);
            var page100 = adapter.FindTagTable(plcSoftware, "page100");

            // Assert
            Assert.IsNotNull(tagTables);
            Assert.AreEqual(2, tagTables!.Count);
            Assert.IsNotNull(page100);
            Assert.AreEqual("Page100", adapter.GetName(page100!));
        }

        [TestMethod]
        public void TryCreateTag_AndCommentHelpers_WorkWithCultureSpecificEntries()
        {
            // Arrange
            var adapter = new V21OpennessAdapter();
            var tagTable = new FakeTagTable { Name = "Default tags" };

            // Act
            var created = adapter.TryCreateTag(tagTable, "MotorReady", "Bool", "%M0.0", out var createdTag, out var failureReason);
            var createdFakeTag = (FakeTag)createdTag!;
            var setComment = adapter.TrySetTagComment(createdFakeTag, "Motor ready", "en-US");
            var commentText = adapter.GetTagCommentText(createdFakeTag, "en-US");

            // Assert
            Assert.IsTrue(created, failureReason);
            Assert.IsNotNull(createdTag);
            Assert.AreEqual("MotorReady", adapter.GetName(createdTag!));
            Assert.AreEqual("Bool", adapter.GetDataType(createdTag!));
            Assert.AreEqual("%M0.0", adapter.GetLogicalAddress(createdTag!));
            Assert.IsTrue(setComment);
            Assert.AreEqual("Motor ready", commentText);
            Assert.AreEqual(1, tagTable.Tags.Count);
        }

        [TestMethod]
        public void FindGlobalDbs_AndGetNestedMembers_ReturnRecursiveMembers()
        {
            // Arrange
            var adapter = new V21OpennessAdapter();
            var plcSoftware = new FakePlcSoftware
            {
                BlockGroup = new FakeBlockGroup
                {
                    Groups =
                    {
                        new FakeBlockGroup
                        {
                            Blocks =
                            {
                                new GlobalDB
                                {
                                    Name = "DB_Settings",
                                    Structure = new FakeStructure
                                    {
                                        Composition = new FakeComposition
                                        {
                                            Members =
                                            {
                                                new FakeMember
                                                {
                                                    Name = "Nested",
                                                    Composition = new FakeComposition
                                                    {
                                                        Members =
                                                        {
                                                            new FakeMember { Name = "Child" }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // Act
            var blocks = adapter.FindGlobalDbs(plcSoftware);
            var members = adapter.GetDbMembers(blocks!.Single());
            var nestedMembers = adapter.GetNestedMembers(members!.Single());

            // Assert
            Assert.IsNotNull(blocks);
            Assert.AreEqual(1, blocks!.Count);
            Assert.AreEqual("DB_Settings", adapter.GetName(blocks.Single()));
            Assert.IsNotNull(members);
            Assert.AreEqual(1, members!.Count);
            Assert.AreEqual("Nested", adapter.GetName(members.Single()));
            Assert.IsNotNull(nestedMembers);
            Assert.AreEqual("Child", adapter.GetName(nestedMembers!.Single()));
        }

        [TestMethod]
        public void FindUdts_AndGetUdtMembers_ReturnRecursiveTypes()
        {
            // Arrange
            var adapter = new V21OpennessAdapter();
            var plcSoftware = new FakePlcSoftware
            {
                TypeGroup = new FakeTypeGroup
                {
                    Groups =
                    {
                        new FakeTypeGroup
                        {
                            Types =
                            {
                                new PlcStruct
                                {
                                    Name = "DriveData",
                                    Structure = new FakeStructure
                                    {
                                        Members =
                                        {
                                            new FakeMember { Name = "Speed" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // Act
            var udts = adapter.FindUdts(plcSoftware);
            var members = adapter.GetUdtMembers(udts!.Single());

            // Assert
            Assert.IsNotNull(udts);
            Assert.AreEqual(1, udts!.Count);
            Assert.AreEqual("DriveData", adapter.GetName(udts.Single()));
            Assert.IsNotNull(members);
            Assert.AreEqual("Speed", adapter.GetName(members!.Single()));
        }

        private sealed class FakeExportableObject
        {
            public string? LastExportPath { get; private set; }

            public ExportOptions? LastExportOption { get; private set; }

            public void Export(FileInfo fileInfo, ExportOptions options)
            {
                LastExportPath = fileInfo.FullName;
                LastExportOption = options;
            }
        }

        private enum ProgrammingLanguage
        {
            ProDiag
        }

        private enum ExportOptions
        {
            WithDefaults,
            None
        }

        private enum FakeImportOptions
        {
            None = 0,
            Override = 1
        }

        private enum FakeImportDocumentOptions
        {
            None = 0,
            Override = 1
        }

        private sealed class FakePlcSoftware
        {
            public FakeTagTableGroup? TagTableGroup { get; set; }

            public FakeBlockGroup? BlockGroup { get; set; }

            public FakeTypeGroup? TypeGroup { get; set; }
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
        }

        private sealed class FakeComment
        {
            public List<FakeCommentItem> Items { get; } = new List<FakeCommentItem>
            {
                new FakeCommentItem("en-US"),
                new FakeCommentItem("it-IT")
            };
        }

        private sealed class FakeCommentItem
        {
            public FakeCommentItem(string cultureName)
            {
                Culture = new FakeCulture(cultureName);
                Language = new FakeLanguage(cultureName);
            }

            public FakeCulture Culture { get; }

            public FakeLanguage Language { get; }

            public string Text { get; set; } = string.Empty;
        }

        private sealed class FakeLanguage
        {
            public FakeLanguage(string cultureName)
            {
                Culture = new FakeCulture(cultureName);
            }

            public FakeCulture Culture { get; }
        }

        private sealed class FakeCulture
        {
            public FakeCulture(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        private sealed class FakeBlockGroup
        {
            public List<object> Blocks { get; } = new List<object>();

            public List<FakeBlockGroup> Groups { get; } = new List<FakeBlockGroup>();
        }

        private sealed class FakeTypeGroup
        {
            public List<object> Types { get; } = new List<object>();

            public List<FakeTypeGroup> Groups { get; } = new List<FakeTypeGroup>();
        }

        private sealed class FakeImportableCollection
        {
            public object ImportResult { get; } = new object();

            public string? LastImportPath { get; private set; }

            public FakeImportOptions? LastImportOption { get; private set; }

            public object Import(FileInfo path, FakeImportOptions importOptions)
            {
                LastImportPath = path.FullName;
                LastImportOption = importOptions;
                return ImportResult;
            }
        }

        private sealed class FakeDocumentImportableCollection
        {
            public object ImportResult { get; } = new object();

            public string? LastDirectoryPath { get; private set; }

            public string? LastBaseName { get; private set; }

            public FakeImportDocumentOptions? LastImportOption { get; private set; }

            public object ImportFromDocuments(DirectoryInfo directoryInfo, string fileNameWithoutExtension, FakeImportDocumentOptions importDocumentOptions)
            {
                LastDirectoryPath = directoryInfo.FullName;
                LastBaseName = fileNameWithoutExtension;
                LastImportOption = importDocumentOptions;
                return ImportResult;
            }
        }

        private sealed class GlobalDB
        {
            public string Name { get; set; } = string.Empty;

            public FakeStructure Structure { get; set; } = new FakeStructure();
        }

        private sealed class PlcStruct
        {
            public string Name { get; set; } = string.Empty;

            public FakeStructure Structure { get; set; } = new FakeStructure();
        }

        private sealed class FakeStructure
        {
            public FakeComposition? Composition { get; set; }

            public List<object> Members { get; } = new List<object>();
        }

        private sealed class FakeComposition
        {
            public List<object> Members { get; } = new List<object>();
        }

        private sealed class FakeMember
        {
            public string Name { get; set; } = string.Empty;

            public FakeComposition? Composition { get; set; }
        }
    }
}
