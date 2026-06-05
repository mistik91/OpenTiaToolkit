using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;

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

        [TestMethod]
        public void GetSelectedEngineeringAttributeInfos_ReadsSupportedAttributesAndSkipsUnsupported()
        {
            var target = new FakeEngineeringObject(new Dictionary<string, object>
            {
                ["Address"] = "192.168.102.45",
                ["PnDeviceNumber"] = 45
            });

            var result = V21HardwareAdapter.GetSelectedEngineeringAttributeInfos(
                target,
                new[] { "Address", "PnDeviceNumber", "Missing" });

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Address", result[0].Name);
            Assert.AreEqual("192.168.102.45", result[0].Value);
            Assert.AreEqual("PnDeviceNumber", result[1].Name);
            Assert.AreEqual("45", result[1].Value);
        }

        private sealed class FakeEngineeringObject
        {
            private readonly IReadOnlyDictionary<string, object> _attributes;

            public FakeEngineeringObject(IReadOnlyDictionary<string, object> attributes)
            {
                _attributes = attributes;
            }

            public object GetAttribute(string name)
            {
                if (_attributes.TryGetValue(name, out var value))
                {
                    return value;
                }

                throw new InvalidOperationException("Unsupported attribute.");
            }
        }
    }
}
