using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiScreenJsonEditResultProjectionTests
    {
        [TestMethod]
        public void BackupSafetyBlock_IsNotAppliedAndPropagatesSafetyDetails()
        {
            var edit = new HmiScreenJsonEditResult();
            var import = new HmiJsonImportResult
            {
                SafetyBlocked = true,
                SafetyMessage = "Import was blocked because the backup export could not be created.",
                BlockedReason = "backup-failed"
            };

            HmiScreenJsonEditService.ProjectImportOutcome(edit, import, new HmiJsonImportExecutionOptions());

            Assert.IsFalse(edit.Applied);
            Assert.IsTrue(edit.SafetyBlocked);
            Assert.AreEqual(import.SafetyMessage, edit.SafetyMessage);
            Assert.AreEqual(import.BlockedReason, edit.BlockedReason);
        }

        [TestMethod]
        public void NonblockedResultWithoutTransactionEvidence_IsNotApplied()
        {
            var edit = new HmiScreenJsonEditResult();

            HmiScreenJsonEditService.ProjectImportOutcome(
                edit,
                new HmiJsonImportResult { DryRun = false },
                new HmiJsonImportExecutionOptions());

            Assert.IsFalse(edit.Applied);
            Assert.IsFalse(edit.SafetyBlocked);
        }

        [TestMethod]
        public void CommittedNonblockedApply_IsApplied()
        {
            var edit = new HmiScreenJsonEditResult();

            HmiScreenJsonEditService.ProjectImportOutcome(
                edit,
                new HmiJsonImportResult
                {
                    DryRun = false,
                    TransactionUsed = true,
                    RolledBack = false
                },
                new HmiJsonImportExecutionOptions());

            Assert.IsTrue(edit.Applied);
            Assert.IsFalse(edit.SafetyBlocked);
        }

        [TestMethod]
        public void DryRunOrRolledBackResult_IsNotApplied()
        {
            var dryRunEdit = new HmiScreenJsonEditResult();
            var rolledBackEdit = new HmiScreenJsonEditResult();

            HmiScreenJsonEditService.ProjectImportOutcome(
                dryRunEdit,
                new HmiJsonImportResult { DryRun = true, TransactionUsed = true, RolledBack = true },
                new HmiJsonImportExecutionOptions());
            HmiScreenJsonEditService.ProjectImportOutcome(
                rolledBackEdit,
                new HmiJsonImportResult { DryRun = false, TransactionUsed = true, RolledBack = true },
                new HmiJsonImportExecutionOptions());

            Assert.IsFalse(dryRunEdit.Applied);
            Assert.IsFalse(rolledBackEdit.Applied);
        }

        [TestMethod]
        public void MissingImportResult_IsSafetyBlockedAndNotApplied()
        {
            var edit = new HmiScreenJsonEditResult();

            HmiScreenJsonEditService.ProjectImportOutcome(edit, import: null, new HmiJsonImportExecutionOptions());

            Assert.IsFalse(edit.Applied);
            Assert.IsTrue(edit.SafetyBlocked);
            StringAssert.Contains(edit.SafetyMessage, "did not return a result");
        }
    }
}
