using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTiaToolkit.Core.Services;

internal static class PlcDocumentReadBack
{
    internal static string? Capture(V21DocumentAccess documents, object group, string name, ILogger? logger)
    {
        var path = Path.Combine(Path.GetTempPath(), "ott-document-readback-" + Guid.NewGuid().ToString("N"));
        try
        {
            var block = TiaReflectionHelper.GetBlocks(group)?.SingleOrDefault(candidate =>
                string.Equals(TiaReflectionHelper.GetName(candidate), name, StringComparison.OrdinalIgnoreCase));
            if (block == null) return null;

            Directory.CreateDirectory(path);
            if (!documents.TryExportAsDocuments(block, new DirectoryInfo(path), name, out _))
                return null;
            return Fingerprint(path, name);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read back block documents for {BlockName}", name);
            return null;
        }
        finally
        {
            PlcWorkflowPrimitives.Cleanup(path);
        }
    }

    internal static string Fingerprint(string path, string name)
    {
        string ReadText(string file) => File.ReadAllText(file).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        string document = ReadText(Path.Combine(path, name + ".s7dcl"));
        string resource = Path.Combine(path, name + ".s7res");
        return PlcWorkflowPrimitives.Hash(Encoding.UTF8.GetBytes(
            document + "\0" + (File.Exists(resource) ? ReadText(resource) : string.Empty)));
    }

    internal static void Compare(PlcDocumentImportResult outcome, string name, string? expected, string? before, string? after)
    {
        outcome.Verified = after != null && string.Equals(expected, after, StringComparison.Ordinal);
        outcome.AlreadyMatching = outcome.Verified && string.Equals(before, after, StringComparison.Ordinal);
        if (outcome.AlreadyMatching)
            outcome.Message = $"The target block '{name}' already matched the input documents.";
        else if (outcome.Verified)
            outcome.Message = $"Imported block '{name}' and verified it against the input documents.";
        else
            outcome.Message = $"TIA Portal reported success for '{name}', but the exported target could not be verified against the input documents.";
    }
}
