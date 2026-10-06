using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class TiaReflectionHelperTests
    {
        [TestMethod]
        public void SetProperty_ThrowsWhenPropertyIsMissing()
        {
            Assert.ThrowsException<MissingMemberException>(() =>
                TiaReflectionHelper.SetProperty(new WritableValue(), "Missing", "value"));
        }

        [TestMethod]
        public void SetProperty_ThrowsWhenSetterDoesNotRetainValue()
        {
            var target = new IgnoredValue();

            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                TiaReflectionHelper.SetProperty(target, nameof(IgnoredValue.Value), "requested"));

            StringAssert.Contains(exception.Message, "did not retain");
        }

        [TestMethod]
        public void SetProperty_WritesAndVerifiesValue()
        {
            var target = new WritableValue();

            TiaReflectionHelper.SetProperty(target, nameof(WritableValue.Value), "updated");

            Assert.AreEqual("updated", target.Value);
        }

        [TestMethod]
        public void GetMultilingualText_ReturnsAllDistinctLocalizedValues()
        {
            var multilingual = new MultilingualValue
            {
                new LocalizedValue { Text = "English" },
                new LocalizedValue { Text = "Italiano" },
                new LocalizedValue { Text = "English" }
            };

            var text = TiaReflectionHelper.GetMultilingualText(multilingual);

            Assert.AreEqual("English | Italiano", text);
        }

        private sealed class WritableValue
        {
            public string Value { get; set; } = string.Empty;
        }

        private sealed class IgnoredValue
        {
            public string Value
            {
                get => "unchanged";
                set { }
            }
        }

        private sealed class MultilingualValue : List<LocalizedValue>
        {
        }

        private sealed class LocalizedValue
        {
            public string Text { get; set; } = string.Empty;
        }
    }
}
