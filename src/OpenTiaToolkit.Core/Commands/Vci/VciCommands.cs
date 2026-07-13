using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        public static void RequireSyncConfirmation(CommandArgs args, VciSyncDirection direction, bool createMappings)
        {
            if (direction != VciSyncDirection.WorkspaceToProject) return;
            if (!args.GetBool("confirm")) throw new InvalidOperationException("workspace-to-project synchronization mutates TIA and requires confirm=true after mapping, status, direction, and workspace artifacts are reviewed.");
            if (createMappings) throw new InvalidOperationException("workspace-to-project synchronization cannot create mappings implicitly. Connect the exact workspace file first and review its status.");
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

        public static CommandResult BulkResult(string operation, VciBulkSyncResult result, CommandArgs args, string commandName, object? metadata = null)
        {
            var message = $"{operation}: {result.SynchronizedCount} synchronized, {result.MappedCount} new mapping(s), {result.SkippedCount} skipped, {result.FailedCount} failed.";
            object data = result;

            if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(result, message))
            {
                var outputPath = ResolveOutputPath(
                    args.Has("output-path")
                        ? args.GetString("output-path")
                        : CommandArtifactSupport.CreateDefaultArtifactPath(commandName, ".json"),
                    commandName + "_result.json");
                data = CreateJsonArtifact(
                    result,
                    outputPath,
                    commandName + "-result",
                    result.Items.Count,
                    args.Has("output-path"),
                    metadata);
                message += $" JSON written to: {outputPath}";
            }

            return result.FailedCount == 0
                ? CommandResult.CreateSuccess(data, message)
                : CommandResult.CreateFailure(message, data);
        }

        public static string ResolveOutputPath(string outputPath, string defaultFileName)
        {
            return CommandArtifactSupport.ResolveOutputPath(outputPath, defaultFileName);
        }

        public static CommandArtifactResult CreateJsonArtifact(
            object data,
            string outputPath,
            string dataKind,
            int itemCount,
            bool explicitOutputPath,
            object? metadata = null)
        {
            return CommandArtifactSupport.CreateJsonArtifact(
                data,
                outputPath,
                dataKind,
                itemCount,
                explicitOutputPath,
                metadata);
        }

        public static long WriteMappingsCsv(IReadOnlyList<VciMappingInfo> mappings, string outputPath)
        {
            CommandArtifactSupport.EnsureParentDirectory(outputPath);
            using (var writer = new StreamWriter(outputPath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(string.Join(",", MappingCsvColumns));
                foreach (var mapping in mappings)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        mapping.WorkspaceName,
                        mapping.ObjectName,
                        mapping.ObjectType,
                        mapping.DirectoryPath,
                        mapping.FileNameWithoutExtension,
                        mapping.FileFormat,
                        mapping.Status
                    }.Select(CommandArtifactSupport.EscapeCsv)));
                }
            }

            return new FileInfo(outputPath).Length;
        }

        public static readonly IReadOnlyList<string> MappingCsvColumns = new[]
        {
            "WorkspaceName",
            "ObjectName",
            "ObjectType",
            "DirectoryPath",
            "FileNameWithoutExtension",
            "FileFormat",
            "Status"
        };
    }

    public sealed class ListVciWorkspacesCommand : IContextualCommand
    {
        private readonly IVciService _service;
        public ListVciWorkspacesCommand(IVciService service) => _service = service;
        public string Name => "list-vci-workspaces";
        public string Description => "Lists Version Control Interface workspaces in the open project";
        public CommandSchema Schema => new CommandSchema(Description);
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Listing VCI workspaces.");
                var workspaces = await _service.ListWorkspacesAsync();
                context.CompleteCommand(Name, $"Found {workspaces.Count} VCI workspace(s).");
                return CommandResult.CreateSuccess(workspaces, $"Found {workspaces.Count} VCI workspace(s).");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Listing VCI workspaces", ex, logger); }
        }
    }

    public sealed class GetVciWorkspaceCommand : IContextualCommand
    {
        private readonly IVciService _service;
        public GetVciWorkspaceCommand(IVciService service) => _service = service;
        public string Name => "get-vci-workspace";
        public string Description => "Gets details for a Version Control Interface workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true) });
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Reading VCI workspace details.");
                var name = VciCommandSupport.Required(args, "workspace-name");
                var workspace = await _service.GetWorkspaceAsync(name);
                context.CompleteCommand(Name, workspace == null ? $"VCI workspace was not found: {name}" : $"VCI workspace found: {name}");
                return workspace == null
                    ? CommandResult.CreateFailure($"VCI workspace was not found: {name}")
                    : CommandResult.CreateSuccess(workspace, $"VCI workspace found: {name}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Getting VCI workspace", ex, logger); }
        }
    }

    public sealed class CreateVciWorkspaceCommand : IContextualCommand
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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Creating VCI workspace.");
                var workspace = await _service.CreateWorkspaceAsync(
                    VciCommandSupport.Required(args, "workspace-name"),
                    VciCommandSupport.Required(args, "root-path"),
                    VciCommandSupport.Optional(args, "workspace-language"));
                context.CompleteCommand(Name, $"Created VCI workspace: {workspace.Name}");
                return CommandResult.CreateSuccess(workspace, $"Created VCI workspace: {workspace.Name}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Creating VCI workspace", ex, logger); }
        }
    }

    public sealed class DeleteVciWorkspaceCommand : IContextualCommand
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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Deleting VCI workspace.");
                var name = VciCommandSupport.Required(args, "workspace-name");
                if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Deletion requires confirm=true.");
                var deleted = await _service.DeleteWorkspaceAsync(name);
                context.CompleteCommand(Name, deleted ? $"Deleted VCI workspace: {name}" : $"VCI workspace was not found: {name}");
                return deleted ? CommandResult.CreateSuccess(message: $"Deleted VCI workspace: {name}") : CommandResult.CreateFailure($"VCI workspace was not found: {name}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Deleting VCI workspace", ex, logger); }
        }
    }

    public sealed class ListVciMappingsCommand : IContextualCommand
    {
        private readonly IVciService _service;
        public ListVciMappingsCommand(IVciService service) => _service = service;
        public string Name => "list-vci-mappings";
        public string Description => "Lists mapped objects in a Version Control Interface workspace";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("workspace-name", CommandParameterType.String, "Workspace name", true),
            new CommandParameter("output-path", CommandParameterType.Path, "Optional CSV file or folder path for VCI mapping rows")
        });
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Reading VCI workspace mappings.");
                var workspaceName = VciCommandSupport.Required(args, "workspace-name");
                var mappings = await _service.ListMappingsAsync(workspaceName);
                var message = $"Found {mappings.Count} VCI mapping(s).";
                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(mappings, message))
                {
                    context.Checkpoint("Writing VCI mappings CSV artifact.", 85, "write-artifact");
                    var outputPath = VciCommandSupport.ResolveOutputPath(
                        args.Has("output-path")
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                        "vci_mappings.csv");
                    var bytesWritten = VciCommandSupport.WriteMappingsCsv(mappings, outputPath);
                    var artifact = CommandArtifactSupport.CreateCsvArtifact(
                        outputPath,
                        "vci-mappings",
                        bytesWritten,
                        mappings.Count,
                        args.Has("output-path"),
                        VciCommandSupport.MappingCsvColumns,
                        new { workspaceName });
                    context.CompleteCommand(Name, $"{message} CSV written to: {outputPath}");
                    return CommandResult.CreateSuccess(artifact, $"{message} CSV written to: {outputPath}");
                }

                context.CompleteCommand(Name, message);
                return CommandResult.CreateSuccess(mappings, message);
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Listing VCI mappings", ex, logger); }
        }
    }

    public sealed class GetVciBlockFormatsCommand : IContextualCommand
    {
        private readonly IVciService _service;
        public GetVciBlockFormatsCommand(IVciService service) => _service = service;
        public string Name => "get-vci-block-formats";
        public string Description => "Gets VCI-supported workspace file formats for one PLC block";
        public CommandSchema Schema => BlockTargetSchema(Description);
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Reading supported VCI block formats.");
                var formats = await _service.GetSupportedBlockFormatsAsync(
                    VciCommandSupport.Required(args, "plc-name"),
                    VciCommandSupport.Required(args, "block-name"),
                    VciCommandSupport.Optional(args, "block-type"),
                    VciCommandSupport.Optional(args, "group-path"));
                context.CompleteCommand(Name, $"Found {formats.Count} supported VCI format(s).");
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

    public sealed class ExportVciBlockCommand : IContextualCommand
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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Exporting PLC block to VCI workspace.");
                var mapping = await _service.ExportBlockAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Required(args, "relative-directory"), VciCommandSupport.Optional(args, "file-name"), VciCommandSupport.Required(args, "file-format"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                context.CompleteCommand(Name, $"Exported and mapped VCI block: {mapping.ObjectName}");
                return CommandResult.CreateSuccess(mapping, $"Exported and mapped VCI block: {mapping.ObjectName}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Exporting VCI block", ex, logger); }
        }
    }

    public sealed class ConnectVciBlockCommand : IContextualCommand
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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Connecting PLC block to VCI workspace files.");
                var mapping = await _service.ConnectBlockAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Required(args, "relative-directory"), VciCommandSupport.Required(args, "file-name"), VciCommandSupport.Required(args, "file-format"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                context.CompleteCommand(Name, $"Connected VCI block: {mapping.ObjectName}");
                return CommandResult.CreateSuccess(mapping, $"Connected VCI block: {mapping.ObjectName}");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Connecting VCI block", ex, logger); }
        }
    }

    public sealed class GetVciBlockStatusCommand : IContextualCommand
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
            new CommandParameter("group-path", CommandParameterType.String, "Optional block group path"),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm workspace-to-project mutation after review", false, false)
        });
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Reading VCI block status.");
                var status = await _service.GetBlockStatusAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                context.CompleteCommand(Name, status.Message ?? "VCI block status retrieved.");
                return CommandResult.CreateSuccess(status, status.Message ?? "VCI block status retrieved.");
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Getting VCI block status", ex, logger); }
        }
    }

    public sealed class SyncVciBlockCommand : IContextualCommand
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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Synchronizing one VCI block.");
                VciSyncDirection direction = VciCommandSupport.Direction(args);
                VciCommandSupport.RequireSyncConfirmation(args, direction, args.GetBool("create-mapping"));
                var result = await _service.SynchronizeBlockAsync(VciCommandSupport.Required(args, "workspace-name"), VciCommandSupport.Required(args, "plc-name"), VciCommandSupport.Required(args, "block-name"), direction, args.GetBool("create-mapping"), args.GetString("relative-directory"), VciCommandSupport.Optional(args, "file-name"), VciCommandSupport.Optional(args, "file-format"), VciCommandSupport.Optional(args, "block-type"), VciCommandSupport.Optional(args, "group-path"));
                context.CompleteCommand(Name, result.Message ?? "VCI block synchronized.");
                return result.Success ? CommandResult.CreateSuccess(result, result.Message ?? "VCI block synchronized.") : CommandResult.CreateFailure(result.Message ?? "VCI block was not synchronized.", result);
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Synchronizing VCI block", ex, logger); }
        }
    }

    public sealed class SyncVciBlocksCommand : IContextualCommand
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
            new CommandParameter("include-skipped", CommandParameterType.Boolean, "Include per-item results for skipped unmapped objects", false, false),
            new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for the bulk sync result"),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm workspace-to-project mutation after review", false, false)
        });
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Synchronizing selected VCI blocks.");
                var workspaceName = VciCommandSupport.Required(args, "workspace-name");
                var plcName = VciCommandSupport.Required(args, "plc-name");
                var direction = VciCommandSupport.Direction(args);
                VciCommandSupport.RequireSyncConfirmation(args, direction, args.GetBool("create-mappings"));
                var result = await _service.SynchronizeBlocksAsync(workspaceName, plcName, direction, VciCommandSupport.Optional(args, "filter-regex"), args.GetStringArray("block-types"), args.GetBool("create-mappings"), args.GetString("relative-directory"), VciCommandSupport.Optional(args, "file-format"), args.GetBool("include-skipped"));
                context.Checkpoint("Preparing VCI block synchronization result.", 85, "prepare-result");
                context.CompleteCommand(Name, "VCI block synchronization completed.");
                return VciCommandSupport.BulkResult("VCI block synchronization", result, args, Name, new
                {
                    workspaceName,
                    plcName,
                    direction = direction.ToString(),
                    filterRegex = VciCommandSupport.Optional(args, "filter-regex"),
                    blockTypes = args.GetStringArray("block-types"),
                    createMappings = args.GetBool("create-mappings"),
                    relativeDirectory = args.GetString("relative-directory"),
                    fileFormat = VciCommandSupport.Optional(args, "file-format"),
                    includeSkipped = args.GetBool("include-skipped")
                });
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Synchronizing VCI blocks", ex, logger); }
        }
    }

    public sealed class SyncVciPlcCommand : IContextualCommand
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
            new CommandParameter("include-skipped", CommandParameterType.Boolean, "Include per-item results for skipped unmapped objects", false, false),
            new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for the PLC sync result"),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm workspace-to-project mutation after review", false, false)
        });
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Synchronizing PLC objects with VCI workspace.");
                var workspaceName = VciCommandSupport.Required(args, "workspace-name");
                var plcName = VciCommandSupport.Required(args, "plc-name");
                var direction = VciCommandSupport.Direction(args);
                VciCommandSupport.RequireSyncConfirmation(args, direction, args.GetBool("create-mappings"));
                var scope = VciCommandSupport.Scope(args);
                var result = await _service.SynchronizePlcAsync(workspaceName, plcName, direction, scope, args.GetBool("create-mappings"), args.GetString("relative-directory"), VciCommandSupport.Optional(args, "file-format"), args.GetBool("include-skipped"));
                context.Checkpoint("Preparing VCI PLC synchronization result.", 85, "prepare-result");
                context.CompleteCommand(Name, "VCI PLC synchronization completed.");
                return VciCommandSupport.BulkResult("VCI PLC synchronization", result, args, Name, new
                {
                    workspaceName,
                    plcName,
                    direction = direction.ToString(),
                    scope = scope.ToString(),
                    createMappings = args.GetBool("create-mappings"),
                    relativeDirectory = args.GetString("relative-directory"),
                    fileFormat = VciCommandSupport.Optional(args, "file-format"),
                    includeSkipped = args.GetBool("include-skipped")
                });
            }
            catch (Exception ex) { return VciCommandSupport.Failure("Synchronizing VCI PLC", ex, logger); }
        }
    }
}
