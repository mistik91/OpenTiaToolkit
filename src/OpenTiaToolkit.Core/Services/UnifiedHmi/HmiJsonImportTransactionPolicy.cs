using OpenTiaToolkit.Core.Models;
using System;
using static OpenTiaToolkit.Core.Services.HmiJsonImportPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonImportTransactionPolicy
    {
        internal HmiJsonImportResult Complete(
            string inputPath,
            HmiJsonImportResult? result,
            HmiJsonImportExecutionOptions options,
            string? backupOutputPath,
            Action commit)
        {
            if (result == null)
            {
                var missingResult = CreateSafetyBlockedResult(
                    inputPath,
                    dryRun: false,
                    "Import was rolled back because it did not return a result.");
                missingResult.BackupPath = backupOutputPath;
                missingResult.TransactionUsed = true;
                missingResult.RolledBack = true;
                return missingResult;
            }

            result.BackupPath = backupOutputPath;
            result.TransactionUsed = true;
            ApplySafetyPolicy(result, options, backupOutputPath);
            if (result.PropertyFailedCount > 0)
            {
                MarkSafetyBlocked(result, "Import was rolled back because one or more property changes failed.", backupOutputPath);
            }

            if (result.SafetyBlocked)
            {
                result.RolledBack = true;
                return result;
            }

            commit();
            return result;
        }

        internal HmiJsonImportResult FromException(string inputPath, Exception exception, string? backupOutputPath)
        {
            var transactionFailureMessage = FormatExceptionMessage(exception);
            var isOnlineModeFailure = IsOnlineModeTransactionFailure(exception);
            var result = CreateSafetyBlockedResult(
                inputPath,
                dryRun: false,
                isOnlineModeFailure
                    ? CreateOnlineModeSafetyMessage(transactionFailureMessage)
                    : $"Import was rolled back because the transaction failed: {transactionFailureMessage}",
                isOnlineModeFailure ? TiaOnlineModeBlockedReason : null);
            result.BackupPath = backupOutputPath;
            result.TransactionUsed = true;
            result.RolledBack = true;
            return result;
        }
    }
}
