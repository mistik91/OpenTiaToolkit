using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Hardware;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Composition
{
    [TestClass]
    public class CapabilityBoundaryTests
    {
        [TestMethod]
        public void HardwareCommands_DependsOnQueryOrPatchCapabilityNotBroadService()
        {
            Type[] queryCommands =
            {
                typeof(FindHardwareCatalogItemsCommand), typeof(GetHardwareTreeCommand),
                typeof(GetHardwareIoAddressesCommand), typeof(GetHardwareNetworksCommand)
            };
            foreach (Type type in queryCommands)
            {
                Type parameter = type.GetConstructors().Single().GetParameters()[0].ParameterType;
                Assert.AreEqual(typeof(IHardwareQueryCapability), parameter, type.Name);
            }

            Type patchParameter = typeof(ApplyHardwarePatchCommand).GetConstructors().Single()
                .GetParameters()[0].ParameterType;
            Assert.AreEqual(typeof(IHardwarePatchCapability), patchParameter);
        }

        [TestMethod]
        public void HmiFacade_ComposesDedicatedCapabilityServices()
        {
            Type[] fieldTypes = typeof(HmiService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(field => field.FieldType).ToArray();
            foreach (Type capability in new[]
            {
                typeof(HmiBrowseService), typeof(HmiMutationService), typeof(HmiCopyService),
                typeof(HmiTrendService), typeof(HmiJsonExportService), typeof(HmiJsonImportService)
            })
                CollectionAssert.Contains(fieldTypes, capability);
        }

        [TestMethod]
        public void JsonPatchAndTagPlanning_AreExtractedCapabilities()
        {
            Assert.IsNotNull(typeof(HmiPatchSuggestionService));
            Assert.IsNotNull(typeof(PlcTagAddressPlanner));
        }
    }
}
