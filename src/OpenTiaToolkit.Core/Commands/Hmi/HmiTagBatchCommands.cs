using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi;

public sealed class CreateHmiTagTableCommand : HmiTagBatchCommand
{
    public CreateHmiTagTableCommand(IHmiService hmi) : base(hmi) { }
    public override string Name => "create-hmi-tag-table";
    public override string Description => "Creates a Unified HMI tag table in the root or an existing group";
}

public sealed class CreateHmiTagsCommand : HmiTagBatchCommand
{
    public CreateHmiTagsCommand(IHmiService hmi) : base(hmi) { }
    public override string Name => "create-hmi-tags";
    public override string Description => "Creates and verifies a batch of simple Unified HMI tags from JSON";
    protected override bool UsesInput => true;
}

public sealed class ImportHmiTagsCommand : HmiTagBatchCommand
{
    public ImportHmiTagsCommand(IHmiService hmi) : base(hmi) { }
    public override string Name => "import-hmi-tags";
    public override string Description => "Imports and verifies simple Unified HMI tags from a WinCCML .hmi.yml file";
    protected override bool UsesInput => true;
    protected override bool UsesWinCcMl => true;
}

public abstract class HmiTagBatchCommand : IContextualCommand
{
    private readonly IHmiService _hmi;

    protected HmiTagBatchCommand(IHmiService hmi) => _hmi = hmi;

    public abstract string Name { get; }
    public abstract string Description { get; }
    protected virtual bool UsesInput => false;
    protected virtual bool UsesWinCcMl => false;

    public CommandSchema Schema
    {
        get
        {
            var parameters = new List<CommandParameter>
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "Unified HMI name", required: true),
                new CommandParameter("table-path", CommandParameterType.String, "Table name or slash-delimited path through existing groups", required: true),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Preview without writing to the project", defaultValue: true),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm applying the batch with dry-run=false", defaultValue: false)
            };
            if (UsesInput)
                parameters.Add(new CommandParameter("input-path", CommandParameterType.Path,
                    UsesWinCcMl ? "Path to one SimpleTags .hmi.yml file" : "Path to a JSON array of tag specifications", required: true));
            if (UsesWinCcMl)
                parameters.Add(new CommandParameter("overwrite", CommandParameterType.Boolean, "Allow replacement of matching tags in the destination table", defaultValue: false));
            return new CommandSchema(Description, parameters);
        }
    }

    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) =>
        ExecuteAsync(args, logger, CommandExecutionContext.None);

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
    {
        var hmiName = args.GetString("hmi-name");
        var tablePath = args.GetString("table-path");
        if (string.IsNullOrWhiteSpace(hmiName) || string.IsNullOrWhiteSpace(tablePath) ||
            !HmiTagTablePath.IsValid(tablePath))
            return CommandResult.CreateFailure("Specify hmi-name and a table-path with no empty segments.");

        var dryRun = !args.Has("dry-run") || args.GetBool("dry-run");
        if (!dryRun && (!args.Has("confirm") || !args.GetBool("confirm")))
            return CommandResult.CreateFailure("Applying an HMI tag batch requires confirm=true.");

        string? stagedPath = null;
        try
        {
            var request = new HmiTagBatchRequest
            {
                HmiName = hmiName,
                TablePath = tablePath,
                DryRun = dryRun,
                Overwrite = UsesWinCcMl && args.Has("overwrite") && args.GetBool("overwrite")
            };
            if (UsesInput)
            {
                var inputPath = args.GetString("input-path");
                if (string.IsNullOrWhiteSpace(inputPath))
                    return CommandResult.CreateFailure("Missing required parameter: input-path.");
                if (UsesWinCcMl && (!inputPath.EndsWith(".hmi.yml", StringComparison.OrdinalIgnoreCase) ||
                    inputPath.EndsWith(".def.hmi.yml", StringComparison.OrdinalIgnoreCase)))
                    return CommandResult.CreateFailure("Specify a SimpleTags .hmi.yml file.");

                if (UsesWinCcMl)
                {
                    var bytes = File.ReadAllBytes(inputPath);
                    using (var reader = new StreamReader(new MemoryStream(bytes)))
                        request.Tags = HmiTagBatchInput.ReadWinCcMl(reader.ReadToEnd());
                    if (!dryRun)
                    {
                        // On .NET Framework, Directory.CreateDirectory can return a DirectoryInfo
                        // whose string representation is relative. Siemens rejects that path.
                        var stage = new DirectoryInfo(Path.GetFullPath(
                            Path.Combine(Path.GetTempPath(), "OttWinCcMl-" + Guid.NewGuid().ToString("N"))));
                        stage.Create();
                        stagedPath = Path.Combine(stage.FullName, "Tags.hmi.yml");
                        File.WriteAllBytes(stagedPath, bytes);
                        request.ImportDirectory = stage;
                    }
                }
                else
                {
                    request.Tags = HmiTagBatchInput.ReadJson(File.ReadAllText(inputPath));
                }
            }

            if ((!_hmi.IsHmiOpen || !string.Equals(_hmi.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase)) &&
                !await _hmi.OpenUnifiedHmiAsync(hmiName).ConfigureAwait(false))
                return CommandResult.CreateFailure($"Unified HMI '{hmiName}' was not found.");

            context.Checkpoint(dryRun ? "Checking destination and collisions." : "Writing and verifying Unified HMI tags.", 50, "hmi-tag-batch");
            var result = await _hmi.ExecuteTagBatchAsync(request, context.CancellationToken).ConfigureAwait(false);
            return CommandResult.CreateSuccess(result, dryRun ? "Unified HMI tag batch preview." : "Unified HMI tag batch verified.");
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            return CommandResult.CreateFailure(ex.Message);
        }
        finally
        {
            if (stagedPath != null)
            {
                try
                {
                    File.Delete(stagedPath);
                    Directory.Delete(Path.GetDirectoryName(stagedPath));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Unable to remove staged WinCCML input at {StagedPath}", stagedPath);
                }
            }
        }
    }
}
