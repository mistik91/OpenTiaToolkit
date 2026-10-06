using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Worker.V21;

internal static class CommandContractMapper
{
    private static readonly HashSet<string> OnlineCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "download-software", "start-cpu", "stop-cpu", "run-plc-code-behavior-probe", "recover-plc-code-behavior-probe"
    };

    private static readonly HashSet<string> ExplicitMutatingCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "run-plc-code-probe"
    };

    private static readonly HashSet<string> DestructiveCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "delete-blocks", "delete-vci-workspace", "delete-hmi-data-log", "delete-hmi-data-logs",
        "remove-hmi-logging-tag", "remove-hmi-logging-tags", "delete-unified-discrete-alarm",
        "delete-external-source", "close-project", "close-tia-portal"
    };

    private static readonly string[] MutationPrefixes =
    {
        "add-", "apply-", "archive-", "connect-vci-", "copy-", "create-", "delete-", "disconnect-",
        "commit-", "download-", "ensure-", "find-replace-", "generate-", "import-", "modify-", "open-global-library", "probe-hmi-faceplate-",
        "remove-", "retrieve-", "save-", "set-", "start-", "stop-", "sync-"
    };

    public static CommandDescriptor ToDescriptor(ICommand command)
    {
        CommandRisk risk = ResolveRisk(command.Name);
        var parameters = command.Schema.Parameters.Select(ToParameter).ToList();
        if (risk != CommandRisk.ReadOnly &&
            !parameters.Any(parameter => string.Equals(parameter.Name, "confirm", StringComparison.OrdinalIgnoreCase)))
        {
            parameters.Add(new CommandParameterContract
            {
                Name = "confirm",
                Kind = CommandParameterKind.Boolean,
                Description = "Explicitly authorizes this state-changing command after review of its target and impact.",
                Required = false,
                DefaultValue = JsonSerializer.SerializeToElement(false)
            });
        }

        if (risk == CommandRisk.ReadOnly && SupportsPaging(command.Name))
        {
            parameters.Add(new CommandParameterContract
            {
                Name = "page-size",
                Kind = CommandParameterKind.Integer,
                Description = "Optional maximum number of rows returned in this page (1-1000)."
            });
            parameters.Add(new CommandParameterContract
            {
                Name = "continuation-token",
                Kind = CommandParameterKind.String,
                Description = "Opaque continuation token returned by a previous page."
            });
        }

        return new CommandDescriptor
        {
            Name = command.Name,
            Description = command.Description,
            Category = ResolveCategory(command.GetType().Namespace),
            Risk = risk,
            SupportsPreview = command.Schema.Parameters.Any(parameter =>
                string.Equals(parameter.Name, "dry-run", StringComparison.OrdinalIgnoreCase)),
            Parameters = parameters.ToArray()
        };
    }

    private static bool SupportsPaging(string name)
    {
        return name.StartsWith("list-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("search-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "get-blocks", StringComparison.OrdinalIgnoreCase);
    }

    private static CommandParameterContract ToParameter(CommandParameter parameter)
    {
        return new CommandParameterContract
        {
            Name = parameter.Name,
            Kind = (CommandParameterKind)(int)parameter.Type,
            Description = parameter.Description,
            Required = parameter.Required,
            DefaultValue = parameter.DefaultValue == null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(parameter.DefaultValue)
        };
    }

    private static CommandRisk ResolveRisk(string name)
    {
        if (OnlineCommands.Contains(name)) return CommandRisk.Online;
        if (DestructiveCommands.Contains(name)) return CommandRisk.Destructive;
        if (ExplicitMutatingCommands.Contains(name)) return CommandRisk.Mutating;
        return MutationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ? CommandRisk.Mutating
            : CommandRisk.ReadOnly;
    }

    private static string ResolveCategory(string? commandNamespace)
    {
        if (string.IsNullOrWhiteSpace(commandNamespace)) return "Other";
        int separator = commandNamespace!.LastIndexOf('.');
        return separator < 0 ? commandNamespace : commandNamespace.Substring(separator + 1);
    }
}
