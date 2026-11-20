using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.VersionControl;
using System;
using System.IO;

namespace OpenTiaToolkit.Core.Services;

internal static class VciSimaticSdEvidence
{
    internal static bool Applies(MappedObject mapping) =>
        string.Equals(mapping.FileFormat, "SimaticSD", StringComparison.OrdinalIgnoreCase) &&
        mapping.EngineeringObject is PlcBlock;

    internal static string? ReadSide(MappedObject mapping, bool workspaceSide) =>
        workspaceSide ? ReadWorkspace(mapping) : ReadProject(mapping);

    internal static string? ReadWorkspace(MappedObject mapping)
    {
        try
        {
            var directory = mapping.DirectoryPath?.ToString();
            var name = mapping.FileNameWithoutExtension;
            return directory == null || !Path.IsPathRooted(directory) || string.IsNullOrEmpty(name)
                ? null : PlcDocumentReadBack.Fingerprint(directory, name);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is EngineeringException)
        {
            return null;
        }
    }

    internal static string? ReadProject(MappedObject mapping)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ott-vci-readback-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!(mapping.EngineeringObject is PlcBlock block) || string.IsNullOrEmpty(mapping.FileNameWithoutExtension))
                return null;
            var name = mapping.FileNameWithoutExtension;
            Directory.CreateDirectory(directory);
            var export = block.ExportAsDocuments(new DirectoryInfo(directory), name);
            return export.State == DocumentResultState.Success ? PlcDocumentReadBack.Fingerprint(directory, name) : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is EngineeringException)
        {
            return null;
        }
        finally
        {
            PlcWorkflowPrimitives.Cleanup(directory);
        }
    }

    internal static bool? Matches(string? expected, string? actual) =>
        expected == null || actual == null ? (bool?)null : string.Equals(expected, actual, StringComparison.Ordinal);
}
