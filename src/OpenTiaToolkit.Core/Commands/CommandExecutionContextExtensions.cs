namespace OpenTiaToolkit.Core.Commands
{
    public static class CommandExecutionContextExtensions
    {
        public static void StartCommand(this CommandExecutionContext context, string commandName, string? message = null)
        {
            context.ThrowIfCancellationRequested();
            context.ReportProgress(message ?? $"Starting {commandName}.", 0, "starting");
        }

        public static void Checkpoint(this CommandExecutionContext context, string message, double? percentage = null, string? stage = null)
        {
            context.ThrowIfCancellationRequested();
            context.ReportProgress(message, percentage, stage);
        }

        public static void CompleteCommand(this CommandExecutionContext context, string commandName, string? message = null)
        {
            context.ReportProgress(message ?? $"Completed {commandName}.", 100, "completed");
        }
    }
}
