using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Hmi;

internal sealed class CaptureHmiScreenCommand : IContextualCommand
{
    private readonly HmiWebViewSession _sessions;
    private readonly EngineeringSessionContext _session;

    public CaptureHmiScreenCommand(HmiWebViewSession sessions, EngineeringSessionContext session)
    {
        _sessions = sessions;
        _session = session;
    }

    public string Name => "capture-hmi-screen";
    public string Description => "Captures the open Unified HMI Next Gen editor viewport as a PNG";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("instance-id", CommandParameterType.Integer,
            "Capture-ready TIA Portal process ID, defaults to the attached instance", required: false),
        new CommandParameter("target-id", CommandParameterType.String,
            "DevTools editor target ID when multiple HMI editors are open", required: false),
        new CommandParameter("output-path", CommandParameterType.Path,
            "Destination PNG path, defaults to a temporary artifact", required: false)
    });

    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) =>
        ExecuteAsync(args, logger, CommandExecutionContext.None);

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
    {
        try
        {
            context.ThrowIfCancellationRequested();
            var attachedId = _session.AttachedPortalProcessId;
            var instanceId = args.Has("instance-id") ? args.GetInt("instance-id") : attachedId;
            if (!instanceId.HasValue || instanceId.Value < 1)
                return CommandResult.CreateFailure("Connect to the capture-ready TIA instance or specify its instance-id.");
            if (attachedId.HasValue && attachedId != instanceId)
                return CommandResult.CreateFailure("The requested instance-id differs from the attached TIA instance.");

            var requestedPath = args.GetString("output-path");
            var path = string.IsNullOrWhiteSpace(requestedPath)
                ? CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".png")
                : Path.GetFullPath(requestedPath);
            if (!".png".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
                return CommandResult.CreateFailure("The output path must end in .png.");
            if (File.Exists(path))
                return CommandResult.CreateFailure("The output PNG already exists. Choose a different output path.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var (endpoint, browserPath) = _sessions.GetEndpoint(instanceId.Value);
            var image = await HmiWebViewCapture.CaptureAsync(endpoint, browserPath, args.GetString("target-id"), timeout.Token)
                .ConfigureAwait(false);

            context.ThrowIfCancellationRequested();
            WritePng(path, image.Png);

            return CommandResult.CreateSuccess(new CommandArtifactResult
            {
                OutputPath = path,
                Format = "png",
                DataKind = "unified-hmi-editor-capture",
                BytesWritten = image.Png.Length,
                Metadata = new { instanceId, targetId = image.TargetId, image.Width, image.Height, captureScope = "editor-viewport" }
            }, "Captured the Unified HMI editor viewport.");
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return CommandResult.CreateFailure("HMI capture timed out. Ensure the screen canvas is visible inside the editor and the inspector is collapsed.");
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            logger.LogDebug(ex, "HMI WebView capture failed.");
            return CommandResult.CreateFailure(ex.Message);
        }
    }

    private static void WritePng(string path, byte[] png)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var staged = Path.Combine(directory, ".hmi-capture-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(staged, png);
            File.Move(staged, path);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }
}
