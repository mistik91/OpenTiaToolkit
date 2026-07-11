using OpenTiaToolkit.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Handles file-path resolution and JSON serialization for Unified HMI JSON workflows.
    /// </summary>
    internal static class HmiJsonDocumentStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public static string ResolveExportFilePath(string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            return Path.HasExtension(fullPath)
                ? fullPath
                : Path.Combine(fullPath, "unified_hmi_export.json");
        }

        public static string ResolvePatchFilePath(string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            return Path.HasExtension(fullPath)
                ? fullPath
                : Path.Combine(fullPath, "unified_hmi_patch.json");
        }

        public static string ResolveImportFilePath(string inputPath)
        {
            var fullPath = Path.GetFullPath(inputPath);
            if (Path.HasExtension(fullPath))
            {
                return fullPath;
            }

            var exportFile = Path.Combine(fullPath, "unified_hmi_export.json");
            if (File.Exists(exportFile))
            {
                return exportFile;
            }

            var patchFile = Path.Combine(fullPath, "unified_hmi_patch.json");
            if (File.Exists(patchFile))
            {
                return patchFile;
            }

            return exportFile;
        }

        public static string ResolveReportFilePath(string inputPath, bool dryRun)
        {
            var inputFile = ResolveImportFilePath(inputPath);
            var baseDirectory = Path.GetDirectoryName(inputFile) ?? Path.GetFullPath(Environment.CurrentDirectory);
            return Path.Combine(baseDirectory, dryRun ? "unified_hmi_preflight_report.json" : "unified_hmi_import_report.json");
        }

        public static string ResolveBackupExportPath(string inputPath, string? backupPath)
        {
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                var explicitPath = Path.GetFullPath(backupPath!);
                return Path.HasExtension(explicitPath)
                    ? explicitPath
                    : Path.Combine(explicitPath, "unified_hmi_export.json");
            }

            var baseDirectory = Path.GetDirectoryName(ResolveImportFilePath(inputPath)) ?? Path.GetFullPath(Environment.CurrentDirectory);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(baseDirectory, "unified_hmi_backup_" + timestamp, "unified_hmi_export.json");
        }

        public static void SaveDocument(HmiJsonDocument document, string outputPath)
        {
            HmiJsonDocumentValidator.Validate(document);
            EnsureParentDirectory(outputPath);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(document, SerializerOptions));
        }

        public static HmiJsonDocument LoadDocument(string inputPath)
        {
            var resolvedPath = ResolveImportFilePath(inputPath);
            var json = File.ReadAllText(resolvedPath);
            var document = JsonSerializer.Deserialize<HmiJsonDocument>(json, SerializerOptions)
                ?? throw new InvalidOperationException("Unable to deserialize Unified HMI JSON document.");
            HmiJsonDocumentValidator.Validate(document);
            return document;
        }

        public static void SaveImportReport(HmiJsonImportResult report, string reportPath)
        {
            EnsureParentDirectory(reportPath);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, SerializerOptions));
        }

        private static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
