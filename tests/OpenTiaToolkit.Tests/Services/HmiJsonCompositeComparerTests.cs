using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System.Collections.Generic;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonCompositeComparerTests
    {
        [TestMethod]
        public void CloneDynamizations_ReturnsIndependentSortedDeepCopy()
        {
            var source = new List<HmiJsonDynamization>
            {
                new HmiJsonDynamization
                {
                    Property = "Top",
                    Type = "Script",
                    Script = new HmiJsonScriptDynamization
                    {
                        ScriptCode = "return 1;",
                        Trigger = new HmiJsonScriptTrigger
                        {
                            Type = "Tag",
                            Tags = new List<string> { "TagA" }
                        }
                    }
                },
                new HmiJsonDynamization
                {
                    Property = "Left",
                    Type = "Tag",
                    Binding = new HmiJsonDynamizationBinding
                    {
                        Tag = "TagB"
                    }
                }
            };

            var clone = HmiJsonCompositeComparer.CloneDynamizations(source);

            Assert.IsNotNull(clone);
            Assert.AreEqual("Left", clone![0].Property);
            Assert.AreEqual("Top", clone[1].Property);

            source[0].Script!.Trigger!.Tags![0] = "Changed";
            Assert.AreEqual("TagA", clone[1].Script!.Trigger!.Tags![0]);
        }

        [TestMethod]
        public void GetChangedInterfaceProperties_UsesCaseInsensitiveKeysAndNestedDynamizations()
        {
            var current = new List<HmiJsonInterfaceBinding>
            {
                new HmiJsonInterfaceBinding
                {
                    PropertyName = "AV",
                    Value = "Valve_1.AV",
                    Dynamizations = new List<HmiJsonDynamization>
                    {
                        new HmiJsonDynamization
                        {
                            Property = "Value",
                            Type = "TagParameter",
                            Binding = new HmiJsonDynamizationBinding { DynamicTagName = "Steps[1]" }
                        }
                    }
                }
            };
            var desired = new List<HmiJsonInterfaceBinding>
            {
                new HmiJsonInterfaceBinding
                {
                    PropertyName = "av",
                    Value = "Valve_1.AV",
                    Dynamizations = new List<HmiJsonDynamization>
                    {
                        new HmiJsonDynamization
                        {
                            Property = "Value",
                            Type = "TagParameter",
                            Binding = new HmiJsonDynamizationBinding { DynamicTagName = "Steps[2]" }
                        }
                    }
                }
            };

            var changed = HmiJsonCompositeComparer.GetChangedInterfaceProperties(current, desired);

            CollectionAssert.AreEqual(new[] { "AV" }, changed);
        }
    }
}
