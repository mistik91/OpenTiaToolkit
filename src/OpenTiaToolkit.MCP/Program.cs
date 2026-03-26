using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core;
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

            services.AddOpenTiaToolkitCoreServices();
            services.AddOpenTiaToolkitCommands();

            // Register MCP-specific services
            services.AddSingleton<ToolRegistry>();
            services.AddSingleton<McpServerHost>();

            return services.BuildServiceProvider();
        }

        // Helper class for logging since static classes can't be used as generic type arguments
        private class MCPApplication { }
    }
}
