using OpenTiaToolkit.Core.Models;
using System;

namespace OpenTiaToolkit.Core.Commands
{
    internal sealed class BatchProgressBridge : IProgress<BatchOperationProgress>
    {
        private readonly CommandExecutionContext _context;
        private readonly double _startPercentage;
        private readonly double _endPercentage;

        public BatchProgressBridge(
            CommandExecutionContext context,
            double startPercentage,
            double endPercentage)
        {
            _context = context;
            _startPercentage = startPercentage;
            _endPercentage = endPercentage;
        }

        public void Report(BatchOperationProgress value)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress(value.Message, ScalePercentage(value), value.Stage);
        }

        private double? ScalePercentage(BatchOperationProgress value)
        {
            if (value.Total == null || value.Total <= 0)
            {
                return null;
            }

            var ratio = Math.Max(0, Math.Min(1, value.Completed / (double)value.Total.Value));
            return _startPercentage + ((_endPercentage - _startPercentage) * ratio);
        }
    }
}
