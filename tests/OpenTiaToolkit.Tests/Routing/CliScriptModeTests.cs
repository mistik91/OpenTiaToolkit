using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.CLI;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Routing
{
    [TestClass]
    public class CliScriptModeTests
    {
        [TestMethod]
        public async Task Main_ExecutesScriptFileWithoutInteractivePrompt()
        {
            var scriptPath = CreateScript(
                "# comment",
                "",
                "help open-project",
                "exit",
                "unknown-command");

            var result = await RunCliAsync(new[] { "--script", scriptPath });

            Assert.AreEqual(0, result.ExitCode);
            StringAssert.Contains(result.StdOut, "Command: open-project");
            Assert.IsFalse(result.StdOut.Contains("tia>"));
            Assert.IsFalse(result.StdOut.Contains("Interactive Mode"));
            Assert.IsFalse(result.StdOut.Contains("Unknown command"));
            Assert.AreEqual(string.Empty, result.StdErr);
        }

        [TestMethod]
        public async Task Main_StopsScriptAtFirstFailingCommand()
        {
            var scriptPath = CreateScript(
                "help",
                "unknown-command",
                "help open-project");

            var result = await RunCliAsync(new[] { "--script", scriptPath });

            Assert.AreEqual(1, result.ExitCode);
            StringAssert.Contains(result.StdOut, "Unknown command: 'unknown-command'");
            Assert.IsFalse(result.StdOut.Contains("Command: open-project"));
            StringAssert.Contains(result.StdErr, "Script stopped at line 2.");
        }

        [TestMethod]
        public async Task Main_ReturnsUsageErrorWhenScriptValueIsMissing()
        {
            var result = await RunCliAsync(new[] { "--script" });

            Assert.AreEqual(2, result.ExitCode);
            StringAssert.Contains(result.StdErr, "Missing value");
        }

        [TestMethod]
        public async Task Main_ReadsScriptFromStdinWhenPathIsDash()
        {
            var result = await RunCliAsync(
                new[] { "--script", "-" },
                "help open-project" + Environment.NewLine);

            Assert.AreEqual(0, result.ExitCode);
            StringAssert.Contains(result.StdOut, "Command: open-project");
            Assert.AreEqual(string.Empty, result.StdErr);
        }

        [DataTestMethod]
        [DataRow("\uFEFF")]
        [DataRow("\u00EF\u00BB\u00BF")]
        [DataRow("\u00B4\u2557\u2510")]
        public async Task Main_StripsBomFromFirstScriptCommand(string bom)
        {
            var result = await RunCliAsync(
                new[] { "--script", "-" },
                bom + "help open-project" + Environment.NewLine);

            Assert.AreEqual(0, result.ExitCode);
            StringAssert.Contains(result.StdOut, "Command: open-project");
            Assert.AreEqual(string.Empty, result.StdErr);
        }

        private static string CreateScript(params string[] lines)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".commands.txt");
            File.WriteAllLines(path, lines, Encoding.UTF8);
            return path;
        }

        private static async Task<CliRunResult> RunCliAsync(string[] args, string? stdin = null)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            var originalIn = Console.In;
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                if (stdin != null)
                {
                    Console.SetIn(new StringReader(stdin));
                }

                var exitCode = await Program.Main(args);
                return new CliRunResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                Console.SetIn(originalIn);
            }
        }

        private sealed class CliRunResult
        {
            public CliRunResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut;
                StdErr = stdErr;
            }

            public int ExitCode { get; }

            public string StdOut { get; }

            public string StdErr { get; }
        }
    }
}
