using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonFilterMatcherTests
    {
        [TestMethod]
        public void Normalize_TrimsDeduplicatesAndNormalizesFilters()
        {
            var filters = HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions
            {
                Screens = new[] { " Screens\\Main ", "Screens/Main", "" },
                ItemNames = new[] { " Button_1 ", "button_1" },
                Properties = new[] { " Text ", "text" }
            });

            Assert.AreEqual(1, filters.Screens.Count);
            Assert.AreEqual("Screens/Main", filters.Screens[0]);
            Assert.AreEqual(1, filters.ItemNames.Count);
            Assert.AreEqual("button_1", filters.ItemNames[0]);
            Assert.AreEqual(1, filters.Properties.Count);
            Assert.AreEqual("Text", filters.Properties[0]);
        }

        [TestMethod]
        public void ShouldIncludeCompositeEntry_MatchesRootEntryAndNestedFilters()
        {
            var rootFilter = HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions
            {
                Properties = new[] { "Dynamizations" }
            });
            var entryFilter = HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions
            {
                Properties = new[] { "Dynamizations.Left.Binding.Tag" }
            });
            var unrelatedFilter = HmiJsonFilterMatcher.Normalize(new HmiJsonFilterOptions
            {
                Properties = new[] { "EventHandlers.Click" }
            });

            Assert.IsTrue(HmiJsonFilterMatcher.ShouldIncludeCompositeEntry("Dynamizations", "Left", rootFilter));
            Assert.IsTrue(HmiJsonFilterMatcher.ShouldIncludeCompositeEntry("Dynamizations", "Left", entryFilter));
            Assert.IsFalse(HmiJsonFilterMatcher.ShouldIncludeCompositeEntry("Dynamizations", "Left", unrelatedFilter));
        }
    }
}
