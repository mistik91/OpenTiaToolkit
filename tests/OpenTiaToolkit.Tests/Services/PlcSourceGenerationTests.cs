using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcSourceGenerationTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TargetedGeneration_SelectsExactGroupOverloadOnce(bool types)
    {
        var (service, source, software) = Fixture();
        var result = await service.GenerateBlocksAsync("Fixture", false, true,
            types ? null : "PLC_1/Blocks/Area/Deep", types ? "PLC_1/Types/Area/Deep" : null);
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Applied);
        Assert.AreEqual(0, source.RootCalls);
        Assert.AreEqual(1, source.GroupCalls);
        Assert.AreSame(types ? (object)software.TypeGroup.Groups[0].Groups[0] : software.BlockGroup.Groups[0].Groups[0], source.Destination);
        Assert.AreEqual(Option.None, source.Option);
    }

    [DataTestMethod]
    [DataRow(true, false, "Area/Deep", null)]
    [DataRow(false, false, "Area/Deep", null)]
    [DataRow(true, false, null, "Area/Deep")]
    [DataRow(false, true, "Gone", null)]
    [DataRow(false, true, null, "Gone")]
    [DataRow(false, true, "Area/Deep", "Area/Deep")]
    public async Task PreviewDeniedOrInvalidDestinations_DoNotInvokeGeneration(bool preview, bool confirm, string? group, string? typeGroup)
    {
        var (service, source, _) = Fixture();
        var result = await service.GenerateBlocksAsync("Fixture", preview, confirm, group, typeGroup);
        Assert.AreEqual(preview, result.Success);
        Assert.IsFalse(result.Applied);
        Assert.AreEqual(0, source.RootCalls + source.GroupCalls);
    }

    [TestMethod]
    public async Task RootAliases_PreserveDefaultGeneration()
    {
        var (service, source, _) = Fixture();
        var result = await service.GenerateBlocksAsync("Fixture", false, true, "PLC_1/Blocks", "PLC_1/Types");
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, source.RootCalls);
        Assert.AreEqual(0, source.GroupCalls);
    }

    [TestMethod]
    public async Task MissingTargetedApi_FailsWithoutFallingBackToRoot()
    {
        var software = new Software();
        var source = new RootOnlySource();
        software.ExternalSourceGroup.ExternalSources.Add(source);
        var result = await Service(software).GenerateBlocksAsync("Fixture", false, true, "Area/Deep");
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "root generation was not attempted");
        Assert.AreEqual(0, source.RootCalls);
    }

    [TestMethod]
    public async Task VendorFailure_IsReturnedWithoutRetryingAnotherOverload()
    {
        var (service, source, _) = Fixture();
        source.Throw = true;
        var result = await service.GenerateBlocksAsync("Fixture", false, true, "Area/Deep");
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "native failure");
        Assert.AreEqual(1, source.GroupCalls);
        Assert.AreEqual(0, source.RootCalls);
    }

    private static (PlcExternalSourceService Service, Source Source, Software Software) Fixture()
    {
        var software = new Software();
        var source = new Source();
        software.ExternalSourceGroup.ExternalSources.Add(source);
        return (Service(software), source, software);
    }
    private static PlcExternalSourceService Service(Software software)
    {
        var context = new EngineeringSessionContext { CurrentPlcName = "PLC_1", PlcSoftware = software };
        return new PlcExternalSourceService(context, new PlcBrowseService(context, null));
    }
    private sealed class Software
    {
        public BlockGroup BlockGroup { get; } = new() { Groups = { new BlockGroup { Name = "Area", Groups = { new BlockGroup { Name = "Deep" } } } } };
        public TypeGroup TypeGroup { get; } = new() { Groups = { new TypeGroup { Name = "Area", Groups = { new TypeGroup { Name = "Deep" } } } } };
        public SourceGroup ExternalSourceGroup { get; } = new();
    }
    private sealed class SourceGroup { public List<object> ExternalSources { get; } = new(); }
    private sealed class BlockGroup { public string Name { get; set; } = "Blocks"; public List<BlockGroup> Groups { get; } = new(); }
    private sealed class TypeGroup { public string Name { get; set; } = "Types"; public List<TypeGroup> Groups { get; } = new(); }
    private enum Option { None, KeepOnError }
    private class RootOnlySource
    {
        public string Name => "Fixture";
        public int RootCalls { get; private set; }
        public void GenerateBlocksFromSource() => RootCalls++;
    }
    private sealed class Source : RootOnlySource
    {
        public object? Destination { get; private set; }
        public Option Option { get; private set; }
        public int GroupCalls { get; private set; }
        public bool Throw { get; set; }
        public void GenerateBlocksFromSource(BlockGroup group, Option option) => Generate(group, option);
        public void GenerateBlocksFromSource(TypeGroup group, Option option) => Generate(group, option);
        private void Generate(object group, Option option)
        {
            GroupCalls++;
            if (Throw) throw new InvalidOperationException("native failure");
            Destination = group; Option = option;
        }
    }
}
