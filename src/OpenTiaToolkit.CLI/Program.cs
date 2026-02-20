using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.CLI.Output;
using OpenTiaToolkit.CLI.Routing;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Commands.Devices;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Services;

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

            try
            {
                // Execute the command
                var logger = serviceProvider.GetRequiredService<ILogger<CLIApplication>>();
                var router = serviceProvider.GetRequiredService<CommandRouter>();
                var result = await router.ExecuteAsync(args);

                // Format and display the result
                var formattedOutput = TableOutputFormatter.Format(result);
                Console.WriteLine(formattedOutput);

                // Return exit code based on success
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
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
            Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        OpenTia Toolkit - Interactive Mode                 ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
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
                    string input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        continue;
                    }

                    // Handle REPL-specific commands
                    if (HandleReplCommands(input, ref shouldExit))
                    {
                        continue;
                    }

                    // Parse input into args
                    string[] args = ParseReplInput(input);

                    // Handle help command
                    if (args.Length > 0 && (args[0] == "help" || args[0] == "?"))
                    {
                        var helpText = args.Length > 1
                            ? router.GetHelp(args[1])
                            : router.GetHelp();
                        Console.WriteLine(helpText);
                        continue;
                    }

                    // Execute the command
                    var result = await router.ExecuteAsync(args);

                    // Format and display the result
                    var formattedOutput = TableOutputFormatter.Format(result);
                    Console.WriteLine(formattedOutput);

                    // Track exit code
                    lastExitCode = result.Success ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    lastExitCode = 1;
                }
            }

            return lastExitCode;
        }

        private static bool HandleReplCommands(string input, ref bool shouldExit)
        {
            string trimmed = input.Trim().ToLower();

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
                    return false;

                default:
                    return false;
            }
        }

        private static string[] ParseReplInput(string input)
        {
            // Simple argument parser that handles quoted strings
            // This is a basic implementation that supports space-separated arguments
            // with double-quoted strings treated as single arguments

            var args = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            System.Text.StringBuilder currentArg = new System.Text.StringBuilder();

            foreach (char c in input)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (currentArg.Length > 0)
                    {
                        args.Add(currentArg.ToString());
                        currentArg.Clear();
                    }
                }
                else
                {
                    currentArg.Append(c);
                }
            }

            // Add the last argument
            if (currentArg.Length > 0)
            {
                args.Add(currentArg.ToString());
            }

            return args.ToArray();
        }

        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();

            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register core services
            services.AddSingleton<IPortalService, PortalService>();
            services.AddSingleton<IPlcService, PlcService>();

            // Register all Phase 1 commands
            services.AddSingleton<ICommand, ConnectPortalCommand>();
            services.AddSingleton<ICommand, DisconnectPortalCommand>();
            services.AddSingleton<ICommand, PortalStatusCommand>();
            services.AddSingleton<ICommand, OpenProjectCommand>();
            services.AddSingleton<ICommand, CloseProjectCommand>();
            services.AddSingleton<ICommand, ListProjectsCommand>();
            services.AddSingleton<ICommand, GetProjectInfoCommand>();
            services.AddSingleton<ICommand, ListDevicesCommand>();

            // Register all Phase 2 commands
            services.AddSingleton<ICommand, OpenTiaPortalCommand>();
            services.AddSingleton<ICommand, CloseTiaPortalCommand>();
            services.AddSingleton<ICommand, ListInstancesCommand>();
            services.AddSingleton<ICommand, ConnectInstanceCommand>();
            services.AddSingleton<ICommand, SaveProjectCommand>();
            services.AddSingleton<ICommand, SaveAsProjectCommand>();
            services.AddSingleton<ICommand, GetProjectTreeCommand>();

            // Register all Phase 3 commands
            services.AddSingleton<ICommand, GetDeviceInfoCommand>();
            services.AddSingleton<ICommand, GetDeviceItemInfoCommand>();
            services.AddSingleton<ICommand, OpenPlcCommand>();
            services.AddSingleton<ICommand, GetSoftwareTreeCommand>();
            services.AddSingleton<ICommand, CompileSoftwareCommand>();
            services.AddSingleton<ICommand, ExportPlcCommand>();
            services.AddSingleton<ICommand, GetBlocksCommand>();
            services.AddSingleton<ICommand, ListPlcsCommand>();
            services.AddSingleton<ICommand, ListHmisCommand>();

            // Register all Phase 4 commands (Advanced Block Operations)
            services.AddSingleton<ICommand, DeleteBlocksCommand>();
            services.AddSingleton<ICommand, GetBlockInfoCommand>();
            services.AddSingleton<ICommand, ExportBlockCommand>();
            services.AddSingleton<ICommand, SearchBlocksCommand>();
            // services.AddSingleton<ICommand, CreateBlockCommand>(); // Disabled

            // Register all Phase 5 commands (Find and Replace)
            services.AddSingleton<ICommand, FindAndReplaceTagsCommand>();
            // services.AddSingleton<ICommand, FindAndReplaceDbMembersCommand>(); // Disabled per requirements
            // services.AddSingleton<ICommand, FindAndReplaceUdtMembersCommand>(); // Disabled per requirements

            // Register Phase 5 commands - Tag Management
            services.AddSingleton<ICommand, CreateTagCommand>();

            // Register test command (for testing)
            services.AddSingleton<ICommand, TestFindReplaceCommand>();
            services.AddSingleton<ICommand, ListDbMembersCommand>();

            // Register router
            services.AddSingleton<CommandRouter>();

            return services.BuildServiceProvider();
        }
    }
}
