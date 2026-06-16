using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Text
{
    public sealed class ListProjectLanguagesCommand : ICommand
    {
        private readonly ITextService _textService;

        public ListProjectLanguagesCommand(ITextService textService)
        {
            _textService = textService;
        }

        public string Name => "list-project-languages";

        public string Description => "Lists project languages and active language settings";

        public CommandSchema Schema => new CommandSchema(Description);

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var result = await _textService.GetProjectLanguagesAsync();
                if (result == null)
                {
                    return CommandResult.CreateFailure("No project is currently open");
                }

                return CommandResult.CreateSuccess(
                    result,
                    $"Found {result.Languages.Count} project language(s), {result.ActiveLanguages.Count} active");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing project languages");
                return CommandResult.CreateFailure($"Error listing project languages: {ex.Message}", ex);
            }
        }
    }

    public sealed class ExportTextsCommand : ICommand
    {
        private readonly ITextService _textService;

        public ExportTextsCommand(ITextService textService)
        {
            _textService = textService;
        }

        public string Name => "export-texts";

        public string Description => "Exports project texts to a Siemens project text workbook";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("output-path", CommandParameterType.Path, "Target .xlsx file path or folder", required: true),
                new CommandParameter("source-culture", CommandParameterType.String, "Source language culture name, such as en-US", required: true),
                new CommandParameter("target-cultures", CommandParameterType.StringArray, "Target language culture names", required: true),
                new CommandParameter("categories", CommandParameterType.StringArray, "Optional project text category identifiers", required: false),
                new CommandParameter("objects", CommandParameterType.StringArray, "Top-level device or device-item names to export", required: true)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var outputPath = args.GetString("output-path");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: output-path");
            }

            var sourceCulture = args.GetString("source-culture");
            if (string.IsNullOrWhiteSpace(sourceCulture))
            {
                return CommandResult.CreateFailure("Missing required parameter: source-culture");
            }

            var targetCultures = args.GetStringArray("target-cultures");
            if (targetCultures.Length == 0)
            {
                return CommandResult.CreateFailure("Missing required parameter: target-cultures");
            }

            var objects = args.GetStringArray("objects");
            if (objects.Length == 0)
            {
                return CommandResult.CreateFailure("Missing required parameter: objects");
            }

            try
            {
                var resolvedOutputPath = CommandArtifactSupport.ResolveOutputPath(outputPath, "project_texts.xlsx");
                var result = await _textService.ExportTextsAsync(
                    resolvedOutputPath,
                    sourceCulture,
                    targetCultures,
                    args.Has("categories") ? args.GetStringArray("categories") : null,
                    objects);

                if (result == null)
                {
                    return CommandResult.CreateFailure("No project is currently open");
                }

                var artifact = new CommandArtifactResult
                {
                    OutputPath = result.OutputPath,
                    Format = "xlsx",
                    DataKind = "project-texts",
                    BytesWritten = result.BytesWritten,
                    ItemCount = null,
                    InlineOutputSuppressed = false,
                    Reason = CommandArtifactSupport.ExplicitOutputPathReason(),
                    Metadata = new
                    {
                        result.SourceCulture,
                        result.TargetCultures,
                        result.CategoryIdentifiers,
                        result.ObjectNames
                    }
                };

                return CommandResult.CreateSuccess(artifact, $"Project texts exported to: {result.OutputPath}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error exporting project texts");
                return CommandResult.CreateFailure($"Error exporting project texts: {ex.Message}", ex);
            }
        }
    }

    public sealed class ImportTextsCommand : ICommand
    {
        private readonly ITextService _textService;

        public ImportTextsCommand(ITextService textService)
        {
            _textService = textService;
        }

        public string Name => "import-texts";

        public string Description => "Imports project texts from a Siemens project text workbook";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("input-path", CommandParameterType.Path, "Source .xlsx project text workbook", required: true),
                new CommandParameter("update-source-language", CommandParameterType.Boolean, "Allow import to update source-language text", required: false, defaultValue: false),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate inputs without invoking Siemens text import", required: false, defaultValue: true)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var inputPath = args.GetString("input-path");
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: input-path");
            }

            try
            {
                var result = await _textService.ImportTextsAsync(
                    inputPath,
                    args.GetBool("update-source-language", false),
                    args.GetBool("dry-run", true));
                if (result == null)
                {
                    return CommandResult.CreateFailure("No project is currently open");
                }

                var action = result.DryRun ? "validated" : "imported";
                return CommandResult.CreateSuccess(result, $"Project texts {action} from: {result.InputPath}");
            }
            catch (FileNotFoundException ex)
            {
                return CommandResult.CreateFailure($"Project text import file was not found: {ex.FileName}", ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error importing project texts");
                return CommandResult.CreateFailure($"Error importing project texts: {ex.Message}", ex);
            }
        }
    }
}
