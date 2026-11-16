using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class HmiJsonCompositeFilteringTests
{
    [DataTestMethod]
    [DataRow("EventHandlers")]
    [DataRow("PropertyEventHandlers")]
    [DataRow("Dynamizations")]
    [DataRow("Interface")]
    public void Export_SelectionPreservesLegacyPayloadOrderingAndMatching(string kind)
    {
        foreach (var mode in new[] { "all", "root", "exact", "prefix", "missing", "multiple", "unrelated" })
        {
            var entries = Enumerable.Range(0, 8).Reverse().Select(i => new Entry("Key_" + i.ToString("D4"))).Concat(new[] { new Entry("") }).ToArray();
            var owner = new Owner(entries);
            var adapter = new V21UnifiedHmiAdapter(null);
            var full = ReadFull(adapter, owner, kind);
            var key = Key(kind, full.Last());
            var properties = mode switch
            {
                "all" => Array.Empty<string>(),
                "root" => new[] { kind.ToUpperInvariant() },
                "exact" => new[] { (kind + "." + key).ToLowerInvariant() },
                "prefix" => new[] { kind + "." + key + ".Script.ScriptCode" },
                "missing" => new[] { kind + ".Missing" },
                "multiple" => new[] { kind + "." + Key(kind, full[1]), kind + "." + key, kind + "." + key },
                _ => new[] { "Width" }
            };
            var filters = HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions { Properties = properties });
            var expected = full.Where(entry => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(kind, Key(kind, entry), filters)).ToArray();
            foreach (var entry in entries) entry.Reset();
            owner.CollectionReads = 0;
            var warnings = new List<string>();

            var actual = Export(adapter, owner, kind, filters, warnings);

            Assert.AreEqual(JsonSerializer.Serialize(expected.Length > 0 ? expected : null), JsonSerializer.Serialize(actual), kind + ":" + mode);
            Assert.AreEqual(0, warnings.Count);
            Assert.AreEqual(expected.Length, entries.Sum(entry => entry.PayloadReads), kind + ":" + mode);
            Assert.AreEqual(mode == "unrelated" ? 0 : 1, owner.CollectionReads);
            Assert.AreEqual(0, entries.Last().PayloadReads, "Invalid identities must not project payloads.");
            if (kind == "Interface") Assert.AreEqual(expected.Length * 2, entries.Sum(entry => entry.NestedTagReads), "Selecting an interface entry preserves its entire nested payload.");
        }
    }

    [DataTestMethod]
    [DataRow("EventHandlers")]
    [DataRow("PropertyEventHandlers")]
    [DataRow("Dynamizations")]
    [DataRow("Interface")]
    public void Export_ThousandEntries_ReadsOnlyTheSelectedPayload(string kind)
    {
        var entries = Enumerable.Range(0, 1000).Select(i => new Entry("Key_" + i.ToString("D4"))).ToArray();
        var owner = new Owner(entries);
        var adapter = new V21UnifiedHmiAdapter(null);
        var full = ReadFull(adapter, owner, kind);
        Assert.AreEqual(1000, entries.Sum(entry => entry.PayloadReads));
        foreach (var entry in entries) entry.Reset();
        var filters = HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions { Properties = new[] { kind + "." + Key(kind, full.Last()) } });
        var result = Export(adapter, owner, kind, filters, new List<string>());
        Assert.AreEqual(1, result!.Length);
        Assert.AreEqual(1, entries.Sum(entry => entry.PayloadReads));
        Assert.IsTrue(entries.All(entry => entry.IdentityReads > 0));
        Assert.IsTrue(entries.Take(999).All(entry => entry.PayloadReads == 0 && entry.NestedTagReads == 0 && entry.KindReads == 0));
    }

    [DataTestMethod]
    [DataRow("EventHandlers")]
    [DataRow("PropertyEventHandlers")]
    [DataRow("Dynamizations")]
    [DataRow("Interface")]
    public void Export_CollectionFailuresRetainWarnings(string kind)
    {
        var warnings = new List<string>();
        var result = Export(new V21UnifiedHmiAdapter(null), new Owner(new object()), kind,
            new HmiJsonFilterOptions { Properties = new[] { kind + ".Missing" } }, warnings);
        Assert.IsNull(result);
        Assert.AreEqual(1, warnings.Count);
        StringAssert.Contains(warnings[0], "failed to export " + kind);
        StringAssert.Contains(warnings[0], "composition is not enumerable");
    }

    [DataTestMethod]
    [DataRow("EventHandlers")]
    [DataRow("PropertyEventHandlers")]
    [DataRow("Dynamizations")]
    [DataRow("Interface")]
    public void FullReaderAfterFilteredExport_IsFreshAndUnfiltered(string kind)
    {
        var entries = new List<Entry> { new("Before") };
        var owner = new Owner(entries);
        var adapter = new V21UnifiedHmiAdapter(null);
        Assert.IsNull(Export(adapter, owner, kind, new HmiJsonFilterOptions { Properties = new[] { kind + ".Missing" } }, new List<string>()));
        entries.Add(new Entry("Added"));
        var full = ReadFull(adapter, owner, kind);
        Assert.AreEqual(2, full.Length);
        Assert.IsTrue(entries.All(entry => entry.PayloadReads == 1));
    }

    private static object[]? Export(V21UnifiedHmiAdapter adapter, Owner owner, string kind, HmiJsonFilterOptions filters, List<string> warnings)
    {
        var service = new HmiJsonExportService(new EngineeringSessionContext(), adapter, null);
        var method = typeof(HmiJsonExportService).GetMethod("Export" + kind, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((IEnumerable?)method.Invoke(service, new object[] { owner, filters, warnings, "Fixture" }))?.Cast<object>().ToArray();
    }
    private static object[] ReadFull(V21UnifiedHmiAdapter adapter, Owner owner, string kind)
    {
        bool ok; string? error; IEnumerable result;
        switch (kind)
        {
            case "EventHandlers": ok = adapter.TryReadEventHandlers(owner, out var events, out error); result = events; break;
            case "PropertyEventHandlers": ok = adapter.TryReadPropertyEventHandlers(owner, out var properties, out error); result = properties; break;
            case "Dynamizations": ok = adapter.TryReadDynamizations(owner, out var dynamics, out error); result = dynamics; break;
            default: ok = adapter.TryReadInterfaceBindings(owner, out var bindings, out error); result = bindings; break;
        }
        Assert.IsTrue(ok, error);
        return result.Cast<object>().ToArray();
    }
    private static string Key(string kind, object entry) => kind switch
    {
        "EventHandlers" => ((HmiJsonEventHandler)entry).EventType,
        "PropertyEventHandlers" => HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(HmiJsonCompositeComparer.GetPropertyEventHandlerKey(((HmiJsonPropertyEventHandler)entry).PropertyName, ((HmiJsonPropertyEventHandler)entry).EventType)!),
        "Dynamizations" => ((HmiJsonDynamization)entry).Property,
        _ => ((HmiJsonInterfaceBinding)entry).PropertyName
    };
    private sealed class Owner
    {
        private readonly object _entries;
        public Owner(object entries) => _entries = entries;
        public int CollectionReads;
        public object EventHandlers { get { CollectionReads++; return _entries; } }
        public object PropertyEventHandlers { get { CollectionReads++; return _entries; } }
        public object Dynamizations { get { CollectionReads++; return _entries; } }
        public object Interface { get { CollectionReads++; return _entries; } }
    }
    private sealed class Entry
    {
        private readonly string _name;
        public Entry(string name) => _name = name;
        public int IdentityReads, PayloadReads, NestedTagReads, KindReads;
        public string EventType { get { IdentityReads++; return _name; } }
        public string PropertyName { get { IdentityReads++; return _name; } }
        public string DynamizationType { get { KindReads++; return "Tag"; } }
        public object Script { get { PayloadReads++; return new ScriptPayload(_name); } }
        public string Tag { get { PayloadReads++; return "Tag_" + _name; } }
        public object Value { get { PayloadReads++; return "Value_" + _name; } }
        public object[] Dynamizations => new object[] { new NestedTag(this, "Left"), new NestedTag(this, "Top") };
        public void Reset() { IdentityReads = 0; PayloadReads = 0; NestedTagReads = 0; KindReads = 0; }
    }
    private sealed class ScriptPayload
    {
        public ScriptPayload(string name) => ScriptCode = "// " + name;
        public bool Async => false;
        public string GlobalDefinitionAreaScriptCode => "// global";
        public string ScriptCode { get; }
    }
    private sealed class NestedTag
    {
        private readonly Entry _owner;
        public NestedTag(Entry owner, string property) { _owner = owner; PropertyName = property; }
        public string PropertyName { get; }
        public string DynamizationType => "Tag";
        public string Tag { get { _owner.NestedTagReads++; return "NestedTag"; } }
    }
}
