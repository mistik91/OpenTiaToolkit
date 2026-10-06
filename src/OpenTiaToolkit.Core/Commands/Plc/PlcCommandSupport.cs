using OpenTiaToolkit.Core.Models;
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
            return CommandArtifactSupport.ResolveOutputPath(outputPath, defaultFileName);
        }

        public static CommandArtifactResult CreateJsonArtifact(
            object data,
            string outputPath,
            string dataKind,
            int itemCount,
            bool explicitOutputPath,
            object? metadata = null)
        {
            return CommandArtifactSupport.CreateJsonArtifact(
                data,
                outputPath,
                dataKind,
                itemCount,
                explicitOutputPath,
                metadata);
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

        public static long WriteFindReplaceCsv(FindReplaceResult result, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", FindReplaceCsvColumns));

                foreach (var change in result.Changes)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Change",
                        change.ItemType,
                        change.Path,
                        change.Before,
                        change.After,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var skipped in result.Skipped)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Skipped",
                        skipped.ItemType,
                        skipped.Path,
                        string.Empty,
                        string.Empty,
                        skipped.Name,
                        skipped.Reason,
                        string.Empty,
                        string.Empty,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var warning in result.Warnings)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Warning",
                        string.Empty,
                        warning.Path,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        warning.Code,
                        warning.Message,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var error in result.Errors)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Error",
                        error.ItemType,
                        error.Path,
                        string.Empty,
                        string.Empty,
                        error.Name,
                        string.Empty,
                        string.Empty,
                        error.Error,
                        error.Details
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }
            }

            return new FileInfo(outputPath).Length;
        }

        public static long WriteTagUpdateCsv(TagUpdateResult result, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", TagUpdateCsvColumns));

                foreach (var updated in result.Updated)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Updated",
                        string.Empty,
                        updated.TagTableName,
                        updated.MatchedBy,
                        updated.OldName,
                        updated.NewName,
                        updated.OldAddress,
                        updated.NewAddress,
                        string.Empty,
                        string.Empty,
                        updated.OldComment,
                        updated.NewComment,
                        string.Empty,
                        string.Empty,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var created in result.Created)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Created",
                        string.Empty,
                        created.TagTableName,
                        string.Empty,
                        string.Empty,
                        created.TagName,
                        string.Empty,
                        created.Address,
                        created.DataType,
                        string.Empty,
                        string.Empty,
                        created.Comment,
                        string.Empty,
                        string.Empty,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var deleted in result.Deleted)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Deleted",
                        string.Empty,
                        deleted.TagTableName,
                        string.Empty,
                        deleted.TagName,
                        string.Empty,
                        deleted.Address,
                        string.Empty,
                        string.Empty,
                        deleted.Reason,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var warning in result.Warnings)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Warning",
                        warning.LineNumber?.ToString(),
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        warning.Reason,
                        string.Empty,
                        string.Empty,
                        warning.Item,
                        string.Empty,
                        string.Empty
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }

                foreach (var error in result.Errors)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        "Error",
                        error.LineNumber?.ToString(),
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        error.Item,
                        error.Error,
                        error.Details
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }
            }

            return new FileInfo(outputPath).Length;
        }

        public static long WriteTagExportCsv(IReadOnlyList<PlcTagExportRow> rows, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", TagExportCsvColumns));
                foreach (var row in rows)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        row.TagTableName,
                        row.Name,
                        row.DataType,
                        row.LogicalAddress,
                        row.Comment
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }
            }

            return new FileInfo(outputPath).Length;
        }

        public static long WriteCrossReferencesCsv(IReadOnlyList<PlcCrossReferenceEntry> rows, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", CrossReferenceCsvColumns));
                foreach (var row in rows)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        row.Name,
                        row.Path,
                        row.Usage,
                        row.ItemType,
                        row.Detail
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }
            }

            return new FileInfo(outputPath).Length;
        }

        public static long WriteSurfaceItemsCsv(IReadOnlyList<PlcSurfaceItem> rows, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", SurfaceItemCsvColumns));
                foreach (var row in rows)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        row.Name,
                        row.ItemType,
                        row.Path,
                        row.Detail
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

        public static readonly IReadOnlyList<string> FindReplaceCsvColumns = new[]
        {
            "Kind",
            "ItemType",
            "Path",
            "Before",
            "After",
            "Name",
            "Reason",
            "Code",
            "Message",
            "Details"
        };

        public static readonly IReadOnlyList<string> TagUpdateCsvColumns = new[]
        {
            "Kind",
            "LineNumber",
            "TagTableName",
            "MatchedBy",
            "OldName",
            "NewName",
            "OldAddress",
            "NewAddress",
            "DataType",
            "Reason",
            "OldComment",
            "NewComment",
            "Item",
            "Error",
            "Details"
        };

        public static readonly IReadOnlyList<string> TagExportCsvColumns = new[]
        {
            "TagTableName",
            "Name",
            "DataType",
            "LogicalAddress",
            "Comment"
        };

        public static readonly IReadOnlyList<string> CrossReferenceCsvColumns = new[]
        {
            "Name",
            "Path",
            "Usage",
            "ItemType",
            "Detail"
        };

        public static readonly IReadOnlyList<string> SurfaceItemCsvColumns = new[]
        {
            "Name",
            "ItemType",
            "Path",
            "Detail"
        };
    }
}
