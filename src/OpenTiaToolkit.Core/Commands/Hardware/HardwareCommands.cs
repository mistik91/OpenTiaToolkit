using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hardware
{
    internal static class HardwareCommandSupport
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string Required(CommandArgs args, string name)
        {
            var value = args.GetString(name);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Missing required parameter: {name}");
            return value;
        }

        public static HardwareObjectRef RequireTarget(CommandArgs args, string name = "target")
        {
            var raw = args.GetObject(name);
            HardwareObjectRef? target;
            if (raw == null)
            {
                var itemPath = args.GetStringArray("item-path");
                if (itemPath.Length == 1 && itemPath[0].Contains("/"))
                {
                    itemPath = itemPath[0].Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                }
                target = new HardwareObjectRef
                {
                    DeviceName = args.GetString("device-name"),
                    ItemPath = itemPath,
                    ExpectedTypeIdentifier = args.Has("expected-type-identifier") ? args.GetString("expected-type-identifier") : null
                };
            }
            else
            {
                var json = RawJson(raw);
                target = JsonSerializer.Deserialize<HardwareObjectRef>(json, JsonOptions);
            }
            if (target == null || string.IsNullOrWhiteSpace(target.DeviceName))
            {
                throw new ArgumentException($"{name}.deviceName or device-name is required.");
            }
            return target;
        }

        public static IReadOnlyList<HardwarePatchOperation> RequireOperations(CommandArgs args)
        {
            var raw = args.GetObject("operations");
            var json = raw == null && args.Has("operations-path")
                ? File.ReadAllText(args.GetString("operations-path"))
                : raw == null
                    ? throw new ArgumentException("Missing required parameter: operations or operations-path")
                    : RawJson(raw);
            if (json.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("operations", out var operations))
                {
                    throw new ArgumentException("operations object must contain an operations array.");
                }
                json = operations.GetRawText();
            }

            var parsed = JsonSerializer.Deserialize<List<HardwarePatchOperation>>(json, JsonOptions);
            if (parsed == null || parsed.Count == 0)
            {
                throw new ArgumentException("operations must contain at least one hardware operation.");
            }
            return parsed;
        }

        private static string RawJson(object raw)
        {
            if (raw is JsonElement element) return element.GetRawText();
            if (raw is string text) return text;
            return JsonSerializer.Serialize(raw);
        }

        public static CommandParameter[] TargetParameters(string description)
        {
            return new[]
            {
                new CommandParameter("target", CommandParameterType.Object, description + " as a structured hardware target", required: false),
                new CommandParameter("device-name", CommandParameterType.String, description + " device name; CLI-friendly alternative to target", required: false),
                new CommandParameter("item-path", CommandParameterType.StringArray, description + " nested item path; use with device-name", required: false),
                new CommandParameter("expected-type-identifier", CommandParameterType.String, "Optional expected target type identifier guard", required: false)
            };
        }

        public static CommandResult Failure(string action, Exception ex, ILogger logger)
        {
            logger.LogError(ex, "Hardware command failed: {Action}", action);
            return CommandResult.CreateFailure($"{action} failed: {ex.Message}", ex);
        }

        public static string ResolveCsvOutputPath(string outputPath, string defaultFileName)
        {
            var fullPath = Path.GetFullPath(outputPath);
            return Path.HasExtension(fullPath)
                ? fullPath
                : Path.Combine(fullPath, defaultFileName);
        }

        public static string FormatTargetItemPath(HardwareObjectRef? target)
        {
            return target?.ItemPath == null || target.ItemPath.Length == 0
                ? string.Empty
                : string.Join("/", target.ItemPath);
        }

        public static void WriteCsvRow(StreamWriter writer, IEnumerable<string?> values)
        {
            writer.WriteLine(string.Join(",", values.Select(CommandArtifactSupport.EscapeCsv)));
        }
    }

    public sealed class FindHardwareCatalogItemsCommand : ICommand
    {
        private readonly IHardwareService _service;
        public FindHardwareCatalogItemsCommand(IHardwareService service) => _service = service;
        public string Name => "find-hardware-catalog-items";
        public string Description => "Searches installed TIA Portal hardware catalog items by text";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("search-text", CommandParameterType.String, "Catalog search text; blank enumerates the available catalog", required: false, defaultValue: ""),
            new CommandParameter("culture", CommandParameterType.String, "Optional catalog language such as en-US", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var values = await _service.FindCatalogItemsAsync(args.GetString("search-text"), args.Has("culture") ? args.GetString("culture") : null);
                return CommandResult.CreateSuccess(values, $"Found {values.Count} hardware catalog item(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Finding hardware catalog items", ex, logger); }
        }
    }

    public sealed class GetHardwareTreeCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareTreeCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-tree";
        public string Description => "Gets nested hardware devices and device items with reusable hardware target paths";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("device-name", CommandParameterType.String, "Optional device name filter", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var devices = await _service.GetHardwareTreeAsync(args.Has("device-name") ? args.GetString("device-name") : null);
                return CommandResult.CreateSuccess(devices, $"Found {devices.Count} hardware device tree(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware tree", ex, logger); }
        }
    }

    public sealed class GetHardwareIoAddressesCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareIoAddressesCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-io-addresses";
        public string Description => "Lists process-image address objects exposed by hardware device items";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("device-name", CommandParameterType.String, "Optional device name filter", required: false),
            new CommandParameter("attributes", CommandParameterType.StringArray, "Optional named address attributes to read", required: false),
            new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for hardware IO address rows", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var values = await _service.GetIoAddressesAsync(
                    args.Has("device-name") ? args.GetString("device-name") : null,
                    args.Has("attributes") ? args.GetStringArray("attributes") : null);
                var message = $"Found {values.Count} hardware IO address object(s).";
                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(values, message))
                {
                    var outputPath = HardwareCommandSupport.ResolveCsvOutputPath(
                        args.Has("output-path")
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                        "hardware_io_addresses.csv");
                    var bytesWritten = WriteIoAddressesCsv(values, outputPath);
                    var artifact = new CommandArtifactResult
                    {
                        OutputPath = outputPath,
                        Format = "csv",
                        DataKind = "hardware-io-addresses",
                        BytesWritten = bytesWritten,
                        ItemCount = values.Count,
                        InlineOutputSuppressed = !args.Has("output-path"),
                        Reason = args.Has("output-path")
                            ? "output-path was provided"
                            : $"inline result exceeded {CommandArtifactSupport.InlineResultThresholdBytes} bytes",
                        Columns = IoAddressCsvColumns,
                        Metadata = new
                        {
                            deviceName = args.Has("device-name") ? args.GetString("device-name") : null,
                            attributes = args.Has("attributes") ? args.GetStringArray("attributes") : Array.Empty<string>(),
                            rowCount = CountIoAddressCsvRows(values)
                        }
                    };
                    return CommandResult.CreateSuccess(artifact, $"{message}. CSV written to: {outputPath}");
                }

                return CommandResult.CreateSuccess(values, message);
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware IO addresses", ex, logger); }
        }

        private static readonly IReadOnlyList<string> IoAddressCsvColumns = new[]
        {
            "DeviceName",
            "ItemPath",
            "ExpectedTypeIdentifier",
            "AddressIndex",
            "AttributeName",
            "AttributeValue",
            "IsReadOnly"
        };

        private static int CountIoAddressCsvRows(IReadOnlyList<HardwareIoAddressInfo> values)
        {
            return values.Sum(value => Math.Max(1, value.Attributes.Count));
        }

        private static long WriteIoAddressesCsv(IReadOnlyList<HardwareIoAddressInfo> values, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", IoAddressCsvColumns));
                foreach (var address in values)
                {
                    if (address.Attributes.Count == 0)
                    {
                        HardwareCommandSupport.WriteCsvRow(writer, new[]
                        {
                            address.Target.DeviceName,
                            HardwareCommandSupport.FormatTargetItemPath(address.Target),
                            address.Target.ExpectedTypeIdentifier,
                            address.Index.ToString(),
                            string.Empty,
                            string.Empty,
                            string.Empty
                        });
                        continue;
                    }

                    foreach (var attribute in address.Attributes)
                    {
                        HardwareCommandSupport.WriteCsvRow(writer, new[]
                        {
                            address.Target.DeviceName,
                            HardwareCommandSupport.FormatTargetItemPath(address.Target),
                            address.Target.ExpectedTypeIdentifier,
                            address.Index.ToString(),
                            attribute.Name,
                            attribute.Value,
                            attribute.IsReadOnly?.ToString()
                        });
                    }
                }
            }

            return new FileInfo(outputPath).Length;
        }
    }

    public sealed class GetHardwareDriveTelegramsCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareDriveTelegramsCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-drive-telegrams";
        public string Description => "Lists Startdrive telegrams and their IO addresses exposed by hardware drive items";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("device-name", CommandParameterType.String, "Optional drive device name filter", required: false),
            new CommandParameter("address-attributes", CommandParameterType.StringArray, "Optional named telegram address attributes to read", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var values = await _service.GetDriveTelegramsAsync(
                    args.Has("device-name") ? args.GetString("device-name") : null,
                    args.Has("address-attributes") ? args.GetStringArray("address-attributes") : null);
                return CommandResult.CreateSuccess(values, $"Found {values.Count} drive telegram(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware drive telegrams", ex, logger); }
        }
    }

    public sealed class AuditHardwareTagMappingsCommand : ICommand
    {
        private readonly IHardwareService _service;
        public AuditHardwareTagMappingsCommand(IHardwareService service) => _service = service;
        public string Name => "audit-hardware-tag-mappings";
        public string Description => "Audits PLC I/Q tag addresses against discovered hardware IO ranges and optional catalog evidence";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name whose tag tables should be scanned", required: true),
            new CommandParameter("tag-table-filter", CommandParameterType.String, "Optional tag table name/text filter", required: false),
            new CommandParameter("catalog-search-text", CommandParameterType.String, "Optional hardware catalog search text such as Promag", required: false),
            new CommandParameter("max-unmatched-tags", CommandParameterType.Integer, "Maximum unmatched tag entries to include; use -1 for all", required: false, defaultValue: 100),
            new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for unmatched PLC tag rows", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var result = await _service.AuditHardwareTagMappingsAsync(
                    HardwareCommandSupport.Required(args, "plc-name"),
                    args.Has("tag-table-filter") ? args.GetString("tag-table-filter") : null,
                    args.Has("catalog-search-text") ? args.GetString("catalog-search-text") : null,
                    args.Has("max-unmatched-tags") ? args.GetInt("max-unmatched-tags", 100) : 100);
                var message = $"Audited {result.AddressedTagCount} addressed PLC tag(s); {result.UnmatchedTagCount} unmatched.";
                if (args.Has("output-path"))
                {
                    var outputPath = HardwareCommandSupport.ResolveCsvOutputPath(args.GetString("output-path"), "hardware_tag_mapping_unmatched_tags.csv");
                    var bytesWritten = WriteUnmatchedTagsCsv(result, outputPath);
                    var artifact = new CommandArtifactResult
                    {
                        OutputPath = outputPath,
                        Format = "csv",
                        DataKind = "hardware-tag-mapping-unmatched-tags",
                        BytesWritten = bytesWritten,
                        ItemCount = result.UnmatchedTags.Count,
                        InlineOutputSuppressed = false,
                        Reason = "output-path was provided",
                        Columns = UnmatchedTagsCsvColumns,
                        Metadata = new
                        {
                            result.PlcName,
                            result.TagTableFilter,
                            result.CatalogSearchText,
                            result.HardwareDeviceCount,
                            result.HardwareIoRangeCount,
                            result.ScannedTagCount,
                            result.AddressedTagCount,
                            result.MatchedTagCount,
                            result.UnmatchedTagCount,
                            exportedUnmatchedTagCount = result.UnmatchedTags.Count,
                            result.Warnings
                        }
                    };
                    return CommandResult.CreateSuccess(artifact, $"{message}. Unmatched tag CSV written to: {outputPath}");
                }

                return CommandResult.CreateSuccess(result, message);
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Auditing hardware tag mappings", ex, logger); }
        }

        private static readonly IReadOnlyList<string> UnmatchedTagsCsvColumns = new[]
        {
            "TagTableName",
            "TagName",
            "LogicalAddress",
            "Area",
            "StartAddress",
            "Length",
            "EndAddress",
            "MatchedHardwareRange"
        };

        private static long WriteUnmatchedTagsCsv(HardwareTagMappingAuditResult result, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", UnmatchedTagsCsvColumns));
                foreach (var tag in result.UnmatchedTags)
                {
                    HardwareCommandSupport.WriteCsvRow(writer, new[]
                    {
                        tag.TagTableName,
                        tag.TagName,
                        tag.LogicalAddress,
                        tag.Area,
                        tag.StartAddress.ToString(),
                        tag.Length.ToString(),
                        tag.EndAddress.ToString(),
                        tag.MatchedHardwareRange
                    });
                }
            }

            return new FileInfo(outputPath).Length;
        }
    }

    public sealed class GetHardwareItemCapabilitiesCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareItemCapabilitiesCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-item-capabilities";
        public string Description => "Gets available hardware feature services and attributes for one hardware target";
        public CommandSchema Schema => new CommandSchema(Description, Combine(
            HardwareCommandSupport.TargetParameters("Hardware target"),
            new CommandParameter("attributes", CommandParameterType.StringArray, "Optional named attributes to read", required: false)));
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var info = await _service.GetCapabilitiesAsync(HardwareCommandSupport.RequireTarget(args), args.Has("attributes") ? args.GetStringArray("attributes") : null);
                return info == null
                    ? CommandResult.CreateFailure("Hardware target was not found.")
                    : CommandResult.CreateSuccess(info, "Hardware item capabilities retrieved.");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware item capabilities", ex, logger); }
        }

        private static CommandParameter[] Combine(CommandParameter[] target, CommandParameter extra)
        {
            var result = new CommandParameter[target.Length + 1];
            target.CopyTo(result, 0);
            result[target.Length] = extra;
            return result;
        }
    }

    public sealed class GetHardwareFeatureAttributesCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareFeatureAttributesCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-feature-attributes";
        public string Description => "Reads engineering attributes from a hardware feature service such as NetworkInterface";
        public CommandSchema Schema => new CommandSchema(Description, new List<CommandParameter>(HardwareCommandSupport.TargetParameters("Hardware target"))
        {
            new CommandParameter("feature-service", CommandParameterType.String, "Hardware feature service name, such as NetworkInterface", required: true),
            new CommandParameter("attributes", CommandParameterType.StringArray, "Optional named feature attributes to read", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var info = await _service.GetFeatureAttributesAsync(
                    HardwareCommandSupport.RequireTarget(args),
                    HardwareCommandSupport.Required(args, "feature-service"),
                    args.Has("attributes") ? args.GetStringArray("attributes") : null);
                return CommandResult.CreateSuccess(info, "Hardware feature attributes retrieved.");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware feature attributes", ex, logger); }
        }
    }

    public sealed class GetHardwareNetworkItemAttributesCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareNetworkItemAttributesCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-network-item-attributes";
        public string Description => "Reads engineering attributes from a NetworkInterface child such as a node or IO connector";
        public CommandSchema Schema => new CommandSchema(Description, new List<CommandParameter>(HardwareCommandSupport.TargetParameters("Network interface hardware target"))
        {
            new CommandParameter("network-collection", CommandParameterType.String, "NetworkInterface collection name: Nodes, IoControllers, or IoConnectors", required: true),
            new CommandParameter("network-item-index", CommandParameterType.Integer, "Zero-based item index in the network collection", required: false, defaultValue: 0),
            new CommandParameter("attributes", CommandParameterType.StringArray, "Optional named network item attributes to read", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var info = await _service.GetNetworkItemAttributesAsync(
                    HardwareCommandSupport.RequireTarget(args),
                    HardwareCommandSupport.Required(args, "network-collection"),
                    args.Has("network-item-index") ? args.GetInt("network-item-index") : 0,
                    args.Has("attributes") ? args.GetStringArray("attributes") : null);
                return CommandResult.CreateSuccess(info, "Hardware network item attributes retrieved.");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware network item attributes", ex, logger); }
        }
    }

    public sealed class FindHardwareModuleTypesCommand : ICommand
    {
        private readonly IHardwareService _service;
        public FindHardwareModuleTypesCommand(IHardwareService service) => _service = service;
        public string Name => "find-hardware-module-types";
        public string Description => "Finds module type identifiers using the project-level TIA module information provider";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("partial-type-identifier", CommandParameterType.String, "Partial type identifier search value", required: true)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var types = await _service.FindModuleTypesAsync(HardwareCommandSupport.Required(args, "partial-type-identifier"));
                return CommandResult.CreateSuccess(types, $"Found {types.Count} hardware module type identifier(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Finding hardware module types", ex, logger); }
        }
    }

    public sealed class CanPlugHardwareItemCommand : ICommand
    {
        private readonly IHardwareService _service;
        public CanPlugHardwareItemCommand(IHardwareService service) => _service = service;
        public string Name => "can-plug-hardware-item";
        public string Description => "Checks whether a hardware module or submodule can be plugged at a target position";
        public CommandSchema Schema => new CommandSchema(Description, new List<CommandParameter>(HardwareCommandSupport.TargetParameters("Parent hardware target"))
        {
            new CommandParameter("type-identifier", CommandParameterType.String, "TIA hardware type identifier", required: true),
            new CommandParameter("item-name", CommandParameterType.String, "Name for the new hardware item", required: true),
            new CommandParameter("position-number", CommandParameterType.Integer, "Target slot or subslot position", required: true)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var result = await _service.CanPlugAsync(
                    HardwareCommandSupport.RequireTarget(args),
                    HardwareCommandSupport.Required(args, "type-identifier"),
                    HardwareCommandSupport.Required(args, "item-name"),
                    args.GetInt("position-number"));
                return CommandResult.CreateSuccess(result, result.CanPlug ? "Hardware item can be plugged." : "Hardware item cannot be plugged.");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Checking hardware plug operation", ex, logger); }
        }
    }

    public sealed class GetHardwareNetworksCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetHardwareNetworksCommand(IHardwareService service) => _service = service;
        public string Name => "get-hardware-networks";
        public string Description => "Lists hardware network interfaces, nodes, IO controllers and IO connectors";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("device-name", CommandParameterType.String, "Optional device name filter", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var values = await _service.GetNetworksAsync(args.Has("device-name") ? args.GetString("device-name") : null);
                return CommandResult.CreateSuccess(values, $"Found {values.Count} hardware network interface(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Getting hardware networks", ex, logger); }
        }
    }

    public sealed class GetGsdParameterDataCommand : ICommand
    {
        private readonly IHardwareService _service;
        public GetGsdParameterDataCommand(IHardwareService service) => _service = service;
        public string Name => "get-gsd-parameter-data";
        public string Description => "Reads raw parameter data bytes from a GSD or GSDML hardware item";
        public CommandSchema Schema => new CommandSchema(Description, new List<CommandParameter>(HardwareCommandSupport.TargetParameters("GSD device item target"))
        {
            new CommandParameter("dataset-number", CommandParameterType.Integer, "Parameter dataset number", required: true),
            new CommandParameter("byte-offset", CommandParameterType.Integer, "Starting byte offset", required: false, defaultValue: 0),
            new CommandParameter("length", CommandParameterType.Integer, "Number of bytes to read", required: true)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var value = await _service.GetGsdParameterDataAsync(
                    HardwareCommandSupport.RequireTarget(args),
                    args.GetInt("dataset-number"),
                    args.GetInt("byte-offset"),
                    args.GetInt("length"));
                return value == null
                    ? CommandResult.CreateFailure("GSD parameter data could not be read.")
                    : CommandResult.CreateSuccess(value, $"Read {value.Length} GSD parameter byte(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Reading GSD parameter data", ex, logger); }
        }
    }

    public sealed class ApplyHardwarePatchCommand : ICommand
    {
        private readonly IHardwareService _service;
        private readonly IPlcService? _plcService;
        public ApplyHardwarePatchCommand(IHardwareService service, IPlcService? plcService = null)
        {
            _service = service;
            _plcService = plcService;
        }
        public string Name => "apply-hardware-patch";
        public string Description => "Dry-runs or applies transactional generic TIA hardware operations";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("operations", CommandParameterType.Object, "Operations array or { operations: [...] } object", required: false),
            new CommandParameter("operations-path", CommandParameterType.Path, "JSON file containing operations; CLI-friendly alternative to operations", required: false),
            new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate operations in a rolled-back transaction without applying", required: false, defaultValue: true),
            new CommandParameter("confirm-destructive", CommandParameterType.Boolean, "Permit delete and disconnect operations", required: false, defaultValue: false),
            new CommandParameter("transaction-description", CommandParameterType.String, "Undo unit description for TIA Portal", required: false),
            new CommandParameter("plc-name", CommandParameterType.String, "Optional PLC device name used with safety-password", required: false),
            new CommandParameter("safety-password", CommandParameterType.String, "Optional safety program password used to log into the offline safety program before hardware mutations", required: false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var dryRun = !args.Has("dry-run") || args.GetBool("dry-run", true);
                var safetyPassword = args.GetString("safety-password");
                SafetyLoginResult? safetyLogin = null;
                if (!string.IsNullOrEmpty(safetyPassword))
                {
                    if (_plcService == null)
                    {
                        return CommandResult.CreateFailure("Safety login is unavailable because no PLC service is registered.");
                    }

                    var plcName = args.GetString("plc-name");
                    if (string.IsNullOrWhiteSpace(plcName))
                    {
                        return CommandResult.CreateFailure("plc-name is required when safety-password is provided.");
                    }

                    if (!_plcService.IsSoftwareOpen ||
                        !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
                    {
                        var opened = await _plcService.OpenPlcAsync(plcName);
                        if (!opened)
                        {
                            return CommandResult.CreateFailure($"Failed to open PLC software: {plcName}");
                        }
                    }

                    safetyLogin = await _plcService.LoginToSafetyOfflineProgramAsync(safetyPassword);
                    if (!safetyLogin.Success && safetyLogin.PasswordRequired)
                    {
                        return CommandResult.CreateFailure(safetyLogin.Message, new { safetyLogin });
                    }
                }

                var result = await _service.ApplyPatchAsync(
                    HardwareCommandSupport.RequireOperations(args),
                    dryRun,
                    args.Has("confirm-destructive") && args.GetBool("confirm-destructive"),
                    args.Has("transaction-description") ? args.GetString("transaction-description") : null);
                if (result.SafetyBlocked)
                {
                    return CommandResult.CreateFailure(result.SafetyMessage ?? "Hardware patch was blocked.", result);
                }
                var action = dryRun ? "preflighted and rolled back" : "applied";
                if (safetyLogin != null)
                {
                    return CommandResult.CreateSuccess(new { safetyLogin, result }, $"Hardware patch {action}.");
                }
                return CommandResult.CreateSuccess(result, $"Hardware patch {action}.");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Applying hardware patch", ex, logger); }
        }
    }
}
