using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiScreenItemCreationPipelineTests
    {
        [TestMethod]
        public void SelectCreationProperties_UsesDescriptorOrderAndCaseInsensitiveInput()
        {
            var selected = HmiScreenItemCreationPipeline.SelectCreationProperties(
                new[] { "ContainedType", "FutureDependency" },
                new Dictionary<string, string?>
                {
                    ["futuredependency"] = "second",
                    ["containedtype"] = @"V0.0.1\Slider_UNIFIED",
                    ["Left"] = "10"
                });

            CollectionAssert.AreEqual(
                new[] { "ContainedType", "FutureDependency" },
                selected.Select(property => property.Key).ToArray());
            CollectionAssert.AreEqual(
                new[] { @"V0.0.1\Slider_UNIFIED", "second" },
                selected.Select(property => property.Value).ToArray());
        }

        [TestMethod]
        public void SelectCreationProperties_ExcludesNullAndOrdinaryProperties()
        {
            var selected = HmiScreenItemCreationPipeline.SelectCreationProperties(
                new[] { "ContainedType" },
                new Dictionary<string, string?>
                {
                    ["ContainedType"] = null,
                    ["Left"] = "10"
                });

            Assert.AreEqual(0, selected.Count);
        }

        [TestMethod]
        public void FaceplateDescriptor_DeclaresContainedTypeCreationDependency()
        {
            var descriptor = V21HmiEngineeringPrimitives
                .BuildItemTypeMap()
                .Values
                .Distinct()
                .Single(candidate => candidate.TypeName.Equals("HmiFaceplateContainer", StringComparison.Ordinal));

            CollectionAssert.AreEqual(new[] { "ContainedType" }, descriptor.CreationPropertyNames);
        }
    }
}
