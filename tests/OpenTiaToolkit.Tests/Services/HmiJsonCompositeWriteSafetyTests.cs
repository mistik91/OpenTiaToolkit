using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class HmiJsonCompositeWriteSafetyTests
{
    [DataTestMethod]
    [DataRow("Screen")]
    [DataRow("Item")]
    public void MixedNewCollections_AreRejectedBeforeAnOwnerExists(string scope)
    {
        var result = Validate(null, new[] { Handler() }, new[] { Dynamic() }, out var accepted, scope: scope);
        Assert.IsFalse(accepted);
        Assert.AreEqual(1, result.PropertyFailedCount);
        Assert.AreEqual(0, result.PropertySetCount);
        Assert.AreEqual(scope, result.FailedProperties[0].Scope);
        StringAssert.Contains(result.FailedProperties[0].Reason, "separate patches that retain");
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void EditingOneCollection_AccountsForTheUnmentionedExistingCollection(bool editEvents)
    {
        var owner = editEvents ? new Owner(false, true) : new Owner(true, false);
        var result = Validate(owner, editEvents ? new[] { Handler() } : null,
            editEvents ? null : new[] { Dynamic() }, out var accepted);
        Assert.IsFalse(accepted);
        Assert.AreEqual(1, result.PropertyFailedCount);
    }

    [TestMethod]
    public void EntryFilter_PreservesTheOtherExistingEntriesWhenCheckingCoexistence()
    {
        var result = Validate(new Owner(true, true), null,
            new[] { Dynamic("Top") }, out var accepted, new[] { "dynamizations.top" });
        Assert.IsFalse(accepted);
        Assert.AreEqual(1, result.PropertyFailedCount);
    }

    [DataTestMethod]
    [DataRow("PropertyEventHandlers")]
    [DataRow("Dynamizations")]
    public void FilteredOutChanges_DoNotInventACollision(string excluded)
    {
        var properties = excluded == "PropertyEventHandlers" ? new[] { "Dynamizations" } : new[] { "PropertyEventHandlers" };
        Validate(new Owner(false, false), new[] { Handler() }, new[] { Dynamic() }, out var accepted, properties);
        Assert.IsTrue(accepted);
    }

    [TestMethod]
    public void UnrelatedProperties_DoNotReadCompositeCollections()
    {
        var owner = new Owner(true, true);
        Validate(owner, new[] { Handler() }, new[] { Dynamic() }, out var accepted, new[] { "Width" });
        Assert.IsTrue(accepted);
        Assert.AreEqual(0, owner.Reads);
    }

    [TestMethod]
    public void ExistingCoexistence_UnchangedCollectionsRemainReadableAndImportable()
    {
        Validate(new Owner(true, true), new[] { Handler() }, new[] { Dynamic() }, out var accepted);
        Assert.IsTrue(accepted);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DirectSwitch_RequiresClearingAndReopeningFirst(bool removeEvents)
    {
        Validate(new Owner(removeEvents, !removeEvents),
            removeEvents ? Array.Empty<HmiJsonPropertyEventHandler>() : new[] { Handler() },
            removeEvents ? new[] { Dynamic() } : Array.Empty<HmiJsonDynamization>(), out var accepted);
        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public void ClearingBothCollections_IsAllowed()
    {
        Validate(new Owner(true, true), Array.Empty<HmiJsonPropertyEventHandler>(), Array.Empty<HmiJsonDynamization>(), out var accepted);
        Assert.IsTrue(accepted);
    }

    [TestMethod]
    public void DifferentOwners_CanUseDifferentCompositeKinds()
    {
        Validate(new Owner(false, false), new[] { Handler() }, null, out var eventsAccepted);
        Validate(new Owner(false, false), null, new[] { Dynamic() }, out var dynamicsAccepted);
        Assert.IsTrue(eventsAccepted);
        Assert.IsTrue(dynamicsAccepted);
    }

    [TestMethod]
    public void MissingEntryFilter_DoesNotTreatUnselectedInputAsADeletion()
    {
        Validate(new Owner(true, true), new[] { Handler("Text") }, null, out var accepted,
            new[] { "PropertyEventHandlers.Missing.Change" });
        Assert.IsTrue(accepted);
    }

    [TestMethod]
    public void UnreadableExistingState_FailsClosed()
    {
        var result = Validate(new BrokenOwner(), null, new[] { Dynamic() }, out var accepted);
        Assert.IsFalse(accepted);
        Assert.AreEqual(1, result.PropertyFailedCount);
        StringAssert.Contains(result.FailedProperties[0].Reason, "not enumerable");
    }

    private static HmiJsonImportResult Validate(object? owner,
        IReadOnlyList<HmiJsonPropertyEventHandler>? handlers, IReadOnlyList<HmiJsonDynamization>? dynamics,
        out bool accepted, string[]? properties = null, string scope = "Item")
    {
        var coordinator = new HmiJsonCompositeImportCoordinator(new V21UnifiedHmiAdapter(null));
        var result = new HmiJsonImportResult();
        accepted = coordinator.ValidatePropertyEventDynamizationCombination(scope, "Screen", scope == "Item" ? "Button" : null,
            handlers, dynamics, owner, HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions { Properties = properties ?? Array.Empty<string>() }), result);
        return result;
    }

    private static HmiJsonPropertyEventHandler Handler(string property = "Visible") => new()
    {
        PropertyName = property,
        EventType = "Change",
        Script = new HmiJsonScriptPayload { Async = false, ScriptCode = "return 1;" }
    };

    private static HmiJsonDynamization Dynamic(string property = "Left") => new()
    {
        Property = property,
        Type = "Tag",
        Binding = new HmiJsonDynamizationBinding { Tag = "ExistingTag" }
    };

    private sealed class Owner
    {
        private readonly bool _events, _dynamics;
        public Owner(bool events, bool dynamics) { _events = events; _dynamics = dynamics; }
        public int Reads;
        public object PropertyEventHandlers { get { Reads++; return _events ? new[] { Handler() } : Array.Empty<HmiJsonPropertyEventHandler>(); } }
        public object Dynamizations { get { Reads++; return _dynamics ? new[] { new RawDynamic() } : Array.Empty<RawDynamic>(); } }
    }

    private sealed class RawDynamic
    {
        public string PropertyName => "Left";
        public string DynamizationType => "Tag";
        public string Tag => "ExistingTag";
    }

    private sealed class BrokenOwner { public object PropertyEventHandlers => new object(); }

}
