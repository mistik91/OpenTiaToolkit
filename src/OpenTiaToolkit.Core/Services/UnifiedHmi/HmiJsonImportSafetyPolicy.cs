using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiJsonImportSafetyPolicy
    {
        internal const string TiaOnlineModeBlockedReason = "tia-online-mode";

        internal static void Apply(HmiJsonImportResult? result, HmiJsonImportExecutionOptions options, string? backupPath = null)
        {
            if (result == null)
            {
                return;
            }

            var blockingMessage = GetBlockingMessage(result, options);
            if (!string.IsNullOrWhiteSpace(blockingMessage))
            {
                MarkBlocked(result, blockingMessage!, backupPath);
            }
        }

        internal static string? GetBlockingMessage(HmiJsonImportResult result, HmiJsonImportExecutionOptions options)
        {
            if ((options.RequireCleanPreflight || options.FailOnWarning) &&
                (result.WarningCount > 0 || result.PropertyFailedCount > 0 || result.PropertySkippedCount > 0))
            {
                return $"Clean preflight was required, but the run reported {result.WarningCount} warning(s), {result.PropertyFailedCount} failed property change(s), and {result.PropertySkippedCount} skipped property change(s).";
            }

            return null;
        }

        internal static bool IsOnlineModeFailure(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            var formattedMessage = FormatExceptionMessage(ex);
            return formattedMessage.IndexOf("online mode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   formattedMessage.IndexOf("not permitted in online", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string CreateOnlineModeMessage(string? detail)
        {
            var message = "Import was blocked because TIA Portal is online. HMI JSON apply/import requires the target to be offline before opening the Openness transaction. Take the HMI or project offline in TIA Portal, then retry the same apply command.";
            return string.IsNullOrWhiteSpace(detail) ? message : message + " Details: " + detail;
        }

        internal static HmiJsonImportResult CreateBlockedResult(string inputPath, bool dryRun, string safetyMessage, string? blockedReason = null)
        {
            return new HmiJsonImportResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                DryRun = dryRun,
                InputPath = HmiJsonDocumentStore.ResolveImportFilePath(inputPath),
                SafetyBlocked = true,
                SafetyMessage = safetyMessage,
                BlockedReason = blockedReason
            };
        }

        internal static void MarkBlocked(HmiJsonImportResult result, string safetyMessage, string? backupPath, string? blockedReason = null)
        {
            result.SafetyBlocked = true;
            result.SafetyMessage = safetyMessage;
            result.BlockedReason = blockedReason;
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                result.BackupPath = backupPath;
            }
        }

        private static string FormatExceptionMessage(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
                {
                    messages.Add(current.Message);
                }
            }

            return messages.Count == 0 ? ex.GetType().Name : string.Join(" | Inner: ", messages);
        }
    }
}
