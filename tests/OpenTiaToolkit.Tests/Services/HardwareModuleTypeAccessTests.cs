using System;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public class HardwareModuleTypeAccessTests
{
    public sealed class RejectedProvider
    {
        public string[] FindModuleTypes(string partial) => throw new ArgumentException("Unknown module identifier: " + partial);
    }

    public sealed class AvailableProvider
    {
        public string[] FindModuleTypes(string partial) => new[] { partial + "/module" };
    }

    [TestMethod]
    public void RejectedLookup_ExposesOriginalNestedCause()
    {
        var error = Assert.ThrowsException<ArgumentException>(() =>
            V21HardwareModuleTypeAccess.ReadModuleTypes(new RejectedProvider(), "OrderNumber:6ES7 214"));
        StringAssert.Contains(error.Message, "Unknown module identifier: OrderNumber:6ES7 214");
    }

    [TestMethod]
    public void AvailableLookup_RetainsModuleIdentifiers()
    {
        CollectionAssert.AreEqual(new[] { "GSD:example/module" },
            V21HardwareModuleTypeAccess.ReadModuleTypes(new AvailableProvider(), "GSD:example"));
    }
}
