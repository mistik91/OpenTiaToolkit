using System;

namespace OpenTiaToolkit.Tests.Integration
{
    internal static class Settings
    {
        // Test Project 'MCP_TEST_V21' - V21 local project (primary target)
        public static readonly string TestProjectPath = GetSetting(
            "OPEN_TIA_TEST_PROJECT_PATH",
            "D:\\Siemens\\V21\\MCP_TEST_V21\\MCP_TEST_V21.ap21");

        // Export paths for testing
        public static readonly string TestExportPath = GetSetting(
            "OPEN_TIA_TEST_EXPORT_PATH",
            "D:\\Siemens\\V21\\MCP_TEST_V21\\Exports");

        // Output path for test files (outside project directory to avoid TIA Portal access issues)
        public static readonly string TestOutputPath = GetSetting(
            "OPEN_TIA_TEST_OUTPUT_PATH",
            "D:\\Siemens\\V21\\MCP_TEST_OUTPUT");

        private static string GetSetting(string envVarName, string fallback)
        {
            var configured = Environment.GetEnvironmentVariable(envVarName);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }
    }
}
