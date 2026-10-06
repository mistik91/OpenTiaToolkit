using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PlcTagAddressPlannerTests
    {
        [DataTestMethod]
        [DataRow("%I0.0", "Bool")]
        [DataRow("%QB12", "Byte")]
        [DataRow("%MW20", "Word")]
        [DataRow("%ID4", "DWord")]
        public void InferDataType_UsesAddressShape(string address, string expected)
        {
            Assert.AreEqual(expected, PlcTagAddressPlanner.InferDataType(address));
        }

        [TestMethod]
        public void TryParse_ReturnsAreaAndByte()
        {
            PlcTagAddressLocation? location = PlcTagAddressPlanner.TryParse("%QW128");
            Assert.IsTrue(location.HasValue);
            Assert.AreEqual("Q", location.Value.Area);
            Assert.AreEqual(128, location.Value.ByteNumber);
        }
    }
}
