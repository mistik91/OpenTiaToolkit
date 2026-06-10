using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    internal static class PlcCommandSupport
    {
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
            var bytesWritten = CommandArtifactSupport.SaveJsonArtifact(data, outputPath);
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
                    : $"inline result exceeded {CommandArtifactSupport.InlineResultThresholdBytes} bytes",
                Metadata = metadata
            };
        }

        public static long WriteBlocksCsv(IReadOnlyList<BlockInfo> blocks, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", BlockCsvColumns));
                foreach (var block in blocks)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        block.Name,
                        block.Type,
                        block.Language,
                        block.Number,
                        block.IsReadOnly.ToString(),
                        block.Comment
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }
            }

            return new FileInfo(outputPath).Length;
        }

        public static readonly IReadOnlyList<string> BlockCsvColumns = new[]
        {
            "Name",
            "Type",
            "Language",
            "Number",
            "IsReadOnly",
            "Comment"
        };
    }
}
