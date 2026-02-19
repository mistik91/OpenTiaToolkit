using System;

namespace OpenTiaToolkit.Tests.Integration
{
    internal static class Settings
    {
        // Test Project 'MCP_TEST_V21' - V21 local project (primary target)
        public const string TestProjectPath = "D:\\Siemens\\V21\\MCP_TEST_V21\\MCP_TEST_V21.ap21";

        // Export paths for testing
        public const string TestExportPath = "D:\\Siemens\\V21\\MCP_TEST_V21\\Exports";

        // Output path for test files (outside project directory to avoid TIA Portal access issues)
        public const string TestOutputPath = "D:\\Siemens\\V21\\MCP_TEST_OUTPUT";
    }
}
