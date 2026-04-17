using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
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
            try
            {
                using var host = BuildHost(args);
                await host.RunAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                return 1;
            }
        }

        private static IHost BuildHost(string[] args)
        {
            var minimumLogLevel = ResolveMinimumLogLevel();
            var hostBuilder = Host.CreateDefaultBuilder(args);

            hostBuilder.ConfigureLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Trace;
                });
                builder.SetMinimumLevel(minimumLogLevel);
            });

            hostBuilder.ConfigureServices(services =>
            {
                services.AddOpenTiaToolkitCoreServices();
                services.AddOpenTiaToolkitCommands();
                services.AddSingleton<ToolRegistry>();
                services
                    .AddMcpServer(options =>
                    {
                        options.ServerInfo = new Implementation
                        {
                            Name = "OpenTiaToolkit",
                            Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                        };
                    })
                    .WithStdioServerTransport()
                    .WithListToolsHandler((request, cancellationToken) =>
                    {
                        var services = request.Services ?? throw new InvalidOperationException("MCP request services are unavailable.");
                        var toolRegistry = services.GetRequiredService<ToolRegistry>();
                        return new ValueTask<ListToolsResult>(new ListToolsResult
                        {
                            Tools = toolRegistry.ListTools()
                        });
                    })
                    .WithCallToolHandler((request, cancellationToken) =>
                    {
                        var services = request.Services ?? throw new InvalidOperationException("MCP request services are unavailable.");
                        var toolRegistry = services.GetRequiredService<ToolRegistry>();
                        return toolRegistry.CallToolAsync(request.Params?.Name, request.Params?.Arguments, cancellationToken);
                    });
            });

            return hostBuilder.Build();
        }

        private static LogLevel ResolveMinimumLogLevel()
        {
            var configuredLevel = Environment.GetEnvironmentVariable("OPEN_TIA_MCP_LOG_LEVEL");
            return Enum.TryParse(configuredLevel, ignoreCase: true, out LogLevel parsedLevel)
                ? parsedLevel
                : LogLevel.Warning;
        }
    }
}
