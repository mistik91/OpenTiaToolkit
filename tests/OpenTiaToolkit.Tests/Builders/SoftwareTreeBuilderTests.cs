using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Builders
{
    [TestClass]
    public class SoftwareTreeBuilderTests
    {
        [TestMethod]
        public void Build_CreatesContainerAndNestedGroupNodes()
        {
            // Arrange
            var plcSoftware = new FakePlcSoftware
            {
                Name = "PLC_1",
                BlockGroup = new FakeGroup
                {
                    Blocks = { new FakeBlock { Name = "Main", Type = "OB" } },
                    Groups =
                    {
                        new FakeGroup
                        {
                            Name = "Motors",
                            Blocks = { new FakeBlock { Name = "MotorFB", ProgrammingLanguage = "LAD" } }
                        }
                    }
                },
                TypeGroup = new FakeGroup
                {
                    Types = { new FakeTypeItem { Name = "MotorType" } },
                    Documents = { new FakeDocument { Name = "MotorDocs" } }
                },
                TagTableGroup = new FakeGroup
                {
                    TagTables = { new FakeTagTable { Name = "DefaultTags" } }
                },
                ExternalSourceGroup = new FakeGroup
                {
                    ExternalSources = { new FakeSource { Name = "MainSource" } }
                }
            };

            var builder = new SoftwareTreeBuilder();

            // Act
            var tree = builder.Build(plcSoftware, plcSoftware.Name);

            // Assert
            Assert.AreEqual("PLC_1", tree.Name);
            CollectionAssert.AreEqual(
                new[] { "Blocks", "Types", "Tag Tables", "Source Files" },
                tree.Children.Select(child => child.Name).ToArray());

            var blocksNode = tree.Children.First(child => child.Name == "Blocks");
            Assert.IsTrue(blocksNode.Children.Any(child => child.Name == "Main" && child.Type == "OB"));

            var motorsGroup = blocksNode.Children.Single(child => child.Name == "Motors");
            Assert.IsTrue(motorsGroup.Children.Any(child => child.Name == "MotorFB" && child.Type.Contains("LAD")));

            var typesNode = tree.Children.First(child => child.Name == "Types");
            Assert.IsTrue(typesNode.Children.Any(child => child.Name == "MotorType"));
            Assert.IsTrue(typesNode.Children.Any(child => child.Name == "MotorDocs"));
        }

        [TestMethod]
        public void Build_HonorsDepthLimitAtContainerLevel()
        {
            // Arrange
            var plcSoftware = new FakePlcSoftware
            {
                Name = "PLC_1",
                BlockGroup = new FakeGroup
                {
                    Blocks = { new FakeBlock { Name = "Main", Type = "OB" } }
                }
            };

            var builder = new SoftwareTreeBuilder();

            // Act
            var tree = builder.Build(plcSoftware, plcSoftware.Name, maxDepth: 1);

            // Assert
            Assert.AreEqual(1, tree.Children.Count);
            Assert.AreEqual("Blocks", tree.Children.Single().Name);
            Assert.AreEqual(0, tree.Children.Single().Children.Count);
        }

        private sealed class FakePlcSoftware
        {
            public string Name { get; set; } = string.Empty;

            public FakeGroup? BlockGroup { get; set; }

            public FakeGroup? TypeGroup { get; set; }

            public FakeGroup? TagTableGroup { get; set; }

            public FakeGroup? ExternalSourceGroup { get; set; }
        }

        private sealed class FakeGroup
        {
            public string Name { get; set; } = string.Empty;

            public List<FakeBlock> Blocks { get; } = new List<FakeBlock>();

            public List<FakeTypeItem> Types { get; } = new List<FakeTypeItem>();

            public List<FakeDocument> Documents { get; } = new List<FakeDocument>();

            public List<FakeTagTable> TagTables { get; } = new List<FakeTagTable>();

            public List<FakeSource> ExternalSources { get; } = new List<FakeSource>();

            public List<FakeGroup> Groups { get; } = new List<FakeGroup>();
        }

        private sealed class FakeBlock
        {
            public string Name { get; set; } = string.Empty;

            public string? Type { get; set; }

            public string? ProgrammingLanguage { get; set; }
        }

        private sealed class FakeTypeItem
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class FakeDocument
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class FakeTagTable
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class FakeSource
        {
            public string Name { get; set; } = string.Empty;
        }
    }
}
