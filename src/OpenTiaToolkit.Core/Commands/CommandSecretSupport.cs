using System;

namespace OpenTiaToolkit.Core.Commands
{
    internal static class CommandSecretSupport
    {
        public const string DefaultSafetyPasswordEnvironmentVariable = "OPENTIA_SAFETY_PASSWORD";

        public static bool TryResolveOptionalEnvironmentSecret(
            CommandArgs args,
            string parameterName,
            string defaultEnvironmentVariable,
            out string? secret,
            out string? error)
        {
            secret = null;
            error = null;
            var explicitReference = args.Has(parameterName);
            var environmentVariable = explicitReference
                ? args.GetString(parameterName)
                : defaultEnvironmentVariable;

            if (string.IsNullOrWhiteSpace(environmentVariable))
            {
                error = $"Parameter {parameterName} must name an environment variable; raw secret values are not accepted.";
                return false;
            }

            secret = Environment.GetEnvironmentVariable(environmentVariable!);
            if (explicitReference && string.IsNullOrEmpty(secret))
            {
                error = $"Environment variable '{environmentVariable}' referenced by {parameterName} is not set.";
                return false;
            }

            return true;
        }
    }
}
