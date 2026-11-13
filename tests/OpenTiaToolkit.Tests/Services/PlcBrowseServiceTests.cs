using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcBrowseServiceTests
{
    [TestMethod]
    public async Task GetBlocks_NameFilterDoesNotReadRejectedBlockMetadata()
    {
        var blocks = Enumerable.Range(0, 1000).Select(i => new FakeBlock("Block_" + i)).ToArray();
        var result = await Service(blocks).GetBlocksAsync("^block_417$");

        Assert.AreEqual("Block_417", result.Single().Name);
        Assert.AreEqual("Comment", result.Single().Comment);
        Assert.AreEqual(5, blocks[417].MetadataReads);
        Assert.AreEqual(5, blocks.Sum(x => x.MetadataReads));
        Assert.IsTrue(blocks.All(x => x.NameReads == 1));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("[")]
    public async Task GetBlocks_UnfilteredAndInvalidRegexKeepAllMetadataAndOrder(string? filter)
    {
        var blocks = new[] { new FakeBlock("Z"), new FakeBlock("A") };
        var result = await Service(blocks).GetBlocksAsync(filter);

        CollectionAssert.AreEqual(new[] { "Z", "A" }, result.Select(x => x.Name).ToArray());
        Assert.IsTrue(blocks.All(x => x.MetadataReads == 5));
    }

    [TestMethod]
    public async Task SearchBlocks_NameSearchTreatsPatternAsLiteral()
    {
        var blocks = new[] { new FakeBlock("Motor[A].+"), new FakeBlock("MotorAxx"), new FakeBlock("Unrelated") };
        var result = await Service(blocks).SearchBlocksAsync("[a].+", "name");

        Assert.AreEqual("Motor[A].+", result.Single().Name);
        Assert.AreEqual(5, blocks.Sum(x => x.MetadataReads));
    }

    [TestMethod]
    public async Task SearchBlocks_NameSearchKeepsOrdinalCaseMatchingInTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var blocks = new[] { new FakeBlock("INPUT"), new FakeBlock("Other") };
            var result = await Service(blocks).SearchBlocksAsync("input", "name");

            Assert.AreEqual("INPUT", result.Single().Name);
            Assert.AreEqual(0, blocks[1].MetadataReads);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [DataTestMethod]
    [DataRow("comment")]
    [DataRow("both")]
    [DataRow("unrecognized-mode")]
    public async Task SearchBlocks_CommentModesStillReadNonmatchingNames(string mode)
    {
        var blocks = new[] { new FakeBlock("Unrelated", comment: "Motor fault"), new FakeBlock("Other") };
        var result = await Service(blocks).SearchBlocksAsync("motor", mode);

        Assert.AreEqual("Unrelated", result.Single().Name);
        Assert.IsTrue(blocks.All(x => x.MetadataReads == 5));
    }

    [TestMethod]
    public async Task SearchBlocks_TypeFilterPreservesCandidateMetadata()
    {
        var blocks = new[] { new FakeBlock("Motor_FB", "FB"), new FakeBlock("Motor_DB", "DB"), new FakeBlock("Other") };
        var result = await Service(blocks).SearchBlocksAsync("motor", "name", "fb");

        var match = result.Single();
        Assert.AreEqual("Motor_FB", match.Name);
        Assert.AreEqual("FB", match.Type);
        Assert.AreEqual("SCL", match.Language);
        Assert.AreEqual("1", match.Number);
        Assert.IsFalse(match.IsReadOnly);
        Assert.AreEqual(0, blocks[2].MetadataReads);
    }

    [TestMethod]
    public async Task GetBlocks_NextCallReadsFreshNamesAndMetadata()
    {
        var block = new FakeBlock("Before");
        var service = Service(block);
        Assert.AreEqual(0, (await service.GetBlocksAsync("After")).Count);
        Assert.AreEqual(0, block.MetadataReads);
        block.BlockName = "After";
        Assert.AreEqual("After", (await service.GetBlocksAsync("After")).Single().Name);
        Assert.AreEqual(5, block.MetadataReads);
    }

    private static PlcBrowseService Service(params FakeBlock[] blocks) => new(new EngineeringSessionContext
    {
        PlcSoftware = new FakeSoftware { BlockGroup = new FakeGroup { Blocks = blocks } },
        CurrentPlcName = "PLC_1"
    }, logger: null);

    private sealed class FakeSoftware
    {
        public FakeGroup BlockGroup { get; set; } = null!;
    }

    private sealed class FakeGroup
    {
        public IReadOnlyList<FakeBlock> Blocks { get; set; } = Array.Empty<FakeBlock>();
    }

    private sealed class FakeBlock
    {
        private readonly string _type;
        private readonly string _comment;
        public FakeBlock(string name, string type = "FB", string comment = "Comment")
        {
            BlockName = name;
            _type = type;
            _comment = comment;
        }
        public string BlockName { get; set; }
        public int NameReads { get; private set; }
        public int MetadataReads { get; private set; }
        public string Name { get { NameReads++; return BlockName; } }
        public string Type { get { MetadataReads++; return _type; } }
        public string Number { get { MetadataReads++; return "1"; } }
        public string ProgrammingLanguage { get { MetadataReads++; return "SCL"; } }
        public bool IsReadOnly { get { MetadataReads++; return false; } }
        public FakeText Comment { get { MetadataReads++; return new FakeText { Text = _comment }; } }
    }

    private sealed class FakeText
    {
        public string Text { get; set; } = string.Empty;
    }
}
