using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.Core.Services.PlcTags;
using OpenTiaToolkit.Core.Services.V21;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcCrossReferenceBatchTests
{
    [TestMethod]
    public async Task BatchReadsTargetInventoryOnceAndQueriesConsumersFreshly()
    {
        var software = Software(1000);
        var block = new Named("Target");
        software.BlockGroup.Blocks.Add(block);
        var context = new EngineeringSessionContext { PlcSoftware = software };
        var service = Service(context);
        var lookup = service.CreateLookup();
        Assert.AreEqual(0, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));

        for (var i = 0; i < 5; i++)
        {
            block.Consumers.ConsumerName = "Caller_" + i;
            var result = await lookup("Target");
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("Caller_" + i, result.Entries.Single().Name);
        }
        Assert.AreEqual(1000, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));
        Assert.AreEqual(1, block.NameReads);
        Assert.AreEqual(5, block.Consumers.Reads);

        var added = new Named("Added");
        software.BlockGroup.Blocks.Add(added);
        var fresh = service.CreateLookup();
        Assert.IsTrue((await fresh("Added")).Success);
        Assert.AreEqual(2000, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));
    }

    [TestMethod]
    public async Task StandaloneQueriesRemainFreshAndInvalidNamesDoNotDiscoverTargets()
    {
        var software = Software(10);
        var context = new EngineeringSessionContext { PlcSoftware = software };
        var service = Service(context);
        Assert.IsFalse((await service.CreateLookup()(" ")).Success);
        Assert.AreEqual(0, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));
        await service.GetAsync("Missing");
        software.BlockGroup.Blocks.Add(new Named("New"));
        Assert.IsTrue((await service.GetAsync("New")).Success);
        Assert.AreEqual(20, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));
    }

    [TestMethod]
    public async Task BatchRejectsChangedPlcSelectionInsteadOfUsingOldHandles()
    {
        var first = Software(1);
        first.BlockGroup.Blocks.Add(new Named("Target"));
        var context = new EngineeringSessionContext { PlcSoftware = first };
        var service = Service(context);
        var lookup = service.CreateLookup();
        Assert.IsTrue((await lookup("Target")).Success);
        var second = Software(1);
        second.BlockGroup.Blocks.Add(new Named("Target"));
        context.PlcSoftware = second;
        var changed = await lookup("Target");
        Assert.IsFalse(changed.Success);
        StringAssert.Contains(changed.Message, "selection changed");
        Assert.AreEqual(0, second.BlockGroup.Blocks[0].Consumers.Reads);
        Assert.IsTrue((await service.CreateLookup()("Target")).Success);
    }

    [TestMethod]
    public async Task ExactDuplicatesAcrossKindsAndGroupsWinOverSubstringMatches()
    {
        var software = Software(0);
        software.TagTableGroup.TagTables[0].Tags.Add(new Named("Target", "TagCaller"));
        software.BlockGroup.Blocks.Add(new Named("TARGET", "RootCaller"));
        software.BlockGroup.Blocks.Add(new Named("Target_Extended", "NearCaller"));
        var child = new Group();
        child.Blocks.Add(new Named("Target", "ChildCaller"));
        software.BlockGroup.Groups.Add(child);
        var lookup = Service(new EngineeringSessionContext { PlcSoftware = software }).CreateLookup();
        var exact = await lookup("target");
        CollectionAssert.AreEqual(new[] { "TagCaller", "RootCaller", "ChildCaller" }, exact.Entries.Select(x => x.Name).ToArray());
        var fuzzy = await lookup("arg");
        CollectionAssert.AreEqual(new[] { "TagCaller", "RootCaller", "NearCaller", "ChildCaller" }, fuzzy.Entries.Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public async Task ProductionImpactAnalyzerCreatesFreshInventoryForEachPhase()
    {
        var software = Software(1000);
        software.BlockGroup.Blocks.AddRange(new[] { new Named("A"), new Named("B") });
        var context = new EngineeringSessionContext { PlcSoftware = software, CurrentPlcName = "PLC_1" };
        var graph = V21ComponentGraph.CreateDefault();
        var plc = new PlcService(Mock.Of<IPortalService>(), context, null, graph.Software, graph.Compilation, graph.Types, graph.Online, graph.Documents, graph.Objects, graph.Tags, new V21PlcTagRepository(graph.Tags, graph.Objects));
        var analyzer = new PlcCodeImpactAnalyzer(plc, new PlcCodeTargetRepository(context, graph.Documents, graph.Objects), new PlcCodeTransactionStore());
        await analyzer.AnalyzeAsync(new PlcCodeTransaction());
        await analyzer.AnalyzeAsync(new PlcCodeTransaction
        {
            Operations = new[]
            {
                new PlcCodeTransactionOperation
                {
                    Sequence = 1,
                    TargetSnapshots = new[]
                    {
                        new PlcCodeTransactionTargetSnapshot { Action = "Create", Target = new PlcBlockIdentity { Name = "New", BlockType = "FC" } }
                    }
                }
            }
        });
        Assert.AreEqual(0, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads), "An analysis without consumer queries must not discover targets.");
        var transaction = new PlcCodeTransaction
        {
            Operations = new[]
            {
                new PlcCodeTransactionOperation
                {
                    Sequence = 1,
                    TargetSnapshots = new[] { "A", "B" }.Select(name => new PlcCodeTransactionTargetSnapshot
                    {
                        Action = "Delete", Target = new PlcBlockIdentity { Name = name, BlockType = "FC" }
                    }).ToArray()
                }
            }
        };
        var first = await analyzer.AnalyzeAsync(transaction);
        Assert.AreEqual(2, first.Targets.Count);
        Assert.AreEqual(1000, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));
        software.BlockGroup.Blocks[0].Consumers.ConsumerName = "Changed";
        var next = await analyzer.AnalyzeAsync(transaction);
        Assert.AreEqual(2000, software.TagTableGroup.TagTables[0].Tags.Sum(x => x.NameReads));
        Assert.AreNotEqual(first.Fingerprint, next.Fingerprint);
        Assert.AreEqual(2, software.BlockGroup.Blocks[0].Consumers.Reads);
    }

    private static PlcCrossReferenceService Service(EngineeringSessionContext context)
    {
        var graph = V21ComponentGraph.CreateDefault();
        return new PlcCrossReferenceService(context, graph.Types, graph.Tags, graph.Objects);
    }
    private static FakeSoftware Software(int tagCount)
    {
        var software = new FakeSoftware();
        var table = new Table();
        table.Tags.AddRange(Enumerable.Range(0, tagCount).Select(i => new Named("Tag_" + i)));
        software.TagTableGroup.TagTables.Add(table);
        return software;
    }
    public sealed class FakeSoftware
    {
        public Group TagTableGroup { get; } = new();
        public Group BlockGroup { get; } = new();
        public Group TypeGroup { get; } = new();
        public Group ExternalSourceGroup { get; } = new();
    }
    public sealed class Group
    {
        public List<Table> TagTables { get; } = new();
        public List<Named> Blocks { get; } = new();
        public List<Named> Types { get; } = new();
        public List<Named> ExternalSources { get; } = new();
        public List<Group> Groups { get; } = new();
    }
    public sealed class Table { public string Name => "Tags"; public List<Named> Tags { get; } = new(); }
    public sealed class Named
    {
        private readonly string _name;
        public Named(string name, string caller = "Caller") { _name = name; Consumers.ConsumerName = caller; }
        public int NameReads { get; private set; }
        public string Name { get { NameReads++; return _name; } }
        public ConsumerService Consumers { get; } = new();
        public object GetService<T>() => Consumers;
    }
    public sealed class ConsumerService
    {
        public string ConsumerName { get; set; } = "Caller";
        public int Reads { get; private set; }
        public object[] GetCrossReferences(string name) { Reads++; return new object[] { new { Name = ConsumerName, Path = ConsumerName, Type = "Block" } }; }
    }
}
