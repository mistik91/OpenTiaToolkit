using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    internal static class EngineeringFailure
    {
        internal static IEnumerable<Exception> Causes(Exception exception)
        {
            yield return exception;
            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    foreach (var cause in Causes(inner)) yield return cause;
            }
            else if (exception.InnerException != null)
                foreach (var cause in Causes(exception.InnerException)) yield return cause;
        }

        internal static bool IsSessionLoss(Exception exception) => Causes(exception).Any(cause =>
            cause is EngineeringSessionUnavailableException ||
            IsSessionLossType(cause.GetType().FullName) ||
            cause is ObjectDisposedException disposed &&
                (disposed.ObjectName ?? string.Empty).StartsWith("Siemens.Engineering.", StringComparison.Ordinal));

        internal static bool IsSessionLossType(string? name) => name == "Siemens.Engineering.NonRecoverableException" ||
            name == "Siemens.Engineering.EngineeringObjectDisposedException";

        internal static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            return exception;
        }

        internal static string Describe(Exception exception) => string.Join(" --> ", Causes(exception)
            .Where(cause => !(cause is TargetInvocationException))
            .Select(cause => cause.Message).Where(message => !string.IsNullOrWhiteSpace(message)).Distinct());
    }

    public sealed class EngineeringSessionUnavailableException : InvalidOperationException
    {
        internal EngineeringSessionUnavailableException(string message, Exception? inner = null)
            : base(message, inner) { }
    }
}
