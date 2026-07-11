using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.CLI.Routing;

public sealed class RemoteCommandRouter
{
    private readonly IToolkitClient _session;
    private readonly IReadOnlyDictionary<string, CommandDescriptor> _commands;

    public RemoteCommandRouter(IToolkitClient session)
    {
        _session = session;
        _commands = session.Commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, CommandDescriptor> Commands => _commands;

    public async Task<CommandOutcome> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            return Failure("No command specified. Use 'help' to see available commands.", ErrorCode.InvalidRequest);
        }

        if (!_commands.TryGetValue(args[0], out CommandDescriptor command))
        {
            return Failure($"Unknown command: '{args[0]}'.", ErrorCode.NotFound);
        }

        IReadOnlyDictionary<string, JsonElement>? arguments = ParseArguments(args.Skip(1).ToArray(), command, out string? error);
        if (error != null) return Failure(error, ErrorCode.ValidationFailed);
        return await _session.ExecuteAsync(new CommandInvocation
        {
            CommandName = command.Name,
            Arguments = arguments!
        }, cancellationToken).ConfigureAwait(false);
    }

    public object GetCommandCatalog()
    {
        var commands = _commands.Values.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(command => new
            {
                name = command.Name,
                description = command.Description,
                category = DisplayCategory(command.Category),
                risk = command.Risk.ToString(),
                supportsPreview = command.SupportsPreview,
                parameters = command.Parameters.Select(parameter => new
                {
                    name = parameter.Name,
                    type = parameter.Kind.ToString(),
                    required = parameter.Required,
                    defaultValue = parameter.DefaultValue,
                    description = parameter.Description
                })
            }).ToArray();
        return new
        {
            schemaVersion = "2.0",
            protocolVersion = OpenTiaToolkit.Contracts.Protocol.ProtocolConstants.Version,
            commandCount = commands.Length,
            commands
        };
    }

    public string GetHelp(string? commandName = null)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            var lines = new List<string>
            {
                "OpenTiaToolkit CLI - isolated TIA Portal automation",
                "",
                "Usage: OpenTiaToolkit.CLI.exe <command> [options] [--format table|json]",
                "       OpenTiaToolkit.CLI.exe --script <file>|-",
                "",
                "Available commands:"
            };
            foreach (var group in _commands.Values
                .OrderBy(command => DisplayCategory(command.Category))
                .ThenBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                .GroupBy(command => DisplayCategory(command.Category)))
            {
                lines.Add(string.Empty);
                lines.Add(group.Key + ":");
                foreach (CommandDescriptor command in group)
                {
                    lines.Add($"  {command.Name,-25} {command.Description}");
                }
            }

            lines.AddRange(new[]
            {
                "", "Global options:",
                "  --format table|json       Select human-readable or JSON output.",
                "  --script <file>|-         Execute commands in one worker-backed session.",
                "", "Use 'help <command>' or '<command> --help' for command details."
            });
            return string.Join(Environment.NewLine, lines);
        }

        string lookup = commandName!.TrimStart('-');
        if (!_commands.TryGetValue(lookup, out CommandDescriptor descriptor))
            return $"Unknown command: '{commandName}'";
        var detail = new List<string>
        {
            $"Command: {descriptor.Name}",
            $"Description: {descriptor.Description}",
            $"Risk: {descriptor.Risk}",
            string.Empty
        };
        if (descriptor.Parameters.Count == 0) detail.Add("No parameters.");
        else
        {
            detail.Add("Parameters:");
            foreach (CommandParameterContract parameter in descriptor.Parameters)
            {
                string required = parameter.Required ? "required" : "optional";
                detail.Add($"  --{parameter.Name,-20} {parameter.Kind,-12} {required}");
                detail.Add("    " + parameter.Description);
                detail.Add(string.Empty);
            }
        }

        return string.Join(Environment.NewLine, detail);
    }

    private static IReadOnlyDictionary<string, JsonElement>? ParseArguments(
        string[] args,
        CommandDescriptor command,
        out string? error)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var parameters = command.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        error = null;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Unexpected positional argument: {argument}";
                return null;
            }

            int prefix = argument.StartsWith("--", StringComparison.Ordinal) ? 2 : 1;
            string token = argument.Substring(prefix);
            int equals = token.IndexOf('=');
            string name = equals >= 0 ? token.Substring(0, equals) : token;
            if (!parameters.TryGetValue(name, out CommandParameterContract parameter))
            {
                error = $"Unknown parameter: --{name}";
                return null;
            }

            string? raw = equals >= 0 ? token.Substring(equals + 1) : null;
            if (raw == null && index + 1 < args.Length && ShouldConsume(args[index + 1], parameter.Kind))
                raw = args[++index];
            if (raw == null && parameter.Kind == CommandParameterKind.Boolean) raw = "true";
            if (raw == null)
            {
                error = $"Missing value for parameter: --{name}";
                return null;
            }

            if (!TryConvert(raw, parameter.Kind, out JsonElement value, out error)) return null;
            values[name] = value;
        }

        foreach (CommandParameterContract parameter in command.Parameters.Where(parameter => parameter.Required))
        {
            if (!values.ContainsKey(parameter.Name))
            {
                error = $"Missing required parameter: {parameter.Name}";
                return null;
            }
        }

        return values;
    }

    private static bool ShouldConsume(string value, CommandParameterKind kind)
    {
        if (!value.StartsWith("-", StringComparison.Ordinal)) return true;
        return kind == CommandParameterKind.Integer && int.TryParse(value, out int parsed) && parsed < 0;
    }

    private static bool TryConvert(string raw, CommandParameterKind kind, out JsonElement value, out string? error)
    {
        error = null;
        try
        {
            switch (kind)
            {
                case CommandParameterKind.Boolean:
                    if (!bool.TryParse(raw, out bool boolean)) throw new FormatException("Expected true or false.");
                    value = JsonSerializer.SerializeToElement(boolean);
                    return true;
                case CommandParameterKind.Integer:
                    if (!int.TryParse(raw, out int integer)) throw new FormatException("Expected an integer.");
                    value = JsonSerializer.SerializeToElement(integer);
                    return true;
                case CommandParameterKind.StringArray:
                    value = JsonSerializer.SerializeToElement(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).ToArray());
                    return true;
                case CommandParameterKind.Object:
                case CommandParameterKind.ObjectArray:
                    using (JsonDocument document = JsonDocument.Parse(raw)) value = document.RootElement.Clone();
                    return true;
                default:
                    value = JsonSerializer.SerializeToElement(raw);
                    return true;
            }
        }
        catch (Exception ex)
        {
            value = default;
            error = $"Invalid value for --{kind}: {ex.Message}";
            return false;
        }
    }

    private static CommandOutcome Failure(string message, ErrorCode code)
    {
        return new CommandOutcome
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            Success = false,
            Message = message,
            Error = new ErrorDetail { Code = code, Message = message },
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string DisplayCategory(string category) => category switch
    {
        "Plc" => "PLC",
        "Hmi" => "Unified HMI",
        "Vci" => "VCI",
        "Text" => "Project Texts",
        _ => category
    };
}
