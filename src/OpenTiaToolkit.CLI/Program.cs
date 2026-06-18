using System;
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
                return await RunReplLoop(serviceProvider, router, logger);
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

        private static async Task<int> RunReplLoop(IServiceProvider serviceProvider, CommandRouter router, ILogger logger)
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

                    // Handle REPL-specific commands
                    if (HandleReplCommands(input, ref shouldExit))
                    {
                        continue;
                    }

                    if (!CommandLineTokenizer.TryTokenize(input, out string[] args, out var parseError))
                    {
                        Console.Error.WriteLine(parseError);
                        lastExitCode = 2;
                        continue;
                    }

                    if (!CliOutputOptions.TryParse(args, out var outputOptions, out var outputError))
                    {
                        Console.Error.WriteLine(outputError);
                        lastExitCode = 2;
                        continue;
                    }

                    args = outputOptions.CommandArgs;

                    // Handle help command
                    if (args.Length > 0 && (args[0] == "help" || args[0] == "?"))
                    {
                        var helpText = args.Length > 1
                            ? router.GetHelp(args[1])
                            : router.GetHelp();
                        Console.WriteLine(helpText);
                        continue;
                    }

                    if (args.Length > 1 && (args[1] == "--help" || args[1] == "-h"))
                    {
                        Console.WriteLine(router.GetHelp(args[0]));
                        continue;
                    }

                    // Execute the command
                    var result = await router.ExecuteAsync(args);

                    // Format and display the result
                    var formattedOutput = CliOutputFormatter.Format(result, outputOptions.Format);
                    Console.WriteLine(formattedOutput);

                    // Track exit code
                    lastExitCode = result.Success ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    lastExitCode = 1;
                }
            }

            return lastExitCode;
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

        private static void ConfigureConsoleEncoding()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Some redirected or legacy hosts reject encoding changes.
            }
        }
    }
}
