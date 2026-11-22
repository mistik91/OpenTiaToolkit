using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using OpenTiaToolkit.Core.Services.AutomationMl;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public class AutomationMlTests
{
    private const string Fixture = """
        <CAEXFile xmlns="urn:example:caex">
          <AdditionalInformation><Document DocumentIdentifier="AR APC" Version="1.4.0"/></AdditionalInformation>
          <InstanceHierarchy Name="APC">
            <InternalElement Name="Assembly">
              <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/AutomationProject"/>
              <InternalElement Name="Controller">
                <RoleRequirements RefBaseRoleClassPath="AutomationProjectConfigurationRoleClassLib/Device"/>
                <InternalElement Name="IO" ID="io">
                  <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/DeviceItem"/>
                  <Attribute Name="PositionNumber"><Value>2</Value></Attribute>
                  <Attribute Name="TypeIdentifier"><Value>OrderNumber:6ES7 000-0XX00-0AA0</Value></Attribute>
                  <ExternalInterface Name="Channel1" ID="channel" RefBaseClassPath="AutomationProjectConfigurationInterfaceClassLib/Channel">
                    <Attribute Name="IoType"><Value>Input</Value></Attribute>
                    <Attribute Name="Number"><Value>1</Value></Attribute>
                  </ExternalInterface>
                  <InternalElement Name="Table" ID="table">
                    <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/TagTable"/>
                    <ExternalInterface Name="SampleTag" ID="tag" RefBaseClassPath="AutomationProjectConfigurationInterfaceClassLib/Tag">
                      <Attribute Name="LogicalAddress"><Value>I0.1</Value></Attribute>
                      <Attribute Name="IoType"><Value>Input</Value></Attribute>
                      <Attribute Name="DataType"><Value>Bool</Value></Attribute>
                    </ExternalInterface>
                  </InternalElement>
                  <InternalElement Name="PN">
                    <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/CommunicationInterface"/>
                    <InternalElement Name="Node" ID="node">
                      <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/Node"/>
                      <Attribute Name="NetworkAddress"><Value>192.168.0.1</Value></Attribute>
                      <ExternalInterface Name="Endpoint" RefBaseClassPath="CommunicationInterfaceClassLib/LogicalEndPoint"/>
                    </InternalElement>
                  </InternalElement>
                </InternalElement>
              </InternalElement>
              <InternalElement Name="PN Network" ID="subnet">
                <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/Subnet"/>
                <ExternalInterface Name="Endpoint" RefBaseClassPath="CommunicationInterfaceClassLib/LogicalEndPoint"/>
              </InternalElement>
              <InternalLink Name="channel to tag" RefPartnerSideA="io:Channel1" RefPartnerSideB="tag"/>
              <InternalLink Name="node to subnet" RefPartnerSideA="node:Endpoint" RefPartnerSideB="subnet:Endpoint"/>
            </InternalElement>
          </InstanceHierarchy>
        </CAEXFile>
        """;

    [TestMethod]
    public void Read_NormalizesHardwareNetworkAndTagRelationships()
    {
        var model = Read(Fixture);
        Assert.AreEqual("1.4.0", model.ApcVersion);
        Assert.AreEqual(8, model.Objects.Count);
        Assert.AreEqual(0, model.Diagnostics.Count);
        Assert.AreEqual("I0.1", model.Objects.Single(item => item.Kind == "Tag").Properties["LogicalAddress"]);
        CollectionAssert.AreEquivalent(new[] { "ChannelTag", "NetworkConnection" },
            model.Connections.Select(link => link.Kind).ToArray());
    }

    [TestMethod]
    public void Compare_PlansMissingConfigurationAndDetectsAddressAndTopologyChanges()
    {
        var desired = Read(Fixture);
        var creation = AmlComparer.Compare(desired, AmlModel.Empty);
        Assert.AreEqual(10, creation.Differences.Count);

        var existing = Read(Fixture.Replace("192.168.0.1", "192.168.0.2").Replace("I0.1", "0.2").Replace("<InternalLink Name=\"node to subnet\"", "<Ignored Name=\"node to subnet\""));
        var plan = AmlComparer.Compare(desired, existing);
        Assert.IsFalse(plan.Matches);
        Assert.AreEqual("0.2", plan.Differences.Single(change => change.Property == "LogicalAddress").Actual);
        Assert.AreEqual("192.168.0.2", plan.Differences.Single(change => change.Property == "NetworkAddress").Actual);
        Assert.IsTrue(plan.Differences.Any(change => change.Change == "MissingNetworkConnection"));
    }

    [TestMethod]
    public void Compare_MatchingStateWithRegeneratedIdsAndCatalogDefaultsIsANoOp()
    {
        var snapshot = Fixture.Replace("Assembly", "Other name")
            .Replace("ID=\"io\"", "ID=\"regenerated\"").Replace("io:Channel1", "regenerated:Channel1")
            .Replace("6ES7 000-0XX00-0AA0", "6ES7000-0XX00-0AA0/V1.0").Replace("I0.1", "0.1");
        Assert.IsTrue(AmlComparer.Compare(Read(Fixture), Read(snapshot)).Matches);
    }

    [TestMethod]
    public void Compare_ReportsCatalogMasksSeparatelyFromConflictingOrderNumbers()
    {
        const string sourceOrderNumber = "6ES7 000-0XX00-0AA0";
        var source = Read(Fixture);
        var compatibleMask = Read(Fixture.Replace(sourceOrderNumber, "6ES7000-0***0-0AA0"));
        var plan = AmlComparer.Compare(source, compatibleMask);
        Assert.IsTrue(plan.Matches);
        Assert.IsFalse(plan.FullyVerified);
        Assert.AreEqual(1, plan.UnverifiedCatalogIdentifiers.Count);

        var incompatibleMask = Read(Fixture.Replace(sourceOrderNumber, "6ES7001-0***0-0AA0"));
        var conflict = AmlComparer.Compare(source, incompatibleMask);
        Assert.IsFalse(conflict.Matches);
        Assert.AreEqual(0, conflict.UnverifiedCatalogIdentifiers.Count);
    }

    [TestMethod]
    [DataRow("%IB8", "B8")]
    [DataRow("IW8", "W8")]
    [DataRow("ID8", "D8")]
    public void Compare_RecombinesTagAddressWidthWithTheDirection(string sourceAddress, string readback)
    {
        var source = Read(Fixture.Replace("I0.1", sourceAddress));
        var target = Fixture.Replace("I0.1", readback);
        Assert.IsTrue(AmlComparer.Compare(source, Read(target)).Matches);
        Assert.IsFalse(AmlComparer.Compare(source, Read(target.Replace(readback, "W9"))).Matches);
    }

    [TestMethod]
    public void Read_RejectsDtdAndReportsDanglingLinks()
    {
        Assert.ThrowsException<XmlException>(() => Read("<!DOCTYPE CAEXFile [<!ENTITY value 'test'>]>" + Fixture));
        Assert.ThrowsException<InvalidDataException>(() => Read("<unrelated/>"));
        var broken = Read(Fixture.Replace("io:Channel1", "other:Channel1"));
        Assert.AreEqual(1, broken.Diagnostics.Count);
        Assert.IsFalse(AmlComparer.Compare(broken, broken).Matches);
    }

    [TestMethod]
    public void Read_ReportsDuplicateInternalIdsEvenWhenObjectPathsDiffer()
    {
        var xml = Fixture.Replace("ID=\"node\"", "ID=\"IO\"").Replace("node:Endpoint", "IO:Endpoint");
        var model = Read(xml);
        Assert.AreEqual(1, model.Diagnostics.Count);
        StringAssert.Contains(model.Diagnostics[0], "Duplicate internal element ID");
        Assert.IsFalse(AmlComparer.Compare(model, model).Matches);
    }

    [TestMethod]
    public void Read_ResolvesBracketedReferencesAndEquivalentChannelAliases()
    {
        var xml = new XmlDocument();
        xml.LoadXml(Fixture.Replace("urn:example:caex", ""));
        var channel = (XmlElement)xml.SelectSingleNode("//ExternalInterface[@ID='channel']")!;
        var alias = (XmlElement)channel.CloneNode(deep: true);
        alias.SetAttribute("ID", "second-channel-id");
        channel.ParentNode!.AppendChild(alias);
        var tag = (XmlElement)xml.SelectSingleNode("//ExternalInterface[@ID='tag']")!;
        tag.SetAttribute("Name", "Symbol:1");
        var link = (XmlElement)xml.SelectSingleNode("//InternalLink[@Name='channel to tag']")!;
        link.SetAttribute("RefPartnerSideA", "second-channel-id");
        link.SetAttribute("RefPartnerSideB", "[table]:[Symbol:1]");

        var model = Read(xml.OuterXml);
        Assert.AreEqual(0, model.Diagnostics.Count);
        Assert.AreEqual(1, model.Objects.Count(item => item.Kind == "Channel"));
        Assert.AreEqual(1, model.Connections.Count(item => item.Kind == "ChannelTag"));

        alias.SelectSingleNode("Attribute[@Name='Number']/Value")!.InnerText = "2";
        Assert.IsTrue(Read(xml.OuterXml).Diagnostics.Any(item => item.StartsWith("Duplicate interface reference")));
    }

    [TestMethod]
    public void Read_DistinguishesRepeatedPortLabelsByPosition()
    {
        const string ports = """
            <InternalElement Name="Port_1">
              <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/CommunicationPort"/>
              <Attribute Name="Label"><Value>P1 R</Value></Attribute>
              <Attribute Name="PositionNumber"><Value>2</Value></Attribute>
            </InternalElement>
            <InternalElement Name="Port_2">
              <SupportedRoleClass RefRoleClassPath="AutomationProjectConfigurationRoleClassLib/CommunicationPort"/>
              <Attribute Name="Label"><Value>P1 R</Value></Attribute>
              <Attribute Name="PositionNumber"><Value>4</Value></Attribute>
            </InternalElement>
            """;
        var model = Read(Fixture.Replace("<InternalElement Name=\"Node\" ID=\"node\">",
            ports + "<InternalElement Name=\"Node\" ID=\"node\">"));

        Assert.AreEqual(0, model.Diagnostics.Count);
        Assert.AreEqual(2, model.Objects.Count(item => item.Kind == "CommunicationPort"));
    }

    private static AmlModel Read(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return AmlReader.Read(stream);
    }
}
