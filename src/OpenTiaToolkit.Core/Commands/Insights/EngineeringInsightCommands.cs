using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Commands.Insights;

public abstract class InsightListCommand : ICommand
{
    protected readonly IEngineeringInsightsService Service;
    protected InsightListCommand(IEngineeringInsightsService service) { Service = service; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("max-results", CommandParameterType.Integer, "Maximum returned rows (1-5000)", false, 500) });
    protected abstract Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync();
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        int maxResults = args.GetInt("max-results", 500);
        if (maxResults < 1 || maxResults > 5000) return CommandResult.CreateFailure("max-results must be between 1 and 5000.");
        IReadOnlyList<EngineeringInsightItem> all = await ReadAsync().ConfigureAwait(false);
        EngineeringInsightItem[] items = all.Take(maxResults).ToArray();
        return CommandResult.CreateSuccess(new { items, totalCount = all.Count, truncated = all.Count > items.Length });
    }
}

public sealed class ListUnifiedAlarmsCommand : InsightListCommand
{
    public ListUnifiedAlarmsCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "list-unified-alarms";
    public override string Description => "Lists Unified HMI alarm classes, analog/discrete alarms, and OPC UA alarm types through the official V21 API.";
    protected override Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync() => Service.ListUnifiedAlarmsAsync();
}

public sealed class GetUnifiedRuntimeSettingsCommand : InsightListCommand
{
    public GetUnifiedRuntimeSettingsCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "get-unified-runtime-settings";
    public override string Description => "Reads scalar Unified HMI runtime settings through the official V21 API.";
    protected override Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync() => Service.ListUnifiedRuntimeSettingsAsync();
}

public sealed class ListUnifiedTextListsCommand : InsightListCommand
{
    public ListUnifiedTextListsCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "list-unified-text-lists";
    public override string Description => "Lists Unified HMI user, system, and graphic text lists through the official V21 API.";
    protected override Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync() => Service.ListUnifiedTextListsAsync();
}

public sealed class GetWatchTableDetailsCommand : InsightListCommand
{
    public GetWatchTableDetailsCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "get-watch-table-details";
    public override string Description => "Reads PLC watch/force tables and their entry metadata through the official V21 API.";
    protected override Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync() => Service.GetWatchTableDetailsAsync();
}

public sealed class GetTechnologyObjectDetailsCommand : InsightListCommand
{
    public GetTechnologyObjectDetailsCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "get-technology-object-details";
    public override string Description => "Reads PLC technology object metadata and parameters through the official V21 API.";
    protected override Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync() => Service.GetTechnologyObjectDetailsAsync();
}

public sealed class GetPlcAlarmTextListDetailsCommand : InsightListCommand
{
    public GetPlcAlarmTextListDetailsCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "get-plc-alarm-text-list-details";
    public override string Description => "Reads PLC alarm system/user text-list metadata through the official V21 API.";
    protected override Task<IReadOnlyList<EngineeringInsightItem>> ReadAsync() => Service.GetPlcAlarmTextListDetailsAsync();
}

public abstract class InsightValidationCommand : ICommand
{
    protected readonly IEngineeringInsightsService Service;
    protected InsightValidationCommand(IEngineeringInsightsService service) { Service = service; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public CommandSchema Schema => new CommandSchema(Description);
    protected abstract Task<EngineeringValidationResult> InspectAsync();
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) =>
        CommandResult.CreateSuccess(await InspectAsync().ConfigureAwait(false));
}

public sealed class ValidateSafetyConfigurationCommand : InsightValidationCommand
{
    public ValidateSafetyConfigurationCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "validate-safety-configuration";
    public override string Description => "Validates official SafetyAdministration availability and offline login state for the selected PLC.";
    protected override Task<EngineeringValidationResult> InspectAsync() => Service.ValidateSafetyAsync();
}

public sealed class GetMultiuserStatusCommand : InsightValidationCommand
{
    public GetMultiuserStatusCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "get-multiuser-status";
    public override string Description => "Reports project/local-session multiuser state and ownership through the official V21 API.";
    protected override Task<EngineeringValidationResult> InspectAsync() => Service.InspectMultiuserAsync();
}

public sealed class InspectProjectSecurityCommand : InsightValidationCommand
{
    public InspectProjectSecurityCommand(IEngineeringInsightsService service) : base(service) { }
    public override string Name => "inspect-project-security";
    public override string Description => "Inspects project security metadata and certificate/password/syslog service availability through V21 Openness.";
    protected override Task<EngineeringValidationResult> InspectAsync() => Service.InspectProjectSecurityAsync();
}
