using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class V21DiscreteAlarmSnapshotMappingTests
{
    [TestMethod]
    public void BatchedAttributes_PreserveEveryScalarFieldAndParameterOrder()
    {
        var expected = new Dictionary<string, object>
        {
            ["Name"] = "Alarm",
            ["Id"] = (uint)1234,
            ["AlarmClass"] = "Error",
            ["Priority"] = (byte)17,
            ["Origin"] = "Origin",
            ["Area"] = "Area",
            ["AuditClass"] = "Audit",
            ["RaisedStateTag"] = "Raised",
            ["RaisedStateTagBitNumber"] = (uint)3,
            ["TriggerMode"] = "OnFallingEdge",
            ["TriggerBitAddress"] = "Trigger",
            ["AcknowledgmentStateTag"] = "AckState",
            ["AcknowledgmentStateTagBitNumber"] = 4,
            ["AcknowledgmentControlTag"] = "AckControl",
            ["AcknowledgmentControlTagBitNumber"] = 5,
            ["AlarmParameterTags"] = new[] { "Second", "First" }
        };

        var snapshot = V21HmiDiscreteAlarmAccess.MapScalarAttributes(Values(expected));

        foreach (var entry in expected.Where(e => e.Key != "AlarmParameterTags"))
            Assert.AreEqual(entry.Value, snapshot.GetType().GetProperty(entry.Key)!.GetValue(snapshot), entry.Key);
        CollectionAssert.AreEqual(new[] { "Second", "First" }, snapshot.AlarmParameterTags.ToArray());
    }

    [TestMethod]
    public void MissingOptionalStringsAndParameters_RetainEmptyDefaults()
    {
        var values = new object[16];
        values[1] = (uint)1;
        values[3] = (byte)1;
        values[8] = (uint)0;
        values[9] = "OnRisingEdge";
        values[12] = -1;
        values[14] = -1;
        var snapshot = V21HmiDiscreteAlarmAccess.MapScalarAttributes(values);
        Assert.AreEqual("", snapshot.Origin);
        Assert.AreEqual("", snapshot.AcknowledgmentControlTag);
        Assert.AreEqual(0, snapshot.AlarmParameterTags.Count);
        Assert.AreEqual(-1, snapshot.AcknowledgmentStateTagBitNumber);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(15)]
    [DataRow(17)]
    public void UnexpectedNativeShape_FailsClosed(int count) =>
        Assert.ThrowsException<InvalidOperationException>(() => V21HmiDiscreteAlarmAccess.MapScalarAttributes(new object[count]));

    private static object[] Values(IReadOnlyDictionary<string, object> values) =>
        V21HmiDiscreteAlarmAccess.SnapshotScalarAttributes.Select(name => values[name]).ToArray();
}
