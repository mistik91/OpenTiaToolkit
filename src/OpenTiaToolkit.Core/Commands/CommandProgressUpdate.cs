namespace OpenTiaToolkit.Core.Commands
{
    /// <summary>
    /// Progress update emitted by long-running commands.
    /// </summary>
    public sealed class CommandProgressUpdate
    {
        public CommandProgressUpdate(string message, double? percentage = null, string? stage = null)
        {
            Message = message;
            Percentage = percentage;
            Stage = stage;
        }

        public string Message { get; }

        public double? Percentage { get; }

        public string? Stage { get; }
    }
}
