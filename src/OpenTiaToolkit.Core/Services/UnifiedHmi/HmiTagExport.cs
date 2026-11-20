using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using YamlDotNet.Core;

namespace OpenTiaToolkit.Core.Services;

internal static class HmiTagExport
{
    private const string TableSuffix = ".hmi.yml";
    private const string DefinitionSuffix = ".def.hmi.yml";

    internal static string ValidateDestination(string path, bool overwrite)
    {
        if ((!path.EndsWith(TableSuffix, StringComparison.OrdinalIgnoreCase) &&
             !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ||
            path.EndsWith(DefinitionSuffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Specify output-path as a table .hmi.yml file or a .zip bundle.");

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            throw new IOException($"The export destination '{fullPath}' is a directory.");
        if (!overwrite && File.Exists(fullPath))
            throw new IOException($"The export destination '{fullPath}' already exists. Specify overwrite=true to replace it.");
        return fullPath;
    }

    internal static HmiTagExportResult Write(string hmiName, string tablePath, string outputPath, bool overwrite,
        Action<DirectoryInfo> nativeExport, CancellationToken cancellation, ILogger? logger = null)
    {
        outputPath = ValidateDestination(outputPath, overwrite);
        cancellation.ThrowIfCancellationRequested();
        var zip = outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var stage = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(outputPath)!, ".ott-winccml-" + Guid.NewGuid().ToString("N")));
        stage.Create();

        try
        {
            nativeExport(stage);
            cancellation.ThrowIfCancellationRequested();

            // Openness can return a path without the extension. Inspect the files it actually wrote.
            var files = stage.GetFiles().Where(file => file.Name.EndsWith(TableSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
            var tables = files.Where(file => !file.Name.EndsWith(DefinitionSuffix, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (tables.Length != 1 || tables[0].Length == 0)
                throw new InvalidDataException("TIA did not export exactly one nonempty WinCCML tag table.");

            var table = tables[0];
            var hasDefinitions = files.Length > 1;
            if (hasDefinitions && !zip)
                throw new InvalidDataException("TIA exported companion definitions. Specify a .zip output to keep the WinCCML files together.");

            var result = new HmiTagExportResult { HmiName = hmiName, TablePath = tablePath, OutputPath = outputPath };
            if (hasDefinitions)
            {
                result.ImportLimitation = "The native export includes companion definitions, which import-hmi-tags does not support.";
            }
            else
            {
                try
                {
                    result.ImportableTagCount = HmiTagBatchInput.ReadWinCcMl(File.ReadAllText(table.FullName)).Count;
                    result.SupportsToolkitImport = true;
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is YamlException)
                {
                    result.ImportLimitation = ex.Message;
                }
            }

            var publishPath = table.FullName;
            if (zip)
            {
                result.ArchiveEntries = files.Select(file => file.Name).ToArray();
                publishPath = Path.Combine(stage.FullName, "bundle.zip");
                using (var stream = File.Create(publishPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        using (var input = file.OpenRead())
                        using (var entry = archive.CreateEntry(file.Name, CompressionLevel.Fastest).Open())
                            input.CopyTo(entry);
                    }
                }
            }

            result.OutputBytes = new FileInfo(publishPath).Length;
            cancellation.ThrowIfCancellationRequested();
            if (overwrite && File.Exists(outputPath))
                File.Replace(publishPath, outputPath, null);
            else
                File.Move(publishPath, outputPath);
            return result;
        }
        finally
        {
            try
            {
                stage.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Unable to remove staged WinCCML export at {StagePath}", stage.FullName);
            }
        }
    }
}
