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

        public static string CreateDefaultArtifactPath(string commandName, string extension)
        {
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
                    ? "output-path was provided"
                    : $"inline result exceeded {InlineResultThresholdBytes} bytes",
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
                    ? "output-path was provided"
                    : $"inline result exceeded {InlineResultThresholdBytes} bytes",
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

            var mustQuote = value!.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            var escaped = value.Replace("\"", "\"\"");
            return mustQuote ? "\"" + escaped + "\"" : escaped;
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
    }
}
