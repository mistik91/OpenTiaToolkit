using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.Core.Services.Hardware.Patching;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class EngineeringFailureTests
    {
        [TestMethod]
        public void SessionLoss_DetectsNestedDisposedSiemensObjectAndRetainsVendorCause()
        {
            var exception = new TargetInvocationException(new ObjectDisposedException("Siemens.Engineering.Project", "Project is disposed"));
            Assert.IsTrue(EngineeringFailure.IsSessionLoss(exception));
            Assert.IsTrue(EngineeringFailure.IsSessionLoss(new AggregateException(new InvalidOperationException("Rollback failed"), exception)));
            StringAssert.Contains(EngineeringFailure.Describe(exception), "Project is disposed");
            Assert.IsFalse(EngineeringFailure.Describe(exception).Contains("Exception has been thrown by the target"));
            Assert.IsFalse(EngineeringFailure.IsSessionLoss(new ObjectDisposedException("FileStream")));
            Assert.IsFalse(EngineeringFailure.IsSessionLoss(new InvalidOperationException("Device not found")));
        }

        [DataTestMethod]
        [DataRow("Siemens.Engineering.NonRecoverableException", true)]
        [DataRow("Siemens.Engineering.EngineeringObjectDisposedException", true)]
        [DataRow("Siemens.Engineering.EngineeringException", false)]
        public void SessionLoss_RecognizesFatalVendorTypesWithoutMessageHeuristics(string type, bool fatal) =>
            Assert.AreEqual(fatal, EngineeringFailure.IsSessionLossType(type));

        [TestMethod]
        public async Task LostSession_QueriesFailExplicitlyUntilSelectionIsReset()
        {
            var context = new EngineeringSessionContext { CurrentPlcName = "PLC", CurrentHmiName = "HMI" };
            var types = new V21TypeServiceResolver();
            var inspection = new PortalInspectionService(context, null, types, new V21SoftwareDiscovery(types));
            var failure = context.RecordReadFailure(new TargetInvocationException(new ObjectDisposedException("Siemens.Engineering.Project")));
            Assert.IsInstanceOfType<EngineeringSessionUnavailableException>(failure);
            Assert.IsNull(context.CurrentPlcName);
            Assert.IsNull(context.CurrentHmiName);
            await Assert.ThrowsExceptionAsync<EngineeringSessionUnavailableException>(() => inspection.GetHmisAsync());
            await Assert.ThrowsExceptionAsync<EngineeringSessionUnavailableException>(() => inspection.GetDevicesAsync());
            await Assert.ThrowsExceptionAsync<EngineeringSessionUnavailableException>(() => inspection.GetPlcsAsync());
            await Assert.ThrowsExceptionAsync<EngineeringSessionUnavailableException>(() => inspection.GetProjectInfoAsync());
            await Assert.ThrowsExceptionAsync<EngineeringSessionUnavailableException>(() => inspection.GetOpenProjectsAsync());
            context.ClearProjectSelection();
            Assert.AreEqual(0, (await inspection.GetHmisAsync()).Count);
            Assert.IsNull(context.ProjectUnavailableReason);
        }

        [TestMethod]
        public void FailureResult_PreservesCauseForWorkerButDoesNotSerializeExceptions()
        {
            var exception = new TargetInvocationException(new InvalidOperationException("Actual Siemens rejection"));
            var result = CommandResult.CreateFailure("Patch failed", new HardwarePatchResult { RecoveryRequired = true }, exception);
            Assert.AreSame(exception, result.FailureException);
            Assert.AreEqual("Actual Siemens rejection", result.Error);
            var json = JsonSerializer.Serialize(result);
            Assert.IsFalse(json.Contains("FailureException"));
            StringAssert.Contains(json, "RecoveryRequired");
        }

        [DataTestMethod]
        [DataRow("create-device", true, true)]
        [DataRow("  CREATE_DEVICE_WITH_ITEM ", true, true)]
        [DataRow("create-device-with-item", false, false)]
        [DataRow("set-attributes", true, false)]
        public void PreviewPolicy_IsolatesCreationAcrossDeviceTypes(string op, bool dryRun, bool isolated) =>
            Assert.AreEqual(isolated, HardwarePreviewPolicy.RequiresIsolation(dryRun, new[] { new HardwarePatchOperation { Op = op } }));
    }
}
