using System;
using System.Threading;

namespace OpenTiaToolkit.Core.Commands
{
    /// <summary>
    /// Optional execution context for hosts that can provide cancellation and progress reporting.
    /// </summary>
    public sealed class CommandExecutionContext
    {
        public CommandExecutionContext(CancellationToken cancellationToken, IProgress<CommandProgressUpdate>? progress = null)
        {
            CancellationToken = cancellationToken;
            Progress = progress;
        }

        public static CommandExecutionContext None { get; } = new CommandExecutionContext(CancellationToken.None);

        public CancellationToken CancellationToken { get; }

        public IProgress<CommandProgressUpdate>? Progress { get; }

        public void ThrowIfCancellationRequested()
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        public void ReportProgress(string message, double? percentage = null, string? stage = null)
        {
            Progress?.Report(new CommandProgressUpdate(message, percentage, stage));
        }
    }
}
