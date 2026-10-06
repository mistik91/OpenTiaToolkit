using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Drawing;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class EngineeringStringConverterTests
    {
        private enum SampleMode
        {
            Disabled,
            Enabled
        }

        [TestMethod]
        public void ConvertInvariant_ParsesCommonPrimitiveTypes()
        {
            Assert.AreEqual(true, EngineeringStringConverter.ConvertInvariant("true", typeof(bool)));
            Assert.AreEqual((short)-12, EngineeringStringConverter.ConvertInvariant("-12", typeof(short)));
            Assert.AreEqual(42, EngineeringStringConverter.ConvertInvariant("42", typeof(int)));
            Assert.AreEqual(1.25d, EngineeringStringConverter.ConvertInvariant("1.25", typeof(double)));
            Assert.AreEqual(TimeSpan.FromSeconds(5), EngineeringStringConverter.ConvertInvariant("00:00:05", typeof(TimeSpan)));
        }

        [TestMethod]
        public void ConvertInvariant_ParsesEnumsIgnoringCase()
        {
            var result = EngineeringStringConverter.ConvertInvariant("enabled", typeof(SampleMode));

            Assert.AreEqual(SampleMode.Enabled, result);
        }

        [TestMethod]
        public void ConvertInvariant_UsesCustomConverterBeforeBuiltIns()
        {
            var result = EngineeringStringConverter.ConvertInvariant(
                "named-color",
                typeof(Color),
                (type, value) => type == typeof(Color) ? Color.MediumSeaGreen : null);

            Assert.AreEqual(Color.MediumSeaGreen, result);
        }
    }
}
