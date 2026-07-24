using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.Core.Services.V21;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class Point6BoundaryCharacterizationTests
    {
        [TestMethod]
        public async Task XmlTransfer_PreservesNoOpenPlcProjectionForBothObjectKinds()
        {
            var context = new EngineeringSessionContext();
            var resolver = new V21TypeServiceResolver();
            var service = new PlcXmlTransferService(
                context,
                null,
                new V21DocumentAccess(resolver),
                new V21EngineeringObjectAccess());

            Assert.IsFalse(await service.ExportBlockAsync("Block", "block.xml"));
            Assert.IsFalse(await service.ExportTypeAsync("Type", "type.xml"));
            Assert.IsFalse(await service.ImportBlockAsync("missing.xml"));
            Assert.IsFalse(await service.ImportTypeAsync("missing.xml"));
            CollectionAssert.Contains((await service.ExportTypesAsync(null, ".")).Errors, "No PLC software is currently open");
            CollectionAssert.Contains((await service.ImportBlocksAsync(".", null, null)).Errors, "No PLC software is currently open");
        }

        [TestMethod]
        public async Task DocumentTransfer_PreservesNoOpenPlcProjectionBeforeVersionChecks()
        {
            var context = new EngineeringSessionContext();
            var resolver = new V21TypeServiceResolver();
            var service = new PlcDocumentTransferService(
                context,
                Mock.Of<IPortalService>(portal => portal.Version == "V21"),
                new PlcBrowseService(context, null),
                null,
                new V21DocumentAccess(resolver));

            Assert.IsFalse(await service.ExportBlockAsync("Block", "."));
            Assert.IsFalse(await service.ImportBlockAsync(".", "Block"));
            CollectionAssert.Contains((await service.ExportBlocksAsync(null, ".")).Errors, "No PLC software is currently open");
            CollectionAssert.Contains((await service.ImportBlocksAsync(".")).Errors, "No PLC software is currently open");
        }

        [TestMethod]
        public async Task CrossReferenceOwner_PreservesInputAndSessionErrors()
        {
            var context = new EngineeringSessionContext();
            var objects = new V21EngineeringObjectAccess();
            var service = new PlcCrossReferenceService(
                context,
                new V21TypeServiceResolver(),
                new V21PlcTagStructureAccess(objects),
                objects);

            var missingName = await service.GetAsync(" ");
            Assert.IsFalse(missingName.Success);
            Assert.AreEqual("name is required", missingName.Message);

            var missingSession = await service.GetAsync("MotorRun");
            Assert.IsFalse(missingSession.Success);
            Assert.AreEqual("MotorRun", missingSession.Query);
            Assert.AreEqual("No PLC software is currently open", missingSession.Message);
        }

        [TestMethod]
        public async Task RetainedSurfaceCapabilities_PreserveEmptyAndMutationGuardResults()
        {
            var context = new EngineeringSessionContext();
            var reader = new PlcAncillarySurfaceReader(context);
            var sources = new PlcExternalSourceService(context, new PlcBrowseService(context, null));

            Assert.AreEqual(0, (await reader.ListExternalSourcesAsync()).Count);
            Assert.AreEqual(0, (await reader.ListWatchTablesAsync()).Count);
            Assert.AreEqual(0, (await reader.ListTechnologyObjectsAsync()).Count);
            Assert.AreEqual(0, (await reader.ListPlcAlarmTextListsAsync()).Count);

            var missingPath = await sources.ImportAsync(string.Empty, true, false);
            Assert.IsFalse(missingPath.Success);
            Assert.AreEqual("source-path is required.", missingPath.Message);
            var missingSource = await sources.GenerateBlocksAsync("Missing", true, false);
            Assert.IsFalse(missingSource.Success);
            StringAssert.Contains(missingSource.Message, "was not found");
        }

        [TestMethod]
        public void TransferRegex_PreservesInvalidFilterWording()
        {
            var errors = new System.Collections.Generic.List<string>();
            Assert.IsNull(PlcTransferRules.TryCreateRegex("[", errors));
            CollectionAssert.AreEqual(new[] { "Invalid regex filter: [" }, errors.ToArray());
        }
    }
}
