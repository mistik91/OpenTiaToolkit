using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Commands
{
    /// <summary>
    /// Shared helpers for keeping large command results out of inline tool output.
    /// </summary>
    public static class CommandArtifactSupport
    {
        public const int InlineResultThresholdBytes = 32 * 1024;
        private const int DefaultArtifactRetentionDays = 7;
        private const string ArtifactRetentionDaysVariable = "OPEN_TIA_ARTIFACT_RETENTION_DAYS";
        private static readonly object PruneLock = new object();
        private static DateTime _lastPruneUtc = DateTime.MinValue;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static int EstimateCommandResultBytes(object? data, string message)
        {
            var resultObj = new
            {
                success = true,
                message,
                error = (string?)null,
                data
            };

            return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(resultObj, JsonOptions));
        }

        public static bool ExceedsInlineThreshold(object? data, string message)
        {
            return EstimateCommandResultBytes(data, message) > InlineResultThresholdBytes;
        }

        public static string InlineResultExceededReason()
        {
            return $"inline result exceeded {InlineResultThresholdBytes} bytes";
        }

        public static string ExplicitOutputPathReason()
        {
            return "output-path was provided";
        }

        public static string CreateDefaultArtifactPath(string commandName, string extension)
        {
            PruneDefaultArtifactDirectory();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            var fileName = SanitizePathSegment(commandName) + "_" + timestamp + "_" + Guid.NewGuid().ToString("N") + extension;
            return Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "CommandArtifacts", fileName);
        }

        public static string ResolveOutputPath(string outputPath, string defaultFileName)
        {
            var fullPath = Path.GetFullPath(outputPath);
            return Path.HasExtension(fullPath)
                ? fullPath
                : Path.Combine(fullPath, defaultFileName);
        }

        public static CommandArtifactResult CreateJsonArtifact(
            object data,
            string outputPath,
            string dataKind,
            int itemCount,
            bool explicitOutputPath,
            object? metadata = null)
        {
            var bytesWritten = SaveJsonArtifact(data, outputPath);
            return new CommandArtifactResult
            {
                OutputPath = outputPath,
                Format = "json",
                DataKind = dataKind,
                BytesWritten = bytesWritten,
                ItemCount = itemCount,
                InlineOutputSuppressed = !explicitOutputPath,
                Reason = explicitOutputPath
                    ? ExplicitOutputPathReason()
                    : InlineResultExceededReason(),
                Metadata = metadata
            };
        }

        public static CommandArtifactResult CreateCsvArtifact(
            string outputPath,
            string dataKind,
            long bytesWritten,
            int itemCount,
            bool explicitOutputPath,
            IReadOnlyList<string> columns,
            object? metadata = null)
        {
            return new CommandArtifactResult
            {
                OutputPath = outputPath,
                Format = "csv",
                DataKind = dataKind,
                BytesWritten = bytesWritten,
                ItemCount = itemCount,
                InlineOutputSuppressed = !explicitOutputPath,
                Reason = explicitOutputPath
                    ? ExplicitOutputPathReason()
                    : InlineResultExceededReason(),
                Columns = columns,
                Metadata = metadata
            };
        }

        public static long SaveJsonArtifact(object data, string outputPath)
        {
            EnsureParentDirectory(outputPath);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(data, JsonOptions), Encoding.UTF8);
            return new FileInfo(outputPath).Length;
        }

        public static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var nonNullValue = value!;
            var spreadsheetSafeValue = IsSpreadsheetFormula(nonNullValue) ? "'" + nonNullValue : nonNullValue;
            var mustQuote = spreadsheetSafeValue.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            var escaped = spreadsheetSafeValue.Replace("\"", "\"\"");
            return mustQuote ? "\"" + escaped + "\"" : escaped;
        }

        private static bool IsSpreadsheetFormula(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            return value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' ||
                   value[0] == '\t' || value[0] == '\r';
        }

        public static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string SanitizePathSegment(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
            }

            return builder.Length == 0 ? "command" : builder.ToString();
        }

        private static void PruneDefaultArtifactDirectory()
        {
            var now = DateTime.UtcNow;
            lock (PruneLock)
            {
                if ((now - _lastPruneUtc) < TimeSpan.FromHours(1))
                {
                    return;
                }

                _lastPruneUtc = now;
            }

            var retention = ResolveArtifactRetention();
            if (retention <= TimeSpan.Zero)
            {
                return;
            }

            var directory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "CommandArtifacts");
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoffUtc = now - retention;
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    var lastWriteUtc = File.GetLastWriteTimeUtc(file);
                    if (lastWriteUtc < cutoffUtc)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Best-effort cleanup must never block command output.
                }
            }
        }

        private static TimeSpan ResolveArtifactRetention()
        {
            var configuredDays = Environment.GetEnvironmentVariable(ArtifactRetentionDaysVariable);
            if (string.IsNullOrWhiteSpace(configuredDays))
            {
                return TimeSpan.FromDays(DefaultArtifactRetentionDays);
            }

            return double.TryParse(configuredDays, NumberStyles.Float, CultureInfo.InvariantCulture, out var days) && days >= 0
                ? TimeSpan.FromDays(days)
                : TimeSpan.FromDays(DefaultArtifactRetentionDays);
        }
    }
}
