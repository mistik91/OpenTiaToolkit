using System;

namespace OpenTiaToolkit.Tests.Integration
{
    internal static class Settings
    {
        // Test Project 'MCP_TEST' - V19 local project (primary target)
        public const string TestProjectPath = "D:\\Siemens\\V19\\MCP_TEST\\MCP_TEST.ap19";

        // Export paths for testing
        public const string TestExportPath = "D:\\Siemens\\V19\\MCP_TEST\\Exports";

        // Output path for test files (outside project directory to avoid TIA Portal access issues)
        public const string TestOutputPath = "D:\\Siemens\\V19\\MCP_TEST_OUTPUT";
    }
}
