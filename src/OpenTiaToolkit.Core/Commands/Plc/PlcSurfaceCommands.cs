using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    public sealed class ExportTagsCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public ExportTagsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "export-tags";

        public string Description => "Exports PLC tags from all tag tables";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                RequiredPlcName(),
                new CommandParameter("culture", CommandParameterType.String, "Project language culture used for tag comments (default: en-US)", required: false),
                new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for exported PLC tags", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var plcName = args.GetString("plc-name");
            if (string.IsNullOrWhiteSpace(plcName))
            {
                return CommandResult.CreateFailure("Missing required parameter: plc-name");
            }

            await OpenPlcIfNeeded(_plcService, plcName);
            var culture = args.GetString("culture");
            if (string.IsNullOrWhiteSpace(culture))
            {
                culture = "en-US";
            }

            var result = await _plcService.ExportTagsAsync(culture);
            if (!result.Success)
            {
                return CommandResult.CreateFailure(result.Message, result);
            }

            var message = $"Exported {result.TagCount} PLC tag(s) from {plcName}";
            if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(result, message))
            {
                var explicitOutputPath = args.Has("output-path");
                var outputPath = PlcCommandSupport.ResolveOutputPath(
                    explicitOutputPath
                        ? args.GetString("output-path")
                        : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                    "plc_tags.csv");
                var bytesWritten = PlcCommandSupport.WriteTagExportCsv(result.Tags, outputPath);
                var artifact = CommandArtifactSupport.CreateCsvArtifact(
                    outputPath,
                    "plc-tags",
                    bytesWritten,
                    result.TagCount,
                    explicitOutputPath,
                    PlcCommandSupport.TagExportCsvColumns,
                    new { plcName, culture });

                return CommandResult.CreateSuccess(artifact, $"{message}. CSV written to: {outputPath}");
            }

            return CommandResult.CreateSuccess(result, message);
        }

        private static CommandParameter RequiredPlcName() => new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true);

        private static async Task OpenPlcIfNeeded(IPlcService plcService, string plcName)
        {
            if (!plcService.IsSoftwareOpen || plcService.CurrentPlcName != plcName)
            {
                await plcService.OpenPlcAsync(plcName);
            }
        }
    }

    public sealed class GetCrossReferencesCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public GetCrossReferencesCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "get-cross-references";

        public string Description => "Gets PLC cross references for a tag, block, or named PLC object";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                RequiredPlcName(),
                new CommandParameter("name", CommandParameterType.String, "Tag, block, or PLC object name to look up", required: true),
                new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for cross-reference rows", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var plcName = args.GetString("plc-name");
            if (string.IsNullOrWhiteSpace(plcName))
            {
                return CommandResult.CreateFailure("Missing required parameter: plc-name");
            }

            var name = args.GetString("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return CommandResult.CreateFailure("Missing required parameter: name");
            }

            await OpenPlcIfNeeded(_plcService, plcName);
            var result = await _plcService.GetCrossReferencesAsync(name);
            if (!result.Success)
            {
                return CommandResult.CreateFailure(result.Message, result);
            }

            var message = $"Found {result.ReferenceCount} cross-reference(s) for {name}";
            if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(result, message))
            {
                var explicitOutputPath = args.Has("output-path");
                var outputPath = PlcCommandSupport.ResolveOutputPath(
                    explicitOutputPath
                        ? args.GetString("output-path")
                        : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                    "plc_cross_references.csv");
                var bytesWritten = PlcCommandSupport.WriteCrossReferencesCsv(result.Entries, outputPath);
                var artifact = CommandArtifactSupport.CreateCsvArtifact(
                    outputPath,
                    "plc-cross-references",
                    bytesWritten,
                    result.ReferenceCount,
                    explicitOutputPath,
                    PlcCommandSupport.CrossReferenceCsvColumns,
                    new { plcName, name });

                return CommandResult.CreateSuccess(artifact, $"{message}. CSV written to: {outputPath}");
            }

            return CommandResult.CreateSuccess(result, message);
        }

        private static CommandParameter RequiredPlcName() => new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true);

        private static async Task OpenPlcIfNeeded(IPlcService plcService, string plcName)
        {
            if (!plcService.IsSoftwareOpen || plcService.CurrentPlcName != plcName)
            {
                await plcService.OpenPlcAsync(plcName);
            }
        }
    }

    public abstract class ListPlcSurfaceItemsCommand : ICommand
    {
        private readonly IPlcService _plcService;

        protected ListPlcSurfaceItemsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public abstract string Name { get; }

        public abstract string Description { get; }

        protected abstract string DataKind { get; }

        protected abstract string DefaultFileName { get; }

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for rows", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var plcName = args.GetString("plc-name");
            if (string.IsNullOrWhiteSpace(plcName))
            {
                return CommandResult.CreateFailure("Missing required parameter: plc-name");
            }

            if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
            {
                await _plcService.OpenPlcAsync(plcName);
            }

            var rows = await ListAsync(_plcService);
            var message = $"Found {rows.Count} {Description.ToLowerInvariant()} item(s) in {plcName}";
            if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(rows, message))
            {
                var explicitOutputPath = args.Has("output-path");
                var outputPath = PlcCommandSupport.ResolveOutputPath(
                    explicitOutputPath
                        ? args.GetString("output-path")
                        : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                    DefaultFileName);
                var bytesWritten = PlcCommandSupport.WriteSurfaceItemsCsv(rows, outputPath);
                var artifact = CommandArtifactSupport.CreateCsvArtifact(
                    outputPath,
                    DataKind,
                    bytesWritten,
                    rows.Count,
                    explicitOutputPath,
                    PlcCommandSupport.SurfaceItemCsvColumns,
                    new { plcName });

                return CommandResult.CreateSuccess(artifact, $"{message}. CSV written to: {outputPath}");
            }

            return CommandResult.CreateSuccess(rows, message);
        }

        protected abstract Task<IReadOnlyList<PlcSurfaceItem>> ListAsync(IPlcService plcService);
    }

    public sealed class ListExternalSourcesCommand : ListPlcSurfaceItemsCommand
    {
        public ListExternalSourcesCommand(IPlcService plcService) : base(plcService) { }

        public override string Name => "list-external-sources";

        public override string Description => "PLC external sources";

        protected override string DataKind => "plc-external-sources";

        protected override string DefaultFileName => "plc_external_sources.csv";

        protected override Task<IReadOnlyList<PlcSurfaceItem>> ListAsync(IPlcService plcService) => plcService.ListExternalSourcesAsync();
    }

    public sealed class ListWatchTablesCommand : ListPlcSurfaceItemsCommand
    {
        public ListWatchTablesCommand(IPlcService plcService) : base(plcService) { }

        public override string Name => "list-watch-tables";

        public override string Description => "PLC watch and force tables";

        protected override string DataKind => "plc-watch-tables";

        protected override string DefaultFileName => "plc_watch_tables.csv";

        protected override Task<IReadOnlyList<PlcSurfaceItem>> ListAsync(IPlcService plcService) => plcService.ListWatchTablesAsync();
    }

    public sealed class ListTechnologyObjectsCommand : ListPlcSurfaceItemsCommand
    {
        public ListTechnologyObjectsCommand(IPlcService plcService) : base(plcService) { }

        public override string Name => "list-technology-objects";

        public override string Description => "PLC technology objects";

        protected override string DataKind => "plc-technology-objects";

        protected override string DefaultFileName => "plc_technology_objects.csv";

        protected override Task<IReadOnlyList<PlcSurfaceItem>> ListAsync(IPlcService plcService) => plcService.ListTechnologyObjectsAsync();
    }

    public sealed class ListPlcAlarmTextListsCommand : ListPlcSurfaceItemsCommand
    {
        public ListPlcAlarmTextListsCommand(IPlcService plcService) : base(plcService) { }

        public override string Name => "list-plc-alarm-text-lists";

        public override string Description => "PLC alarm text lists";

        protected override string DataKind => "plc-alarm-text-lists";

        protected override string DefaultFileName => "plc_alarm_text_lists.csv";

        protected override Task<IReadOnlyList<PlcSurfaceItem>> ListAsync(IPlcService plcService) => plcService.ListPlcAlarmTextListsAsync();
    }

    public sealed class ImportSourceCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public ImportSourceCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "import-source";

        public string Description => "Imports a PLC external source file when supported";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("source-path", CommandParameterType.Path, "Path to a .scl or .awl source file", required: true),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Preview without mutating the PLC project (default: true)", required: false),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Required for non-dry-run imports", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var plcName = args.GetString("plc-name");
            if (string.IsNullOrWhiteSpace(plcName))
            {
                return CommandResult.CreateFailure("Missing required parameter: plc-name");
            }

            var sourcePath = args.GetString("source-path");
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return CommandResult.CreateFailure("Missing required parameter: source-path");
            }

            if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
            {
                await _plcService.OpenPlcAsync(plcName);
            }

            var result = await _plcService.ImportSourceAsync(sourcePath, args.GetBool("dry-run", true), args.GetBool("confirm", false));
            return result.Success
                ? CommandResult.CreateSuccess(result, result.Message)
                : CommandResult.CreateFailure(result.Message, result);
        }
    }

    public sealed class GenerateBlocksFromSourceCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public GenerateBlocksFromSourceCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "generate-blocks-from-source";

        public string Description => "Generates PLC blocks from an external source when supported";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("source-name", CommandParameterType.String, "Name of the imported source file", required: true),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Preview without mutating the PLC project (default: true)", required: false),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Required for non-dry-run generation", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var plcName = args.GetString("plc-name");
            if (string.IsNullOrWhiteSpace(plcName))
            {
                return CommandResult.CreateFailure("Missing required parameter: plc-name");
            }

            var sourceName = args.GetString("source-name");
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return CommandResult.CreateFailure("Missing required parameter: source-name");
            }

            if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
            {
                await _plcService.OpenPlcAsync(plcName);
            }

            var result = await _plcService.GenerateBlocksFromSourceAsync(sourceName, args.GetBool("dry-run", true), args.GetBool("confirm", false));
            return result.Success
                ? CommandResult.CreateSuccess(result, result.Message)
                : CommandResult.CreateFailure(result.Message, result);
        }
    }

    public sealed class ExportBlockAsSourceCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public ExportBlockAsSourceCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "export-block-as-source";

        public string Description => "Exports a PLC block as an external source file when supported";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("block-name", CommandParameterType.String, "Name of the PLC block to export", required: true),
                new CommandParameter("output-path", CommandParameterType.Path, "Output source file or folder path", required: true)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var plcName = args.GetString("plc-name");
            if (string.IsNullOrWhiteSpace(plcName))
            {
                return CommandResult.CreateFailure("Missing required parameter: plc-name");
            }

            var blockName = args.GetString("block-name");
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return CommandResult.CreateFailure("Missing required parameter: block-name");
            }

            var outputPath = args.GetString("output-path");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: output-path");
            }

            if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
            {
                await _plcService.OpenPlcAsync(plcName);
            }

            var result = await _plcService.ExportBlockAsSourceAsync(blockName, outputPath);
            return result.Success
                ? CommandResult.CreateSuccess(result, result.Message)
                : CommandResult.CreateFailure(result.Message, result);
        }
    }
}
