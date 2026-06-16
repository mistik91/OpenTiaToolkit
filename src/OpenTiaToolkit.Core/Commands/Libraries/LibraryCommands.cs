using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Libraries;

internal static class LibraryCommandSupport
{
    public static string Required(CommandArgs args, string name)
    {
        var value = args.GetString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required parameter: {name}");
        }

        return value;
    }

    public static string? Optional(CommandArgs args, string name)
    {
        var value = args.GetString(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static CommandResult Failure(string operation, Exception ex, ILogger logger)
    {
        logger.LogError(ex, "Library command failed: {Operation}", operation);
        return CommandResult.CreateFailure($"{operation} failed: {ex.Message}", ex);
    }

    public static CommandResult ListResult<T>(
        string message,
        IReadOnlyList<T> items,
        CommandArgs args,
        string commandName,
        string dataKind)
    {
        if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(items, message))
        {
            var outputPath = CommandArtifactSupport.ResolveOutputPath(
                args.Has("output-path")
                    ? args.GetString("output-path")
                    : CommandArtifactSupport.CreateDefaultArtifactPath(commandName, ".json"),
                commandName + ".json");
            var artifact = CommandArtifactSupport.CreateJsonArtifact(
                items,
                outputPath,
                dataKind,
                items.Count,
                args.Has("output-path"));
            return CommandResult.CreateSuccess(artifact, $"{message} JSON written to: {outputPath}");
        }

        return CommandResult.CreateSuccess(items, message);
    }
}

public sealed class ListGlobalLibrariesCommand : ICommand
{
    private readonly ILibraryService _service;
    public ListGlobalLibrariesCommand(ILibraryService service) => _service = service;
    public string Name => "list-global-libraries";
    public string Description => "Lists known and open TIA Portal global libraries";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for library rows")
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var libraries = await _service.ListGlobalLibrariesAsync();
            return LibraryCommandSupport.ListResult($"Found {libraries.Count} global library/libraries.", libraries, args, Name, "global-libraries");
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Listing global libraries", ex, logger); }
    }
}

public sealed class OpenGlobalLibraryCommand : ICommand
{
    private readonly ILibraryService _service;
    public OpenGlobalLibraryCommand(ILibraryService service) => _service = service;
    public string Name => "open-global-library";
    public string Description => "Opens a TIA Portal global library by file path";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("path", CommandParameterType.Path, "Global library file path", true),
        new CommandParameter("read-only", CommandParameterType.Boolean, "Open the library read-only", false, true),
        new CommandParameter("upgrade", CommandParameterType.Boolean, "Open with upgrade", false, false)
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var library = await _service.OpenGlobalLibraryAsync(
                LibraryCommandSupport.Required(args, "path"),
                args.GetBool("read-only", true),
                args.GetBool("upgrade"));
            return CommandResult.CreateSuccess(library, $"Opened global library: {library?.Name ?? args.GetString("path")}");
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Opening global library", ex, logger); }
    }
}

public sealed class CloseGlobalLibraryCommand : ICommand
{
    private readonly ILibraryService _service;
    public CloseGlobalLibraryCommand(ILibraryService service) => _service = service;
    public string Name => "close-global-library";
    public string Description => "Closes an open user global library by name or path";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("global-library", CommandParameterType.String, "Open global library name or path", true)
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var name = LibraryCommandSupport.Required(args, "global-library");
            var closed = await _service.CloseGlobalLibraryAsync(name);
            return closed
                ? CommandResult.CreateSuccess(message: $"Closed global library: {name}")
                : CommandResult.CreateFailure($"Global library was not found or cannot be closed: {name}");
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Closing global library", ex, logger); }
    }
}

public sealed class ListMasterCopiesCommand : ICommand
{
    private readonly ILibraryService _service;
    public ListMasterCopiesCommand(ILibraryService service) => _service = service;
    public string Name => "list-master-copies";
    public string Description => "Lists master copies in the project library or an open global library";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("library-scope", CommandParameterType.String, "Library scope: project or global", false, "project"),
        new CommandParameter("global-library", CommandParameterType.String, "Open global library name or path when library-scope=global"),
        new CommandParameter("folder-path", CommandParameterType.String, "Optional folder path inside MasterCopyFolder"),
        new CommandParameter("recursive", CommandParameterType.Boolean, "List nested folders recursively", false, true),
        new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for master copy rows")
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var items = await _service.ListMasterCopiesAsync(
                args.GetString("library-scope", "project"),
                LibraryCommandSupport.Optional(args, "global-library"),
                LibraryCommandSupport.Optional(args, "folder-path"),
                args.GetBool("recursive", true));
            return LibraryCommandSupport.ListResult($"Found {items.Count} master copy/copies.", items, args, Name, "library-master-copies");
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Listing master copies", ex, logger); }
    }
}

public sealed class CopyFromMasterCopyCommand : ICommand
{
    private readonly ILibraryService _service;
    public CopyFromMasterCopyCommand(ILibraryService service) => _service = service;
    public string Name => "copy-from-master-copy";
    public string Description => "Copies a library master copy into a PLC block group with dry-run and confirmation gates";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("library-scope", CommandParameterType.String, "Library scope: project or global", false, "project"),
        new CommandParameter("global-library", CommandParameterType.String, "Open global library name or path when library-scope=global"),
        new CommandParameter("master-copy-path", CommandParameterType.String, "Master copy path inside MasterCopyFolder", true),
        new CommandParameter("plc-name", CommandParameterType.String, "Target PLC device name", true),
        new CommandParameter("target-group-path", CommandParameterType.String, "Optional target PLC block group path"),
        new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate and return the planned copy without applying", false, true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Explicitly confirm a non-dry-run copy", false, false)
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var result = await _service.CopyFromMasterCopyAsync(
                args.GetString("library-scope", "project"),
                LibraryCommandSupport.Required(args, "master-copy-path"),
                LibraryCommandSupport.Required(args, "plc-name"),
                LibraryCommandSupport.Optional(args, "target-group-path"),
                LibraryCommandSupport.Optional(args, "global-library"),
                args.GetBool("dry-run", true),
                args.GetBool("confirm"));
            return result.Success
                ? CommandResult.CreateSuccess(result, result.Message)
                : CommandResult.CreateFailure(result.Message, result);
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Copying from master copy", ex, logger); }
    }
}

public sealed class ListLibraryTypesCommand : ICommand
{
    private readonly ILibraryService _service;
    public ListLibraryTypesCommand(ILibraryService service) => _service = service;
    public string Name => "list-library-types";
    public string Description => "Lists library types in the project library or an open global library";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("library-scope", CommandParameterType.String, "Library scope: project or global", false, "project"),
        new CommandParameter("global-library", CommandParameterType.String, "Open global library name or path when library-scope=global"),
        new CommandParameter("folder-path", CommandParameterType.String, "Optional folder path inside TypeFolder"),
        new CommandParameter("recursive", CommandParameterType.Boolean, "List nested folders recursively", false, true),
        new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for library type rows")
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var items = await _service.ListLibraryTypesAsync(
                args.GetString("library-scope", "project"),
                LibraryCommandSupport.Optional(args, "global-library"),
                LibraryCommandSupport.Optional(args, "folder-path"),
                args.GetBool("recursive", true));
            return LibraryCommandSupport.ListResult($"Found {items.Count} library type(s).", items, args, Name, "library-types");
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Listing library types", ex, logger); }
    }
}

public sealed class GetLibraryTypeVersionsCommand : ICommand
{
    private readonly ILibraryService _service;
    public GetLibraryTypeVersionsCommand(ILibraryService service) => _service = service;
    public string Name => "get-library-type-versions";
    public string Description => "Lists versions for a library type";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("library-scope", CommandParameterType.String, "Library scope: project or global", false, "project"),
        new CommandParameter("global-library", CommandParameterType.String, "Open global library name or path when library-scope=global"),
        new CommandParameter("type-path", CommandParameterType.String, "Library type path inside TypeFolder", true),
        new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for version rows")
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var items = await _service.GetLibraryTypeVersionsAsync(
                args.GetString("library-scope", "project"),
                LibraryCommandSupport.Required(args, "type-path"),
                LibraryCommandSupport.Optional(args, "global-library"));
            return LibraryCommandSupport.ListResult($"Found {items.Count} library type version(s).", items, args, Name, "library-type-versions");
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Getting library type versions", ex, logger); }
    }
}

public sealed class CheckLibraryUpdatesCommand : ICommand
{
    private readonly ILibraryService _service;
    public CheckLibraryUpdatesCommand(ILibraryService service) => _service = service;
    public string Name => "check-library-updates";
    public string Description => "Reports project instances that are outdated against the project library or an open global library";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("library-scope", CommandParameterType.String, "Library scope: project or global", false, "project"),
        new CommandParameter("global-library", CommandParameterType.String, "Open global library name or path when library-scope=global"),
        new CommandParameter("include-up-to-date", CommandParameterType.Boolean, "Include up-to-date instances in the report", false, false),
        new CommandParameter("output-path", CommandParameterType.Path, "Optional JSON file or folder path for update messages")
    });

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var result = await _service.CheckLibraryUpdatesAsync(
                args.GetString("library-scope", "project"),
                LibraryCommandSupport.Optional(args, "global-library"),
                args.GetBool("include-up-to-date"));

            if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(result, result.Message))
            {
                var outputPath = CommandArtifactSupport.ResolveOutputPath(
                    args.Has("output-path")
                        ? args.GetString("output-path")
                        : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".json"),
                    "library_update_check.json");
                var artifact = CommandArtifactSupport.CreateJsonArtifact(
                    result,
                    outputPath,
                    "library-update-check",
                    result.Messages.Count,
                    args.Has("output-path"));
                return CommandResult.CreateSuccess(artifact, $"{result.Message} JSON written to: {outputPath}");
            }

            return CommandResult.CreateSuccess(result, result.Message);
        }
        catch (Exception ex) { return LibraryCommandSupport.Failure("Checking library updates", ex, logger); }
    }
}
