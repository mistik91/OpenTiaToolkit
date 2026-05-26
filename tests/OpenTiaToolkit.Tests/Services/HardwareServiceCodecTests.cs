using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HardwareServiceCodecTests
    {
        [TestMethod]
        public void ParseHex_AcceptsReadableByteFormatting()
        {
            var result = HardwareService.ParseHex("0x01 02-0A,FF");

            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x0A, 0xFF }, result);
            Assert.AreEqual("01 02 0A FF", HardwareService.ToHex(result));
        }

        [TestMethod]
        public void ParseHex_RejectsPartialBytes()
        {
            Assert.ThrowsException<FormatException>(() => HardwareService.ParseHex("0A F"));
        }
    }
}
