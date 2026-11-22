using System.Collections.Generic;
using System.Linq;
using OpenTiaToolkit.Core.Services.AutomationMl;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public class AutomationMlAdditivePlannerTests
{
    [TestMethod]
    public void PlansNewStationAndTagUsingExistingNetworkWithoutSchedulingReplacement()
    {
        var current = Existing();
        var source = Desired(current);
        var plan = AmlAdditivePlanner.Create(source, current, AmlComparer.Compare(source, current));

        Assert.IsTrue(plan.CanApply);
        var station = plan.Stations.Single();
        Assert.AreEqual("Remote", station.Name);
        Assert.AreEqual("PN", station.Subnet);
        Assert.AreEqual("IO", station.IoSystem);
        Assert.AreEqual("PLC", station.Controller);
        Assert.AreEqual(20, station.Modules.Single().Addresses.Single().Start);
        Assert.AreEqual("%I20.0", plan.Tags.Single().Address);
        Assert.AreEqual(0, plan.Conflicts.Count);
    }

    [TestMethod]
    [DataRow("name", "tag name or address")]
    [DataRow("address", "tag name or address")]
    [DataRow("hardware range", "module I/O range")]
    [DataRow("ip", "IP address")]
    public void BlocksConflictingAdditions(string conflict, string message)
    {
        var baseline = Existing();
        var extra = conflict switch
        {
            "name" => Obj("Device:PLC/TagTable:Default/Tag:Alias", "Tag", "remoteready", ("LogicalAddress", "I22.0")),
            "address" => Obj("Device:PLC/TagTable:Default/Tag:Existing", "Tag", "Existing", ("LogicalAddress", "IW20")),
            "hardware range" => Obj("Device:PLC/DeviceItem:Slot1", "DeviceItem", "Inputs",
                ("Address/1/IoType", "Input"), ("Address/1/StartAddress", "20"), ("Address/1/Length", "8")),
            _ => Obj("Device:PLC/Node:Existing", "Node", "Existing", ("NetworkAddress", "192.168.0.20"))
        };
        var current = Model(baseline.Objects.Concat(new[] { extra }));
        var source = Desired(baseline);
        var plan = AmlAdditivePlanner.Create(source, current, AmlComparer.Compare(source, current));

        Assert.IsFalse(plan.CanApply);
        Assert.IsTrue(plan.Conflicts.Any(item => item.Actual?.Contains(message) == true));
    }

    [TestMethod]
    public void BlocksChangesToAnExistingHardwareSlotEvenWithAnIndependentAddition()
    {
        var baseline = Existing();
        const string key = "Device:PLC/DeviceItem:Slot1";
        var existingModule = Obj(key, "DeviceItem", "Existing", ("TypeIdentifier", "OrderNumber:6ES7 521-0AA00-0AA0"));
        var changedModule = Obj(key, "DeviceItem", "Existing", ("TypeIdentifier", "OrderNumber:6ES7 521-0AB00-0AA0"));
        var current = Model(baseline.Objects.Concat(new[] { existingModule }));
        var desired = Desired(Model(baseline.Objects.Concat(new[] { changedModule })));
        var plan = AmlAdditivePlanner.Create(desired, current, AmlComparer.Compare(desired, current));

        Assert.IsFalse(plan.CanApply);
        Assert.IsTrue(plan.Conflicts.Any(item => item.Change == "DifferentProperty" && item.Target == key));
    }

    [TestMethod]
    [DataRow("independent local", true)]
    [DataRow("independent remote", true)]
    [DataRow("same PLC remote", false)]
    [DataRow("ambiguous remote", false)]
    [DataRow("unassigned remote", false)]
    public void ScopesHardwareRangesToKnownPlcOwners(string ownership, bool allowed)
    {
        const string other = "Device:Other";
        const string otherSystem = other + "/IoSystem:OtherIo";
        const string installed = "Device:Installed";
        const string nic = installed + "/CommunicationInterface:Port";
        const string endpoint = "/LogicalEndPoint:";
        var baseline = Existing();
        var linked = new List<AmlConnection>();
        if (ownership is "independent remote" or "ambiguous remote")
            linked.Add(new AmlConnection("NetworkConnection", nic + endpoint, otherSystem + endpoint));
        if (ownership is "same PLC remote" or "ambiguous remote")
            linked.Add(new AmlConnection("NetworkConnection", "Device:PLC/IoSystem:IO" + endpoint, nic + endpoint));
        var rangeRoot = ownership == "independent local" ? other : installed;
        var current = Model(baseline.Objects.Concat(new[]
        {
            Obj(other, "Device", "Other"),
            Obj(otherSystem, "IoSystem", "OtherIo"),
            Obj(installed, "Device", "Installed"),
            Obj(nic, "CommunicationInterface", "Port"),
            Obj(rangeRoot + "/DeviceItem:Slot1", "DeviceItem", "Inputs",
                ("Address/1/IoType", "Input"), ("Address/1/StartAddress", "20"), ("Address/1/Length", "8"))
        }), linked.ToArray());
        var desired = Desired(baseline);
        var plan = AmlAdditivePlanner.Create(desired, current, AmlComparer.Compare(desired, current));

        Assert.AreEqual(allowed, plan.CanApply);
        Assert.AreEqual(!allowed, plan.Conflicts.Any(item => item.Actual?.Contains("module I/O range") == true));
    }

    private static AmlModel Existing() => Model(new[]
    {
        Obj("Device:PLC", "Device", "PLC"),
        Obj("Device:PLC/TagTable:Default", "TagTable", "Default"),
        Obj("Device:PLC/IoSystem:IO", "IoSystem", "IO"),
        Obj("Subnet:PN", "Subnet", "PN")
    });

    private static AmlModel Desired(AmlModel baseline)
    {
        const string device = "Device:Remote";
        const string rack = device + "/DeviceItem:Rack";
        const string head = rack + "/DeviceItem:Head";
        const string module = rack + "/DeviceItem:IO";
        const string port = head + "/CommunicationInterface:PN";
        const string node = port + "/Node:Node";
        const string endpoint = "/LogicalEndPoint:";
        return Model(baseline.Objects.Concat(new[]
        {
            Obj(device, "Device", "Remote", ("TypeIdentifier", "System:Device.ET200SP")),
            Obj(rack, "DeviceItem", "Rack", ("PositionNumber", "0")),
            Obj(head, "DeviceItem", "Head", ("PositionNumber", "0"), ("TypeIdentifier", "OrderNumber:6ES7 155-6AU02-0BN0")),
            Obj(module, "DeviceItem", "IO", ("PositionNumber", "1"), ("TypeIdentifier", "OrderNumber:6ES7 131-6BF01-0BA0"),
                ("Address/1/StartAddress", "20"), ("Address/1/IoType", "Input"), ("Address/1/Length", "8")),
            Obj(port, "CommunicationInterface", "Port", ("Label", "X1")),
            Obj(node, "Node", "Node", ("NetworkAddress", "192.168.0.20")),
            Obj("Device:PLC/TagTable:Default/Tag:RemoteReady", "Tag", "RemoteReady", ("LogicalAddress", "I20.0"), ("DataType", "Bool"))
        }), new[]
        {
            new AmlConnection("NetworkConnection", "Device:PLC/IoSystem:IO" + endpoint, port + endpoint),
            new AmlConnection("NetworkConnection", node + endpoint, "Subnet:PN" + endpoint)
        });
    }

    private static AmlObject Obj(string key, string kind, string name, params (string Name, string Value)[] properties) =>
        new(key, kind, name, properties.ToDictionary(property => property.Name, property => property.Value));

    private static AmlModel Model(IEnumerable<AmlObject> objects, AmlConnection[]? links = null) =>
        new("1.4.0", objects.ToArray(), links ?? System.Array.Empty<AmlConnection>(), System.Array.Empty<string>());
}
