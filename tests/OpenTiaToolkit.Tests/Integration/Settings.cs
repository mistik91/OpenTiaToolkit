using System;
using System.IO;

namespace OpenTiaToolkit.Tests.Integration
{
    internal static class Settings
    {
        // Integration tests intentionally require caller-provided project paths.
        // Falling back to a developer workstation path can mutate the wrong TIA project.
        public static string TestProjectPath => GetRequiredPath("OPEN_TIA_TEST_PROJECT_PATH");

        // Export paths for testing
        public static string TestExportPath => GetSetting(
            "OPEN_TIA_TEST_EXPORT_PATH",
            Path.Combine(Path.GetTempPath(), "OTT", "Exports"));

        // Output path for test files (outside project directory to avoid TIA Portal access issues)
        public static string TestOutputPath => GetSetting(
            "OPEN_TIA_TEST_OUTPUT_PATH",
            Path.Combine(Path.GetTempPath(), "OTT", "Output"));

        // Unified HMI-specific validation project
        public static string UnifiedHmiProjectPath => GetRequiredPath("OPEN_TIA_TEST_HMI_PROJECT_PATH");

        public static string UnifiedHmiName => GetSetting(
            "OPEN_TIA_TEST_UNIFIED_HMI_NAME",
            "HMI_1");

        public static bool AllowDestructiveProjectTests => IsEnabled(
            "OPEN_TIA_ALLOW_DESTRUCTIVE_INTEGRATION_TESTS");

        private static string GetSetting(string envVarName, string fallback)
        {
            var configured = Environment.GetEnvironmentVariable(envVarName);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }

        private static string GetRequiredPath(string envVarName)
        {
            var configured = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            throw new InvalidOperationException(
                $"{envVarName} must be set to run OpenTiaToolkit integration tests.");
        }

        private static bool IsEnabled(string envVarName)
        {
            var configured = Environment.GetEnvironmentVariable(envVarName);
            return bool.TryParse(configured, out var enabled) && enabled;
        }
    }
}
