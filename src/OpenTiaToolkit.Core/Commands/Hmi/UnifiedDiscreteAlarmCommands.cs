using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi;

internal static class UnifiedDiscreteAlarmCommandSupport
{
    internal static async Task<string?> OpenAsync(IHmiService service, string hmiName)
    {
        if (string.IsNullOrWhiteSpace(hmiName)) return "Missing required parameter: hmi-name";
        if (service.IsHmiOpen && string.Equals(service.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase)) return null;
        return await service.OpenUnifiedHmiAsync(hmiName) ? null : $"Failed to open Unified HMI: {hmiName}";
    }

    internal static CommandResult Failure(Exception ex)
    {
        var root = ex;
        while (root.InnerException != null) root = root.InnerException;
        return root.GetType().Name.IndexOf("LicenseNotFoundException", StringComparison.OrdinalIgnoreCase) >= 0
            ? CommandResult.CreateFailure(
                "A Siemens WinCC Unified engineering license is required for discrete-alarm access.",
                new { errorCode = "unified-license-required", actionableMessage = "Install or activate the WinCC Unified engineering license and retry." },
                root)
            : CommandResult.CreateFailure(root.Message, root);
    }
}

public sealed class ListUnifiedDiscreteAlarmsCommand : ICommand
{
    private readonly IHmiService _hmiService;
    public ListUnifiedDiscreteAlarmsCommand(IHmiService hmiService) => _hmiService = hmiService;
    public string Name => "list-unified-discrete-alarms";
    public string Description => "Lists complete canonical WinCC Unified discrete-alarm snapshots";
    public CommandSchema Schema => new(Description, new[]
    {
        new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
        new CommandParameter("include-validation", CommandParameterType.Boolean, "Include Siemens Validate() diagnostics", defaultValue: false)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        var error = await UnifiedDiscreteAlarmCommandSupport.OpenAsync(_hmiService, args.GetString("hmi-name"));
        if (error != null) return CommandResult.CreateFailure(error);
        try
        {
            var alarms = await _hmiService.ListDiscreteAlarmsAsync(args.GetBool("include-validation", false));
            return CommandResult.CreateSuccess(alarms, $"Found {alarms.Count} Unified discrete alarm(s).");
        }
        catch (Exception ex) { return UnifiedDiscreteAlarmCommandSupport.Failure(ex); }
    }
}

public sealed class GetUnifiedDiscreteAlarmCommand : ICommand
{
    private readonly IHmiService _hmiService;
    public GetUnifiedDiscreteAlarmCommand(IHmiService hmiService) => _hmiService = hmiService;
    public string Name => "get-unified-discrete-alarm";
    public string Description => "Gets one exact canonical WinCC Unified discrete-alarm snapshot";
    public CommandSchema Schema => new(Description, new[]
    {
        new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
        new CommandParameter("alarm-name", CommandParameterType.String, "The exact discrete-alarm name", required: true),
        new CommandParameter("include-validation", CommandParameterType.Boolean, "Include Siemens Validate() diagnostics", defaultValue: true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        var name = args.GetString("alarm-name");
        if (string.IsNullOrWhiteSpace(name)) return CommandResult.CreateFailure("Missing required parameter: alarm-name");
        var error = await UnifiedDiscreteAlarmCommandSupport.OpenAsync(_hmiService, args.GetString("hmi-name"));
        if (error != null) return CommandResult.CreateFailure(error);
        try
        {
            var alarm = await _hmiService.GetDiscreteAlarmAsync(name, args.GetBool("include-validation", true));
            return alarm == null
                ? CommandResult.CreateFailure($"Unified discrete alarm not found: {name}", new { errorCode = "alarm-not-found", alarmName = name })
                : CommandResult.CreateSuccess(alarm, $"Found Unified discrete alarm: {name}");
        }
        catch (Exception ex) { return UnifiedDiscreteAlarmCommandSupport.Failure(ex); }
    }
}

public sealed class ApplyUnifiedDiscreteAlarmsCommand : ICommand
{
    private readonly IHmiService _hmiService;
    public ApplyUnifiedDiscreteAlarmsCommand(IHmiService hmiService) => _hmiService = hmiService;
    public string Name => "apply-unified-discrete-alarms";
    public string Description => "Atomically dry-runs or applies a versioned WinCC Unified discrete-alarm document";
    public CommandSchema Schema => new(Description, new[]
    {
        new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
        new CommandParameter("input-path", CommandParameterType.Path, "Path to the versioned discrete-alarm JSON document", required: true),
        new CommandParameter("dry-run", CommandParameterType.Boolean, "Execute setters and Siemens validation in a rolled-back transaction", defaultValue: true),
        new CommandParameter("apply", CommandParameterType.Boolean, "Explicitly request apply", defaultValue: false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm the reviewed apply", defaultValue: false),
        new CommandParameter("fail-on-warning", CommandParameterType.Boolean, "Block commit on Siemens or toolkit warnings", defaultValue: true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        var inputPath = args.GetString("input-path");
        if (string.IsNullOrWhiteSpace(inputPath)) return CommandResult.CreateFailure("Missing required parameter: input-path");
        var dryRun = args.GetBool("dry-run", true);
        var apply = args.GetBool("apply", false);
        var confirm = args.GetBool("confirm", false);
        if (dryRun && apply) return CommandResult.CreateFailure("Parameter apply=true requires dry-run=false.");
        if (!dryRun && (!apply || !confirm)) return CommandResult.CreateFailure("Applying Unified discrete alarms requires dry-run=false, apply=true, and confirm=true after reviewing dry-run output.");
        var error = await UnifiedDiscreteAlarmCommandSupport.OpenAsync(_hmiService, args.GetString("hmi-name"));
        if (error != null) return CommandResult.CreateFailure(error);
        var result = await _hmiService.ApplyDiscreteAlarmsAsync(inputPath, dryRun, args.GetBool("fail-on-warning", true));
        return result.Success ? CommandResult.CreateSuccess(result, result.Message) : CommandResult.CreateFailure(result.Message, result);
    }
}

public sealed class DeleteUnifiedDiscreteAlarmCommand : ICommand
{
    private readonly IHmiService _hmiService;
    public DeleteUnifiedDiscreteAlarmCommand(IHmiService hmiService) => _hmiService = hmiService;
    public string Name => "delete-unified-discrete-alarm";
    public string Description => "Dry-runs or deletes exactly one WinCC Unified discrete alarm with hash and reference guards";
    public CommandSchema Schema => new(Description, new[]
    {
        new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
        new CommandParameter("alarm-name", CommandParameterType.String, "The exact discrete-alarm name", required: true),
        new CommandParameter("expected-hash", CommandParameterType.String, "Canonical hash returned by list/get", required: true),
        new CommandParameter("dry-run", CommandParameterType.Boolean, "Execute deletion in a rolled-back transaction", defaultValue: true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm destructive deletion", defaultValue: false),
        new CommandParameter("allow-referenced-delete", CommandParameterType.Boolean, "Allow deletion after reviewing reported cross references", defaultValue: false)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        var name = args.GetString("alarm-name");
        var hash = args.GetString("expected-hash");
        if (string.IsNullOrWhiteSpace(name)) return CommandResult.CreateFailure("Missing required parameter: alarm-name");
        if (string.IsNullOrWhiteSpace(hash)) return CommandResult.CreateFailure("Missing required parameter: expected-hash");
        var dryRun = args.GetBool("dry-run", true);
        var confirm = args.GetBool("confirm", false);
        if (!dryRun && !confirm) return CommandResult.CreateFailure("Deleting a Unified discrete alarm requires dry-run=false and confirm=true after reviewing dry-run output.");
        var error = await UnifiedDiscreteAlarmCommandSupport.OpenAsync(_hmiService, args.GetString("hmi-name"));
        if (error != null) return CommandResult.CreateFailure(error);
        var result = await _hmiService.DeleteDiscreteAlarmAsync(name, hash, dryRun, confirm, args.GetBool("allow-referenced-delete", false));
        return result.Success ? CommandResult.CreateSuccess(result, result.Message) : CommandResult.CreateFailure(result.Message, result);
    }
}
