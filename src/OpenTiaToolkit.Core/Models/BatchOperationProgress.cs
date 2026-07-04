namespace OpenTiaToolkit.Core.Models
{
    /// <summary>
    /// Progress update for service-layer batch operations.
    /// </summary>
    public sealed class BatchOperationProgress
    {
        public BatchOperationProgress(
            string message,
            int completed,
            int? total = null,
            string? stage = null)
        {
            Message = message;
            Completed = completed;
            Total = total;
            Stage = stage;
        }

        public string Message { get; }

        public int Completed { get; }

        public int? Total { get; }

        public string? Stage { get; }
    }
}
