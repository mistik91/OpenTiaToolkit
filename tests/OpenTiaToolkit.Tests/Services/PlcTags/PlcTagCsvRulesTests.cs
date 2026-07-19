using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services.PlcTags;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Services.PlcTags
{
    [TestClass]
    public sealed class PlcTagCsvRulesTests
    {
        [TestMethod]
        public void AllocateTagName_UsesTiaNumericSuffixes()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RESERVE", "RESERVE(1)" };

            var allocated = PlcTagCsvRules.AllocateTagName("reserve", names);

            Assert.AreEqual("reserve(2)", allocated);
        }
    }
}
