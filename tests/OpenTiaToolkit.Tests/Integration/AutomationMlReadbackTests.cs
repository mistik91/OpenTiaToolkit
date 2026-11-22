using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OpenTiaToolkit.Core.Services.AutomationMl;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public class AutomationMlReadbackTests
{
    [TestMethod]
    public void SiemensFixture_ReadbackMatchesAndReportsCatalogSpecificity()
    {
        var (source, readback) = LoadPair();
        var plan = AmlComparer.Compare(source, Read(readback));
        Assert.IsTrue(plan.Matches, Describe(plan));
        Assert.IsFalse(plan.FullyVerified, "The V21 sample readback exposes the mounting rails as a wildcard order number.");
        Assert.AreEqual(2, plan.UnverifiedCatalogIdentifiers.Count);
    }

    [TestMethod]
    [DataRow("tag address", "DifferentProperty", "LogicalAddress")]
    [DataRow("module slot", "MissingObject", null)]
    [DataRow("port link", "MissingPortConnection", null)]
    [DataRow("ambiguous port", "AmbiguousObject", null)]
    public void SiemensFixture_DetectsRealChanges(string change, string difference, string? property)
    {
        var (source, readback) = LoadPair();
        var ns = readback.Root!.Name.Namespace;
        var hierarchy = readback.Root.Element(ns + "InstanceHierarchy")!;
        var tag = hierarchy.Descendants(ns + "ExternalInterface")
            .First(element => ((string?)element.Attribute("RefBaseClassPath"))?.EndsWith("/Tag", StringComparison.Ordinal) == true);
        var module = hierarchy.Descendants(ns + "InternalElement").First(element =>
            Attribute(element, "TypeIdentifier")?.Value.Replace(" ", string.Empty).StartsWith("OrderNumber:6ES7521-", StringComparison.OrdinalIgnoreCase) == true);

        switch (change)
        {
            case "tag address": Attribute(tag, "LogicalAddress")!.Value = "200.0"; break;
            case "module slot": Attribute(module, "PositionNumber")!.Value = "200"; break;
            case "port link":
                hierarchy.Descendants(ns + "InternalLink")
                    .First(link => ((string?)link.Attribute("Name"))?.StartsWith("Link To Port", StringComparison.Ordinal) == true)
                    .Remove();
                break;
            case "ambiguous port":
                var port = hierarchy.Descendants(ns + "InternalElement")
                    .First(element => Attribute(element, "Label")?.Value == "P1R");
                var duplicate = new XElement(port);
                duplicate.SetAttributeValue("ID", "synthetic-duplicate-port");
                Attribute(duplicate, "PositionNumber")!.Value = "250";
                duplicate.Elements(ns + "ExternalInterface").Remove();
                port.AddAfterSelf(duplicate);
                break;
        }

        var plan = AmlComparer.Compare(source, Read(readback));
        Assert.IsFalse(plan.Matches);
        Assert.IsTrue(plan.Differences.Any(item => item.Change == difference && item.Property == property), Describe(plan));
    }

    private static (AmlModel Source, XDocument Readback) LoadPair()
    {
        var source = Environment.GetEnvironmentVariable("OPEN_TIA_AUTOMATIONML_FIXTURE");
        var readback = Environment.GetEnvironmentVariable("OPEN_TIA_AUTOMATIONML_READBACK");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(readback))
            Assert.Inconclusive("Set the Siemens 109748224 EPLAN fixture and its V21 CAx readback paths.");

        using var input = File.OpenRead(source!);
        return (AmlReader.Read(input), XDocument.Load(readback!));
    }

    private static XElement? Attribute(XElement element, string name) =>
        element.Elements(element.Name.Namespace + "Attribute").FirstOrDefault(item => (string?)item.Attribute("Name") == name)
            ?.Element(element.Name.Namespace + "Value");

    private static AmlModel Read(XDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        stream.Position = 0;
        return AmlReader.Read(stream, projectSnapshot: true);
    }

    private static string Describe(AmlPlan plan) => string.Join(Environment.NewLine,
        plan.Differences.Select(diff => $"{diff.Change} {diff.Target} {diff.Property} [{diff.Expected}] [{diff.Actual}]")
            .Concat(plan.SourceDiagnostics).Concat(plan.ProjectDiagnostics));
}
