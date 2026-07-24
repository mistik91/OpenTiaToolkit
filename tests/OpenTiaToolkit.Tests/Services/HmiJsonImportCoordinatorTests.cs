using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonImportCoordinatorTests
    {
        [TestMethod]
        public async Task DryRun_UsesRollbackOnlyPreflightWithoutBackupOrApply()
        {
            var expected = new HmiJsonImportResult { DryRun = true, TransactionUsed = true, RolledBack = true };
            var executor = new FakeExecutor(expected);
            var backup = new FakeBackupExporter();
            var runner = new FakeTransactionRunner { PreflightResult = expected };
            var coordinator = CreateCoordinator(backup, executor, runner);

            var result = await coordinator.ImportAsync("input.json", null, dryRun: true, new HmiJsonImportExecutionOptions());

            Assert.AreSame(expected, result);
            Assert.AreEqual(0, executor.CallCount);
            Assert.AreEqual(0, backup.CallCount);
            Assert.AreEqual(0, runner.CallCount);
            Assert.AreEqual(1, runner.PreflightCallCount);
            Assert.IsTrue(result!.DryRun);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsTrue(result.RolledBack);
        }

        [TestMethod]
        public async Task FailedCleanGate_PreventsBackupAndTransaction()
        {
            var preflight = new HmiJsonImportResult { DryRun = true, WarningCount = 1 };
            preflight.Warnings.Add("warning");
            var executor = new FakeExecutor(new HmiJsonImportResult());
            var backup = new FakeBackupExporter { Result = new HmiJsonExportResult { OutputPath = "backup.json" } };
            var runner = new FakeTransactionRunner { PreflightResult = preflight };
            var report = new FakeReportWriter();
            var coordinator = CreateCoordinator(backup, executor, runner, report);

            var result = await coordinator.ImportAsync(
                "input.json",
                null,
                dryRun: false,
                new HmiJsonImportExecutionOptions { RequireCleanPreflight = true, BackupBeforeImport = true });

            Assert.AreSame(preflight, result);
            Assert.IsTrue(result!.SafetyBlocked);
            Assert.AreEqual(0, backup.CallCount);
            Assert.AreEqual(0, runner.CallCount);
            Assert.AreEqual(1, runner.PreflightCallCount);
            Assert.AreEqual(0, report.CallCount);
        }

        [TestMethod]
        public async Task MissingGateResult_ReturnsStructuredBlock()
        {
            var executor = new FakeExecutor(new HmiJsonImportResult());
            var runner = new FakeTransactionRunner { PreflightResult = null };
            var report = new FakeReportWriter();
            var coordinator = CreateCoordinator(new FakeBackupExporter(), executor, runner, report);

            var result = await coordinator.ImportAsync(
                "input.json",
                null,
                dryRun: false,
                new HmiJsonImportExecutionOptions { RequireCleanPreflight = true });

            Assert.IsNotNull(result);
            Assert.IsTrue(result!.SafetyBlocked);
            StringAssert.Contains(result.SafetyMessage, "preflight");
            Assert.AreEqual(0, runner.CallCount);
            Assert.AreEqual(1, report.CallCount);
        }

        [TestMethod]
        public async Task BackupFailure_PreventsTransaction()
        {
            var runner = new FakeTransactionRunner();
            var backup = new FakeBackupExporter { Result = null };
            var coordinator = CreateCoordinator(backup, new FakeExecutor(new HmiJsonImportResult()), runner);

            var result = await coordinator.ImportAsync(
                "input.json",
                null,
                dryRun: false,
                new HmiJsonImportExecutionOptions { BackupBeforeImport = true });

            Assert.IsNotNull(result);
            Assert.IsTrue(result!.SafetyBlocked);
            StringAssert.Contains(result.SafetyMessage, "backup");
            Assert.AreEqual(1, backup.CallCount);
            Assert.AreEqual(0, runner.CallCount);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task BackupFailureAfterCleanDefaultPreflight_ReturnsApplySafetyBlock(string? backupPath)
        {
            var preflight = new HmiJsonImportResult { DryRun = true, TransactionUsed = true, RolledBack = true };
            var runner = new FakeTransactionRunner { PreflightResult = preflight };
            var backup = new FakeBackupExporter
            {
                Result = backupPath == null ? null : new HmiJsonExportResult { OutputPath = backupPath }
            };
            var report = new FakeReportWriter();
            var coordinator = CreateCoordinator(backup, new FakeExecutor(new HmiJsonImportResult()), runner, report);

            var result = await coordinator.ImportAsync(
                "input.json",
                null,
                dryRun: false,
                new HmiJsonImportExecutionOptions
                {
                    FailOnWarning = true,
                    RequireCleanPreflight = true,
                    BackupBeforeImport = true
                });

            Assert.IsNotNull(result);
            Assert.IsFalse(result!.DryRun);
            Assert.IsTrue(result.SafetyBlocked);
            StringAssert.Contains(result.SafetyMessage, "backup");
            Assert.IsTrue(string.IsNullOrEmpty(result.BackupPath));
            Assert.AreEqual(1, runner.PreflightCallCount);
            Assert.AreEqual(1, backup.CallCount);
            Assert.AreEqual(0, runner.CallCount);
            Assert.AreEqual(1, report.CallCount);
        }

        [TestMethod]
        public async Task CleanGatePreflight_ProceedsToBackupAndApply()
        {
            var runner = new FakeTransactionRunner
            {
                PreflightResult = new HmiJsonImportResult { DryRun = true, TransactionUsed = true, RolledBack = true },
                Result = new HmiJsonImportResult { TransactionUsed = true }
            };
            var backup = new FakeBackupExporter { Result = new HmiJsonExportResult { OutputPath = "backup.json" } };
            var coordinator = CreateCoordinator(backup, new FakeExecutor(new HmiJsonImportResult()), runner);

            var result = await coordinator.ImportAsync(
                "input.json",
                null,
                dryRun: false,
                new HmiJsonImportExecutionOptions
                {
                    FailOnWarning = true,
                    RequireCleanPreflight = true,
                    BackupBeforeImport = true
                });

            Assert.IsNotNull(result);
            Assert.AreEqual(1, runner.PreflightCallCount);
            Assert.AreEqual(1, backup.CallCount);
            Assert.AreEqual(1, runner.CallCount);
        }

        [TestMethod]
        public async Task PreflightTransactionBlock_PreventsBackupAndApply()
        {
            var runner = new FakeTransactionRunner
            {
                PreflightResult = new HmiJsonImportResult
                {
                    DryRun = true,
                    SafetyBlocked = true,
                    SafetyMessage = "Preflight transaction failed.",
                    TransactionUsed = true,
                    RolledBack = true
                }
            };
            var backup = new FakeBackupExporter { Result = new HmiJsonExportResult { OutputPath = "backup.json" } };
            var coordinator = CreateCoordinator(backup, new FakeExecutor(new HmiJsonImportResult()), runner);

            var result = await coordinator.ImportAsync(
                "input.json",
                null,
                dryRun: false,
                new HmiJsonImportExecutionOptions { RequireCleanPreflight = true, BackupBeforeImport = true });

            Assert.IsTrue(result!.SafetyBlocked);
            Assert.AreEqual(1, runner.PreflightCallCount);
            Assert.AreEqual(0, backup.CallCount);
            Assert.AreEqual(0, runner.CallCount);
        }

        [TestMethod]
        public async Task SafeApply_PassesBackupPathAndOptionsToOneTransaction()
        {
            var expected = new HmiJsonImportResult { TransactionUsed = true };
            var runner = new FakeTransactionRunner { Result = expected };
            var backup = new FakeBackupExporter { Result = new HmiJsonExportResult { OutputPath = "backup.json" } };
            var coordinator = CreateCoordinator(backup, new FakeExecutor(new HmiJsonImportResult()), runner);
            var options = new HmiJsonImportExecutionOptions { BackupBeforeImport = true };

            var result = await coordinator.ImportAsync("input.json", null, dryRun: false, options);

            Assert.AreSame(expected, result);
            Assert.AreEqual(1, runner.CallCount);
            Assert.AreEqual("backup.json", runner.BackupPath);
            Assert.AreSame(options, runner.Options);
        }

        [TestMethod]
        public async Task TransactionRunnerWithoutProject_ReturnsStructuredBlockWithoutOpeningTransaction()
        {
            var executor = new FakeExecutor(new HmiJsonImportResult());
            var runner = new HmiJsonImportTransactionRunner(
                new EngineeringSessionContext(),
                executor,
                new FakeReportWriter(),
                logger: null);

            var result = await runner.RunAsync("input.json", null, new HmiJsonImportExecutionOptions(), null);

            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsFalse(result.TransactionUsed);
            StringAssert.Contains(result.SafetyMessage, "no TIA Portal project");
            Assert.AreEqual(0, executor.CallCount);
        }

        [TestMethod]
        public async Task PreflightRunnerWithoutProject_ReturnsDryRunBlockWithoutInvokingExecutor()
        {
            var executor = new FakeExecutor(new HmiJsonImportResult());
            var report = new FakeReportWriter();
            var runner = new HmiJsonImportTransactionRunner(
                new EngineeringSessionContext(),
                executor,
                report,
                logger: null);

            var result = await runner.RunPreflightAsync("input.json", null, new HmiJsonImportExecutionOptions());

            Assert.IsTrue(result.DryRun);
            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsFalse(result.TransactionUsed);
            Assert.IsFalse(result.RolledBack);
            StringAssert.Contains(result.SafetyMessage, "no TIA Portal project");
            Assert.AreEqual(0, executor.CallCount);
            Assert.AreEqual(1, report.CallCount);
        }

        [TestMethod]
        public void PreflightPolicy_CleanResultIsRollbackOnlyAndNeverCommits()
        {
            var input = new HmiJsonImportResult();

            var result = new HmiJsonImportTransactionPolicy().CompletePreflight(
                "input.json",
                input,
                new HmiJsonImportExecutionOptions());

            Assert.AreSame(input, result);
            Assert.IsTrue(result.DryRun);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsTrue(result.RolledBack);
            Assert.IsFalse(result.SafetyBlocked);
        }

        [TestMethod]
        public void PreflightPolicy_NullResultBecomesStructuredDryRunBlock()
        {
            var result = new HmiJsonImportTransactionPolicy().CompletePreflight(
                "input.json",
                result: null,
                new HmiJsonImportExecutionOptions());

            Assert.IsTrue(result.DryRun);
            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsTrue(result.RolledBack);
            StringAssert.Contains(result.SafetyMessage, "did not return a result");
        }

        [TestMethod]
        public void PreflightPolicy_GenericExceptionReportsRollbackWithoutOnlineReason()
        {
            var result = new HmiJsonImportTransactionPolicy().FromPreflightException(
                "input.json",
                new System.InvalidOperationException("generic transaction failure"));

            Assert.IsTrue(result.DryRun);
            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsTrue(result.RolledBack);
            Assert.IsNull(result.BlockedReason);
            StringAssert.Contains(result.SafetyMessage, "transaction failed");
        }

        [TestMethod]
        public void PreflightPolicy_NestedOnlineExceptionMapsBlockedReason()
        {
            var exception = new System.Exception("outer", new System.InvalidOperationException("Operation is not permitted in online mode"));

            var result = new HmiJsonImportTransactionPolicy().FromPreflightException("input.json", exception);

            Assert.AreEqual("tia-online-mode", result.BlockedReason);
            Assert.IsTrue(result.DryRun);
            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsTrue(result.RolledBack);
        }

        [TestMethod]
        public void TransactionPolicy_NullResultRollsBackWithoutCommit()
        {
            var commitCount = 0;
            var result = new HmiJsonImportTransactionPolicy().Complete(
                "input.json",
                result: null,
                new HmiJsonImportExecutionOptions(),
                "backup.json",
                () => commitCount++);

            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsTrue(result.RolledBack);
            Assert.AreEqual("backup.json", result.BackupPath);
            Assert.AreEqual(0, commitCount);
        }

        [TestMethod]
        public void TransactionPolicy_SafeResultCommitsExactlyOnce()
        {
            var commitCount = 0;
            var input = new HmiJsonImportResult();
            var result = new HmiJsonImportTransactionPolicy().Complete(
                "input.json",
                input,
                new HmiJsonImportExecutionOptions(),
                null,
                () => commitCount++);

            Assert.AreSame(input, result);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsFalse(result.RolledBack);
            Assert.AreEqual(1, commitCount);
        }

        [TestMethod]
        public void TransactionPolicy_PropertyFailureBlocksCommitAndMarksRollback()
        {
            var commitCount = 0;
            var result = new HmiJsonImportTransactionPolicy().Complete(
                "input.json",
                new HmiJsonImportResult { PropertyFailedCount = 1 },
                new HmiJsonImportExecutionOptions(),
                null,
                () => commitCount++);

            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsTrue(result.RolledBack);
            Assert.AreEqual(0, commitCount);
        }

        [TestMethod]
        public void TransactionPolicy_OnlineExceptionMapsBlockedReasonAndRollback()
        {
            var exception = new System.Exception("outer", new System.InvalidOperationException("Operation is not permitted in online mode"));
            var result = new HmiJsonImportTransactionPolicy().FromException("input.json", exception, "backup.json");

            Assert.AreEqual("tia-online-mode", result.BlockedReason);
            Assert.IsTrue(result.SafetyBlocked);
            Assert.IsTrue(result.TransactionUsed);
            Assert.IsTrue(result.RolledBack);
            Assert.AreEqual("backup.json", result.BackupPath);
        }

        [TestMethod]
        public void TransactionPolicy_GenericExceptionDoesNotMasqueradeAsOnlineMode()
        {
            var result = new HmiJsonImportTransactionPolicy().FromException(
                "input.json",
                new System.InvalidOperationException("generic transaction failure"),
                null);

            Assert.IsNull(result.BlockedReason);
            StringAssert.Contains(result.SafetyMessage, "transaction failed");
            Assert.IsTrue(result.RolledBack);
        }

        [TestMethod]
        public void ReportWriter_FailureDoesNotHideImportResult()
        {
            var result = new HmiJsonImportResult();

            new HmiJsonImportReportWriter().TryWrite(result, "\0invalid", dryRun: false);

            Assert.IsNull(result.ReportPath);
        }

        [TestMethod]
        public void ResultBuilder_UsesStableCountersAndPaths()
        {
            var result = new HmiJsonImportResult();

            HmiJsonImportResultBuilder.AddObjectAction(result, "Item", "Screens/Main", "Button_1", "HmiButton", "Create", "Missing");
            HmiJsonImportResultBuilder.AddSkippedProperty(result, "Item", "Screens/Main", "Button_1", "Text", "Filtered out");
            HmiJsonImportResultBuilder.AddFailedProperty(result, "Item", "Screens/Main", "Button_1", "Visible", "Read-only");

            Assert.AreEqual(1, result.ObjectActions.Count);
            Assert.AreEqual(1, result.PropertySkippedCount);
            Assert.AreEqual(1, result.PropertyFailedCount);
            Assert.AreEqual("Screens/Main/Button_1", HmiJsonImportResultBuilder.FormatObjectPath("Screens/Main", "Button_1"));
        }

        private static HmiJsonImportCoordinator CreateCoordinator(
            FakeBackupExporter backup,
            FakeExecutor executor,
            FakeTransactionRunner runner,
            FakeReportWriter? report = null) =>
            new HmiJsonImportCoordinator(
                new EngineeringSessionContext(),
                backup,
                executor,
                report ?? new FakeReportWriter(),
                runner,
                logger: null);

        private sealed class FakeExecutor : IHmiJsonDocumentExecutor
        {
            private readonly HmiJsonImportResult? _result;
            internal int CallCount { get; private set; }
            internal bool LastDryRun { get; private set; }

            internal FakeExecutor(HmiJsonImportResult? result) { _result = result; }

            public Task<HmiJsonImportResult?> ImportCoreAsync(string inputPath, HmiJsonFilterOptions? filters, bool dryRun)
            {
                CallCount++;
                LastDryRun = dryRun;
                return Task.FromResult(_result);
            }
        }

        private sealed class FakeBackupExporter : IHmiJsonBackupExporter
        {
            internal int CallCount { get; private set; }
            internal HmiJsonExportResult? Result { get; set; }

            public Task<HmiJsonExportResult?> ExportAsync(string outputPath, HmiJsonFilterOptions? filters)
            {
                CallCount++;
                return Task.FromResult(Result);
            }
        }

        private sealed class FakeReportWriter : IHmiJsonImportReportWriter
        {
            internal int CallCount { get; private set; }
            public void TryWrite(HmiJsonImportResult result, string inputPath, bool dryRun) => CallCount++;
        }

        private sealed class FakeTransactionRunner : IHmiJsonImportTransactionRunner
        {
            internal int CallCount { get; private set; }
            internal int PreflightCallCount { get; private set; }
            internal string? BackupPath { get; private set; }
            internal HmiJsonImportExecutionOptions? Options { get; private set; }
            internal HmiJsonImportResult Result { get; set; } = new HmiJsonImportResult();
            internal HmiJsonImportResult? PreflightResult { get; set; } = new HmiJsonImportResult
            {
                DryRun = true,
                TransactionUsed = true,
                RolledBack = true
            };

            public Task<HmiJsonImportResult> RunPreflightAsync(string inputPath, HmiJsonFilterOptions? filters, HmiJsonImportExecutionOptions options)
            {
                PreflightCallCount++;
                return Task.FromResult(PreflightResult!);
            }

            public Task<HmiJsonImportResult> RunAsync(string inputPath, HmiJsonFilterOptions? filters, HmiJsonImportExecutionOptions options, string? backupOutputPath)
            {
                CallCount++;
                BackupPath = backupOutputPath;
                Options = options;
                return Task.FromResult(Result);
            }
        }
    }
}
