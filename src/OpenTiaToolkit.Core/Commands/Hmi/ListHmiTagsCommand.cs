using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to list Unified HMI tags.
    /// </summary>
    public class ListHmiTagsCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public ListHmiTagsCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "list-hmi-tags";

        public string Description => "Lists tags in a Unified HMI";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("filter", CommandParameterType.String, "Optional text filter", required: false),
                new CommandParameter("root-path", CommandParameterType.String, "Optional tag path to enumerate only that subtree", required: false),
                new CommandParameter("allow-full-scan", CommandParameterType.Boolean, "Allow scanning the full HMI tag tree when root-path is omitted", required: false, defaultValue: false),
                new CommandParameter("max-results", CommandParameterType.Integer, "Maximum number of tags to return during enumeration", required: false, defaultValue: 200),
                new CommandParameter("max-depth", CommandParameterType.Integer, "Maximum member depth to traverse from the selected root", required: false, defaultValue: 4),
                new CommandParameter("include-details", CommandParameterType.Boolean, "Read detailed tag metadata such as display name and data type", required: false, defaultValue: true),
                new CommandParameter("include-logging-counts", CommandParameterType.Boolean, "Read logging tag counts for returned tags", required: false, defaultValue: false),
                new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for large tag lists", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var rootPath = args.Has("root-path") ? args.GetString("root-path") : null;
            var trimmedRootPath = rootPath?.Trim();
            var hasRootPath = !string.IsNullOrWhiteSpace(trimmedRootPath) &&
                !string.Equals(trimmedRootPath, "\\", StringComparison.Ordinal) &&
                !string.Equals(trimmedRootPath, "/", StringComparison.Ordinal);
            if (!hasRootPath && !args.GetBool("allow-full-scan", false))
            {
                return CommandResult.CreateFailure(
                    "Refusing to scan the complete Unified HMI tag tree without allow-full-scan=true. Provide a concrete root-path for a known tag/subtree, or opt into a bounded full scan with max-results and max-depth.",
                    new
                    {
                        safetyBlocked = true,
                        blockedReason = "full-hmi-tag-scan",
                        recommendedNextAction = "retry-with-root-path-or-allow-full-scan"
                    });
            }

            var maxResults = args.GetInt("max-results", 200);
            if (maxResults < 1)
            {
                return CommandResult.CreateFailure("Parameter max-results must be greater than zero.");
            }

            var maxDepth = args.GetInt("max-depth", 4);
            if (maxDepth < 0)
            {
                return CommandResult.CreateFailure("Parameter max-depth cannot be negative.");
            }

            var options = new HmiTagBrowseOptions
            {
                Filter = args.Has("filter") ? args.GetString("filter") : null,
                RootPath = hasRootPath ? trimmedRootPath : null,
                MaxResults = maxResults,
                MaxDepth = maxDepth,
                IncludeDetails = args.GetBool("include-details", true),
                IncludeLoggingCounts = args.GetBool("include-logging-counts", false)
            };

            var tags = await _hmiService.GetTagsAsync(options);
            var limitNote = tags.Count >= options.MaxResults.GetValueOrDefault(int.MaxValue)
                ? $" (limited to {options.MaxResults})"
                : string.Empty;
            var message = $"Found {tags.Count} Unified HMI tag(s){limitNote}";

            if (ShouldWriteCsvArtifact(args, tags, message))
            {
                var outputPath = ResolveCsvOutputPath(args.Has("output-path")
                    ? args.GetString("output-path")
                    : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"));
                var bytesWritten = WriteTagsCsv(tags, outputPath);
                var artifact = new CommandArtifactResult
                {
                    OutputPath = outputPath,
                    Format = "csv",
                    DataKind = "unified-hmi-tags",
                    BytesWritten = bytesWritten,
                    ItemCount = tags.Count,
                    InlineOutputSuppressed = !args.Has("output-path"),
                    Reason = args.Has("output-path")
                        ? "output-path was provided"
                        : $"inline result exceeded {CommandArtifactSupport.InlineResultThresholdBytes} bytes",
                    Columns = CsvColumns,
                    Metadata = new
                    {
                        hmiName,
                        options.Filter,
                        options.RootPath,
                        options.MaxResults,
                        options.MaxDepth,
                        options.IncludeDetails,
                        options.IncludeLoggingCounts,
                        limited = tags.Count >= options.MaxResults.GetValueOrDefault(int.MaxValue)
                    }
                };

                return CommandResult.CreateSuccess(artifact, $"{message}. CSV written to: {outputPath}");
            }

            return CommandResult.CreateSuccess(tags, $"Found {tags.Count} Unified HMI tag(s){limitNote}");
        }

        private static readonly IReadOnlyList<string> CsvColumns = new[]
        {
            "Path",
            "Name",
            "DisplayName",
            "DataType",
            "HmiDataType",
            "TagTableName",
            "HasMembers",
            "MemberCount",
            "LoggingTagCount"
        };

        private static bool ShouldWriteCsvArtifact(CommandArgs args, object data, string message)
        {
            return args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(data, message);
        }

        private static string ResolveCsvOutputPath(string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            return Path.HasExtension(fullPath)
                ? fullPath
                : Path.Combine(fullPath, "unified_hmi_tags.csv");
        }

        private static long WriteTagsCsv(IEnumerable<HmiTagInfo> tags, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);

            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", CsvColumns));
                foreach (var tag in tags)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        CommandArtifactSupport.EscapeCsv(tag.Path),
                        CommandArtifactSupport.EscapeCsv(tag.Name),
                        CommandArtifactSupport.EscapeCsv(tag.DisplayName),
                        CommandArtifactSupport.EscapeCsv(tag.DataType),
                        CommandArtifactSupport.EscapeCsv(tag.HmiDataType),
                        CommandArtifactSupport.EscapeCsv(tag.TagTableName),
                        CommandArtifactSupport.EscapeCsv(tag.HasMembers.ToString()),
                        CommandArtifactSupport.EscapeCsv(tag.MemberCount.ToString()),
                        CommandArtifactSupport.EscapeCsv(tag.LoggingTagCount.ToString())
                    }));
                }
            }

            return new FileInfo(outputPath).Length;
        }
    }
}
