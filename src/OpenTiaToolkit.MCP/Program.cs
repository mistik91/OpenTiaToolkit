using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Commands.Devices;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.MCP.Server;

namespace OpenTiaToolkit.MCP
{
    /// <summary>
    /// Main entry point for the OpenTiaToolkit MCP server.
    /// </summary>
    public static class Program
    {
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
            // Set up dependency injection
            var serviceProvider = BuildServiceProvider();

            try
            {
                var logger = serviceProvider.GetRequiredService<ILogger<MCPApplication>>();
                var toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
                var serverHost = serviceProvider.GetRequiredService<McpServerHost>();

                logger.LogInformation("OpenTiaToolkit MCP Server starting...");
                logger.LogInformation("Registered {ToolCount} tools", toolRegistry.Tools.Count);

                // Start the MCP server (reads from stdin, writes to stdout)
                var exitCode = await serverHost.StartAsync();

                logger.LogInformation("MCP Server shutting down with exit code: {ExitCode}", exitCode);
                return exitCode;
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

            // Register all Phase 4 commands
            services.AddSingleton<ICommand, ExportBlockCommand>();
            services.AddSingleton<ICommand, DeleteBlocksCommand>();
            services.AddSingleton<ICommand, SearchBlocksCommand>();
            services.AddSingleton<ICommand, GetBlockInfoCommand>();
            // services.AddSingleton<ICommand, CreateBlockCommand>(); // Disabled

            // Register Phase 4.5 commands (SIMATIC SD Export and Type Export)
            services.AddSingleton<ICommand, ExportBlockAsDocumentsCommand>();
            services.AddSingleton<ICommand, ExportBlocksAsDocumentsCommand>();
            services.AddSingleton<ICommand, ExportTypeCommand>();
            services.AddSingleton<ICommand, ExportTypesCommand>();

            // Register Phase 4.6 commands - Import (mirrors Export)
            services.AddSingleton<ICommand, ImportBlockCommand>();
            services.AddSingleton<ICommand, ImportBlocksCommand>();
            services.AddSingleton<ICommand, ImportTypeCommand>();
            services.AddSingleton<ICommand, ImportTypesCommand>();
            services.AddSingleton<ICommand, ImportBlockFromDocumentsCommand>();
            services.AddSingleton<ICommand, ImportBlocksFromDocumentsCommand>();

            // Register all Phase 5 commands - Find and Replace
            services.AddSingleton<ICommand, FindAndReplaceTagsCommand>();
            // services.AddSingleton<ICommand, FindAndReplaceDbMembersCommand>(); // Disabled per requirements
            // services.AddSingleton<ICommand, FindAndReplaceUdtMembersCommand>(); // Disabled per requirements

            // Register Phase 5 commands - Tag Management
            services.AddSingleton<ICommand, CreateTagCommand>();
            services.AddSingleton<ICommand, UpdateTagsFromCsvCommand>();

            // Register MCP-specific services
            services.AddSingleton<ToolRegistry>();
            services.AddSingleton<McpServerHost>();

            return services.BuildServiceProvider();
        }

        // Helper class for logging since static classes can't be used as generic type arguments
        private class MCPApplication { }
    }
}

