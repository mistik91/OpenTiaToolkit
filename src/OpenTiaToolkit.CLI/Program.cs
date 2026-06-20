using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.CLI.Output;
using OpenTiaToolkit.CLI.Routing;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;

namespace OpenTiaToolkit.CLI
{
    /// <summary>
    /// Main entry point for the OpenTiaToolkit CLI application.
    /// </summary>
    public static class Program
    {
        // Helper class for logging since static classes can't be used as generic type arguments
        private class CLIApplication { }

        /// <summary>
        /// Static constructor - registers the TIA Portal assembly resolver BEFORE any types are loaded.
        /// This is critical for proper assembly resolution according to Siemens documentation.
        /// </summary>
        static Program()
        {
            TiaPortalAssemblyResolver.Register();
        }

        public static async Task<int> Main(string[] args)
        {
            if (TryParseScriptMode(args, out var scriptPath, out var scriptError, out _))
            {
                ConfigureConsoleEncoding(configureInput: scriptPath != "-");

                if (scriptError != null)
                {
                    Console.Error.WriteLine(scriptError);
                    return 2;
                }

                return await RunScriptMode(scriptPath!);
            }

            ConfigureConsoleEncoding();

            bool isInteractiveMode = args.Length == 0 ||
                                     System.Array.Exists(args, a => a == "--interactive") ||
                                     System.Array.Exists(args, a => a == "-i");

            if (isInteractiveMode)
            {
                return await RunReplMode();
            }

            return await RunSingleCommandMode(args);
        }

        private static async Task<int> RunSingleCommandMode(string[] args)
        {
            // Set up dependency injection
            var serviceProvider = BuildServiceProvider();

            if (!CliOutputOptions.TryParse(args, out var outputOptions, out var outputError))
            {
                Console.Error.WriteLine(outputError);
                return 2;
            }

            args = outputOptions.CommandArgs;

            // Handle help command
            if (args.Length > 0 && (args[0] == "help" || args[0] == "--help" || args[0] == "-h"))
            {
                var router = serviceProvider.GetRequiredService<CommandRouter>();
                var helpText = args.Length > 1
                    ? router.GetHelp(args[1])
                    : router.GetHelp();
                Console.WriteLine(helpText);
                return 0;
            }

            if (args.Length > 1 && (args[1] == "--help" || args[1] == "-h"))
            {
                var router = serviceProvider.GetRequiredService<CommandRouter>();
                Console.WriteLine(router.GetHelp(args[0]));
                return 0;
            }

            try
            {
                // Execute the command
                var logger = serviceProvider.GetRequiredService<ILogger<CLIApplication>>();
                var router = serviceProvider.GetRequiredService<CommandRouter>();
                var result = await router.ExecuteAsync(args);

                // Format and display the result
                var formattedOutput = CliOutputFormatter.Format(result, outputOptions.Format);
                Console.WriteLine(formattedOutput);

                // Return exit code based on success
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                return 1;
            }
            finally
            {
                // Clean up
                if (serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static async Task<int> RunReplMode()
        {
            var serviceProvider = BuildServiceProvider();

            try
            {
                var logger = serviceProvider.GetRequiredService<ILogger<CLIApplication>>();
                var router = serviceProvider.GetRequiredService<CommandRouter>();

                DisplayWelcomeMessage(router);
                return await RunReplLoop(router, logger);
            }
            finally
            {
                if (serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static void DisplayWelcomeMessage(CommandRouter router)
        {
            Console.WriteLine();
            Console.WriteLine("+-----------------------------------------------------------+");
            Console.WriteLine("|        OpenTia Toolkit - Interactive Mode                 |");
            Console.WriteLine("+-----------------------------------------------------------+");
            Console.WriteLine();
            Console.WriteLine("Connected TIA Portal instances will persist across commands.");
            Console.WriteLine("Type 'help' for available commands or 'exit' to quit.");
            Console.WriteLine();
        }

        private static async Task<int> RunScriptMode(string scriptPath)
        {
            var serviceProvider = BuildServiceProvider();

            try
            {
                var logger = serviceProvider.GetRequiredService<ILogger<CLIApplication>>();
                var router = serviceProvider.GetRequiredService<CommandRouter>();
                return await RunScriptAsync(scriptPath, router, logger);
            }
            finally
            {
                if (serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static async Task<int> RunReplLoop(CommandRouter router, ILogger logger)
        {
            int lastExitCode = 0;
            bool shouldExit = false;

            // Set up Ctrl+C handler
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                shouldExit = true;
                Console.WriteLine("\nExiting...");
            };

            while (!shouldExit)
            {
                try
                {
                    Console.Write("tia> ");
                    string? input = Console.ReadLine();

                    if (input == null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        continue;
                    }

                    var execution = await ExecuteCommandLineAsync(input, router, logger, allowReplCommands: true);
                    shouldExit = execution.ShouldExit;
                    lastExitCode = execution.ExitCode;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    lastExitCode = 1;
                }
            }

            return lastExitCode;
        }

        private static async Task<int> RunScriptAsync(string scriptPath, CommandRouter router, ILogger logger)
        {
            TextReader? reader = null;
            try
            {
                reader = string.Equals(scriptPath, "-", StringComparison.Ordinal)
                    ? Console.In
                    : File.OpenText(scriptPath);

                int lastExitCode = 0;
                int lineNumber = 0;

                while (true)
                {
                    var input = await reader.ReadLineAsync();
                    if (input == null)
                    {
                        break;
                    }

                    lineNumber++;
                    if (IsSkippableScriptLine(input))
                    {
                        continue;
                    }

                    var execution = await ExecuteCommandLineAsync(input, router, logger, allowReplCommands: false);
                    lastExitCode = execution.ExitCode;

                    if (execution.ShouldExit)
                    {
                        return lastExitCode;
                    }

                    if (lastExitCode != 0)
                    {
                        Console.Error.WriteLine($"Script stopped at line {lineNumber}.");
                        return lastExitCode;
                    }
                }

                return lastExitCode;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Failed to read script '{scriptPath}': {ex.Message}");
                return 1;
            }
            finally
            {
                if (reader != null && !string.Equals(scriptPath, "-", StringComparison.Ordinal))
                {
                    reader.Dispose();
                }
            }
        }

        private static async Task<CommandLineExecutionResult> ExecuteCommandLineAsync(
            string input,
            CommandRouter router,
            ILogger logger,
            bool allowReplCommands)
        {
            var shouldExit = false;
            if (allowReplCommands && HandleReplCommands(input, ref shouldExit))
            {
                return new CommandLineExecutionResult(0, shouldExit);
            }

            if (!allowReplCommands && IsScriptExitCommand(input))
            {
                return new CommandLineExecutionResult(0, shouldExit: true);
            }

            if (!CommandLineTokenizer.TryTokenize(input, out string[] args, out var parseError))
            {
                Console.Error.WriteLine(parseError);
                return new CommandLineExecutionResult(2);
            }

            if (!CliOutputOptions.TryParse(args, out var outputOptions, out var outputError))
            {
                Console.Error.WriteLine(outputError);
                return new CommandLineExecutionResult(2);
            }

            args = outputOptions.CommandArgs;

            if (args.Length > 0 && (args[0] == "help" || args[0] == "?"))
            {
                var helpText = args.Length > 1
                    ? router.GetHelp(args[1])
                    : router.GetHelp();
                Console.WriteLine(helpText);
                return new CommandLineExecutionResult(0);
            }

            if (args.Length > 1 && (args[1] == "--help" || args[1] == "-h"))
            {
                Console.WriteLine(router.GetHelp(args[0]));
                return new CommandLineExecutionResult(0);
            }

            var result = await router.ExecuteAsync(args);
            var formattedOutput = CliOutputFormatter.Format(result, outputOptions.Format);
            Console.WriteLine(formattedOutput);
            return new CommandLineExecutionResult(result.Success ? 0 : 1);
        }

        private static bool IsSkippableScriptLine(string input)
        {
            var trimmed = input.Trim();
            return trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal);
        }

        private static bool IsScriptExitCommand(string input)
        {
            var trimmed = input.Trim();
            return string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "quit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HandleReplCommands(string input, ref bool shouldExit)
        {
            string trimmed = input.Trim().ToLowerInvariant();

            switch (trimmed)
            {
                case "exit":
                case "quit":
                    shouldExit = true;
                    Console.WriteLine("Goodbye!");
                    return true;

                case "clear":
                case "cls":
                    Console.Clear();
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryParseScriptMode(
            string[] args,
            out string? scriptPath,
            out string? error,
            out bool isScriptMode)
        {
            scriptPath = null;
            error = null;
            isScriptMode = false;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string? value = null;

                if (arg.StartsWith("--script=", StringComparison.OrdinalIgnoreCase))
                {
                    value = arg.Substring("--script=".Length);
                }
                else if (string.Equals(arg, "--script", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-") && args[i + 1] != "-")
                    {
                        isScriptMode = true;
                        error = "Missing value for global option: --script. Use a script file path or '-' for stdin.";
                        return true;
                    }

                    value = args[++i];
                }
                else
                {
                    continue;
                }

                isScriptMode = true;
                if (scriptPath != null)
                {
                    error = "Global option --script can only be specified once.";
                    return true;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "Missing value for global option: --script. Use a script file path or '-' for stdin.";
                    return true;
                }

                scriptPath = value;
            }

            if (isScriptMode && args.Length > (args[0].StartsWith("--script=", StringComparison.OrdinalIgnoreCase) ? 1 : 2))
            {
                error = "Unexpected argument in script mode. Use: OpenTiaToolkit.CLI.exe --script <file>|-";
            }

            return isScriptMode;
        }

        private sealed class CommandLineExecutionResult
        {
            public CommandLineExecutionResult(int exitCode, bool shouldExit = false)
            {
                ExitCode = exitCode;
                ShouldExit = shouldExit;
            }

            public int ExitCode { get; }

            public bool ShouldExit { get; }
        }

        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();

            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Trace;
                });
                builder.SetMinimumLevel(LogLevel.Warning);
            });

            services.AddOpenTiaToolkitCoreServices();
            services.AddOpenTiaToolkitCommands();

            // Register router
            services.AddSingleton<CommandRouter>();

            return services.BuildServiceProvider();
        }

        private static void ConfigureConsoleEncoding(bool configureInput = true)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                if (configureInput)
                {
                    Console.InputEncoding = Encoding.UTF8;
                }
            }
            catch
            {
                // Some redirected or legacy hosts reject encoding changes.
            }
        }
    }
}
