using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Vci
{
    internal static class VciCommandSupport
    {
        public static string Required(CommandArgs args, string name)
        {
            var value = args.GetString(name);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Missing required parameter: {name}");
            return value;
        }

        public static string? Optional(CommandArgs args, string name)
        {
            var value = args.GetString(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public static VciSyncDirection Direction(CommandArgs args)
        {
            var value = Required(args, "direction").Replace("-", string.Empty).Replace("_", string.Empty);
            if (value.Equals("projecttoworkspace", StringComparison.OrdinalIgnoreCase)) return VciSyncDirection.ProjectToWorkspace;
            if (value.Equals("workspacetoproject", StringComparison.OrdinalIgnoreCase)) return VciSyncDirection.WorkspaceToProject;
            throw new ArgumentException("direction must be project-to-workspace or workspace-to-project");
        }

        public static VciPlcScope Scope(CommandArgs args)
        {
            var value = args.GetString("scope", "blocks").Replace("-", string.Empty).Replace("_", string.Empty);
            if (value.Equals("blocks", StringComparison.OrdinalIgnoreCase)) return VciPlcScope.Blocks;
            if (value.Equals("blocksandtypes", StringComparison.OrdinalIgnoreCase)) return VciPlcScope.BlocksAndTypes;
            if (value.Equals("allsupported", StringComparison.OrdinalIgnoreCase)) return VciPlcScope.AllSupported;
            throw new ArgumentException("scope must be blocks, blocks-and-types, or all-supported");
        }

        public static CommandResult Failure(string operation, Exception ex, ILogger logger)
        {
            logger.LogError(ex, "VCI command failed: {Operation}", operation);
            return CommandResult.CreateFailure($"{operation} failed: {ex.Message}", ex);
        }

        public static CommandResult BulkResult(string operation, VciBulkSyncResult result)
        {
            var message = $"{operation}: {result.SynchronizedCount} synchronized, {result.MappedCount} new mapping(s), {result.SkippedCount} skipped, {result.FailedCount} failed.";
            return result.FailedCount == 0
                ? CommandResult.CreateSuccess(result, message)
                : CommandResult.CreateFailure(message, result);
        }
    }

    public sealed class ListVciWorkspacesCommand : ICommand
    {
        private readonly IVciService _service;
        public ListVciWorkspacesCommand(IVciService service) => _service = service;
        public string Name => "list-vci-workspaces";
        public string Description => "Lists Version Control Interface workspaces in the open project";
        public CommandSchema Schema => new CommandSchema(Description);
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var workspaces = await _service.ListWorkspacesAsync();
                return CommandResult.CreateSuccess(workspaces, $"Found {workspaces.Count} VCI workspace(s).");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Listing VCI workspaces", ex, logger); }
        }
    }

    public sealed class GetVciWorkspaceCommand : ICommand
    {
        private readonly IVciService _service;
        public GetVciWorkspaceCommand(IVciService service) => _service = service;
        public string Name => "get-vci-workspace";
        public string Description => "Gets details for a Version Control Interface workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true) });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var name = VciCommandSupport.Required(args, "workspace-name");
                var workspace = await _service.GetWorkspaceAsync(name);
                return workspace == null
                    ? CommandResult.CreateFailure($"VCI workspace was not found: {name}")
                    : CommandResult.CreateSuccess(workspace, $"VCI workspace found: {name}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Getting VCI workspace", ex, logger); }
        }
    }

    public sealed class CreateVciWorkspaceCommand : ICommand
    {
        private readonly IVciService _service;
        public CreateVciWorkspaceCommand(IVciService service) => _service = service;
        public string Name => "create-vci-workspace";
        public string Description => "Creates a Version Control Interface workspace in the open project";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("root-path", CommandParameterType.Path, "Workspace filesystem root", true),
            new CommandParameter("workspace-language", CommandParameterType.String, "Optional culture such as en-US")
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var workspace = await _service.CreateWorkspaceAsync(
                    VciCommandSupport.Required(args, "workspace-name"),
                    VciCommandSupport.Required(args, "root-path"),
                    VciCommandSupport.Optional(args, "workspace-language"));
                return CommandResult.CreateSuccess(workspace, $"Created VCI workspace: {workspace.Name}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Creating VCI workspace", ex, logger); }
        }
    }

    public sealed class DeleteVciWorkspaceCommand : ICommand
    {
        private readonly IVciService _service;
        public DeleteVciWorkspaceCommand(IVciService service) => _service = service;
        public string Name => "delete-vci-workspace";
        public string Description => "Deletes a Version Control Interface workspace definition";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm deletion", true)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var name = VciCommandSupport.Required(args, "workspace-name");
                if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Deletion requires confirm=true.");
                var deleted = await _service.DeleteWorkspaceAsync(name);
                return deleted ? CommandResult.CreateSuccess(message: $"Deleted VCI workspace: {name}") : CommandResult.CreateFailure($"VCI workspace was not found: {name}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Deleting VCI workspace", ex, logger); }
        }
    }

    public sealed class ListVciMappingsCommand : ICommand
    {
        private readonly IVciService _service;
        public ListVciMappingsCommand(IVciService service) => _service = service;
        public string Name => "list-vci-mappings";
        public string Description => "Lists mapped objects in a Version Control Interface workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true) });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var mappings = await _service.ListMappingsAsync(VciCommandSupport.Required(args, "workspace-name"));
                return CommandResult.CreateSuccess(mappings, $"Found {mappings.Count} VCI mapping(s).");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Listing VCI mappings", ex, logger); }
        }
    }

    public sealed class GetVciBlockFormatsCommand : ICommand
    {
        private readonly IVciService _service;
        public GetVciBlockFormatsCommand(IVciService service) => _service = service;
        public string Name => "get-vci-block-formats";
        public string Description => "Gets VCI-supported workspace file formats for one PLC block";
        public CommandSchema Schema => BlockTargetSchema(Description);
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var formats = await _service.GetSupportedBlockFormatsAsync(
                    VciCommandSupport.Required(args, "plc-name"),
                    VciCommandSupport.Required(args, "block-name"),
                    VciCommandSupport.Optional(args, "block-type"),
                    VciCommandSupport.Optional(args, "group-path"));
                return CommandResult.CreateSuccess(formats, $"Found {formats.Count} supported VCI format(s).");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Getting VCI block formats", ex, logger); }
        }

        internal static CommandSchema BlockTargetSchema(string description)
        {
            return new CommandSchema(description, new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
                new CommandParameter("block-name", CommandParameterType.String, "Exact block name", true),
                new CommandParameter("block-type", CommandParameterType.String, "Optional block type such as FC or DB"),
                new CommandParameter("group-path", CommandParameterType.String, "Optional block group path")
            });
        }
    }

    public sealed class ExportVciBlockCommand : ICommand
    {
        private readonly IVciService _service;
        public ExportVciBlockCommand(IVciService service) => _service = service;
        public string Name => "export-vci-block";
        public string Description => "Exports one PLC block into a VCI workspace and creates its mapping";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
            new CommandParameter("block-name", CommandParameterType.String, "Exact block name", true),
            new CommandParameter("relative-directory", CommandParameterType.String, "Path relative to workspace root", true),
            new CommandParameter("file-name", CommandParameterType.String, "File name without extension; defaults to block name"),
            new CommandParameter("file-format", CommandParameterType.String, "Format returned by get-vci-block-formats", true),
            new CommandParameter("block-type", CommandParameterType.String, "Optional block type such as FC or DB"),
            new CommandParameter("group-path", CommandParameterType.String, "Optional block group path")
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var mapping = await _service.ExportBlockAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Required(args, "relative-directory"), VciCommandSupport.Optional(args, "file-name"), VciCommandSupport.Required(args, "file-format"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                return CommandResult.CreateSuccess(mapping, $"Exported and mapped VCI block: {mapping.ObjectName}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Exporting VCI block", ex, logger); }
        }
    }

    public sealed class ConnectVciBlockCommand : ICommand
    {
        private readonly IVciService _service;
        public ConnectVciBlockCommand(IVciService service) => _service = service;
        public string Name => "connect-vci-block";
        public string Description => "Connects one PLC block to existing VCI workspace files";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
            new CommandParameter("block-name", CommandParameterType.String, "Exact block name", true),
            new CommandParameter("relative-directory", CommandParameterType.String, "Path relative to workspace root", true),
            new CommandParameter("file-name", CommandParameterType.String, "Existing file name without extension", true),
            new CommandParameter("file-format", CommandParameterType.String, "Format returned by get-vci-block-formats", true),
            new CommandParameter("block-type", CommandParameterType.String, "Optional block type such as FC or DB"),
            new CommandParameter("group-path", CommandParameterType.String, "Optional block group path")
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var mapping = await _service.ConnectBlockAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Required(args, "relative-directory"), VciCommandSupport.Required(args, "file-name"), VciCommandSupport.Required(args, "file-format"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                return CommandResult.CreateSuccess(mapping, $"Connected VCI block: {mapping.ObjectName}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Connecting VCI block", ex, logger); }
        }
    }

    public sealed class GetVciBlockStatusCommand : ICommand
    {
        private readonly IVciService _service;
        public GetVciBlockStatusCommand(IVciService service) => _service = service;
        public string Name => "get-vci-block-status";
        public string Description => "Gets VCI comparison status for one mapped PLC block";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
            new CommandParameter("block-name", CommandParameterType.String, "Exact block name", true),
            new CommandParameter("block-type", CommandParameterType.String, "Optional block type"),
            new CommandParameter("group-path", CommandParameterType.String, "Optional block group path")
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var status = await _service.GetBlockStatusAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                return CommandResult.CreateSuccess(status, status.Message ?? "VCI block status retrieved.");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Getting VCI block status", ex, logger); }
        }
    }

    public sealed class SyncVciBlockCommand : ICommand
    {
        private readonly IVciService _service;
        public SyncVciBlockCommand(IVciService service) => _service = service;
        public string Name => "sync-vci-block";
        public string Description => "Synchronizes one mapped PLC block with a VCI workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
            new CommandParameter("block-name", CommandParameterType.String, "Exact block name", true),
            new CommandParameter("direction", CommandParameterType.String, "project-to-workspace or workspace-to-project", true),
            new CommandParameter("create-mapping", CommandParameterType.Boolean, "Export and map an unmapped item for project-to-workspace", false, false),
            new CommandParameter("relative-directory", CommandParameterType.String, "Path relative to workspace root", false, ""),
            new CommandParameter("file-name", CommandParameterType.String, "Optional file name without extension"),
            new CommandParameter("file-format", CommandParameterType.String, "Optional supported file format"),
            new CommandParameter("block-type", CommandParameterType.String, "Optional block type"),
            new CommandParameter("group-path", CommandParameterType.String, "Optional block group path")
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var result = await _service.SynchronizeBlockAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Direction(args), args.GetBool("create-mapping"), args.GetString("relative-directory"), VciCommandSupport.Optional(args, "file-name"), VciCommandSupport.Optional(args, "file-format"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                return result.Success ? CommandResult.CreateSuccess(result, result.Message ?? "VCI block synchronized.") : CommandResult.CreateFailure(result.Message ?? "VCI block was not synchronized.", result);
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Synchronizing VCI block", ex, logger); }
        }
    }

    public sealed class SyncVciBlocksCommand : ICommand
    {
        private readonly IVciService _service;
        public SyncVciBlocksCommand(IVciService service) => _service = service;
        public string Name => "sync-vci-blocks";
        public string Description => "Synchronizes selected PLC blocks with a VCI workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
            new CommandParameter("direction", CommandParameterType.String, "project-to-workspace or workspace-to-project", true),
            new CommandParameter("filter-regex", CommandParameterType.String, "Optional block-name regular expression"),
            new CommandParameter("block-types", CommandParameterType.StringArray, "Optional set of types such as FC, DB"),
            new CommandParameter("create-mappings", CommandParameterType.Boolean, "Export and map unmapped items for project-to-workspace", false, false),
            new CommandParameter("relative-directory", CommandParameterType.String, "Base path relative to workspace root", false, ""),
            new CommandParameter("file-format", CommandParameterType.String, "Optional supported file format"),
            new CommandParameter("include-skipped", CommandParameterType.Boolean, "Include per-item results for skipped unmapped objects", false, false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var result = await _service.SynchronizeBlocksAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Direction(args), VciCommandSupport.Optional(args, "filter-regex"), args.GetStringArray("block-types"), args.GetBool("create-mappings"), args.GetString("relative-directory"), VciCommandSupport.Optional(args, "file-format"), args.GetBool("include-skipped"));
                return VciCommandSupport.BulkResult("VCI block synchronization", result);
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Synchronizing VCI blocks", ex, logger); }
        }
    }

    public sealed class SyncVciPlcCommand : ICommand
    {
        private readonly IVciService _service;
        public SyncVciPlcCommand(IVciService service) => _service = service;
        public string Name => "sync-vci-plc";
        public string Description => "Synchronizes supported objects of a PLC with a VCI workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
            new CommandParameter("direction", CommandParameterType.String, "project-to-workspace or workspace-to-project", true),
            new CommandParameter("scope", CommandParameterType.String, "blocks, blocks-and-types, or all-supported", false, "blocks"),
            new CommandParameter("create-mappings", CommandParameterType.Boolean, "Export and map unmapped items for project-to-workspace", false, false),
            new CommandParameter("relative-directory", CommandParameterType.String, "Base path relative to workspace root", false, ""),
            new CommandParameter("file-format", CommandParameterType.String, "Optional supported file format"),
            new CommandParameter("include-skipped", CommandParameterType.Boolean, "Include per-item results for skipped unmapped objects", false, false)
        });
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            try
            {
                var result = await _service.SynchronizePlcAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Direction(args), VciCommandSupport.Scope(args), args.GetBool("create-mappings"), args.GetString("relative-directory"), VciCommandSupport.Optional(args, "file-format"), args.GetBool("include-skipped"));
                return VciCommandSupport.BulkResult("VCI PLC synchronization", result);
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Synchronizing VCI PLC", ex, logger); }
        }
    }
}
