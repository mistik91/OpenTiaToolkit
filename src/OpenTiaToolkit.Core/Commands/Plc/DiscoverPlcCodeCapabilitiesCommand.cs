using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Plc;

public sealed class DiscoverPlcCodeCapabilitiesCommand : ICommand
{
    private readonly PlcCodeCapabilityDiscoveryService _service;
    public DiscoverPlcCodeCapabilitiesCommand(PlcCodeCapabilityDiscoveryService service) => _service = service;
    public string Name => "discover-plc-code-capabilities";
    public string Description => "Discovers and fingerprints the live TIA, PLC, target, safety, VCI, and import/export evidence before PLC code generation";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("plc-name", CommandParameterType.String, "Exact PLC device name", true),
        new CommandParameter("object-kind", CommandParameterType.String, "Block or Type", false, "Block"),
        new CommandParameter("object-name", CommandParameterType.String, "Exact existing or intended object name", true),
        new CommandParameter("group-path", CommandParameterType.String, "Target subgroup relative to Program blocks or PLC data types; omit for root (PLC-prefixed software-tree paths are accepted)", false),
        new CommandParameter("operation", CommandParameterType.String, "Modify or Create", false, "Modify"),
        new CommandParameter("block-type", CommandParameterType.String, "FB, FC, OB, DB, GlobalDB, InstanceDB, or TYPE when known", false),
        new CommandParameter("requested-language", CommandParameterType.String, "Requested code language such as LAD, FBD, or SCL", false),
        new CommandParameter("requested-format", CommandParameterType.String, "Intended artifact format such as SCL, S7DCL, SimaticML, or VCI", false),
        new CommandParameter("prefer-source-control", CommandParameterType.Boolean, "Rank an observed exact VCI mapping first", false, false)
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcCodeCapabilityDiscovery result = await _service.DiscoverAsync(new PlcCodeCapabilityRequest
        {
            PlcName = args.GetString("plc-name"),
            ObjectKind = string.IsNullOrWhiteSpace(args.GetString("object-kind")) ? "Block" : args.GetString("object-kind"),
            ObjectName = args.GetString("object-name"),
            GroupPath = args.GetString("group-path") ?? string.Empty,
            Operation = string.IsNullOrWhiteSpace(args.GetString("operation")) ? "Modify" : args.GetString("operation"),
            BlockType = args.GetString("block-type"),
            RequestedLanguage = args.GetString("requested-language"),
            RequestedFormat = args.GetString("requested-format"),
            PreferSourceControl = args.GetBool("prefer-source-control")
        }).ConfigureAwait(false);
        string message = result.SelectedRoute == "Blocked"
            ? "Capability discovery completed, but no safe compatible mutation route was observed."
            : result.SelectedRoute == "VCI"
                ? "Capability discovery selected VCI. Use the reported workspace, mapping, format, and status in the guarded VCI synchronization workflow."
                : $"Capability discovery selected {result.SelectedRoute}. Bind capability-id '{result.CapabilityId}' when beginning a direct-import transaction.";
        return CommandResult.CreateSuccess(result, message);
    }
}
