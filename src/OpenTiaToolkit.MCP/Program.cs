using System;
using System.Globalization;
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
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help"))
            {
                Console.WriteLine(GetHelpText());
                return 0;
            }

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
                services.AddSingleton(ResolveRuntimeOptions());
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
            if (string.IsNullOrWhiteSpace(configuredLevel))
            {
                return LogLevel.Warning;
            }

            if (Enum.TryParse(configuredLevel, ignoreCase: true, out LogLevel parsedLevel))
            {
                return parsedLevel;
            }

            Console.Error.WriteLine(
                $"Ignoring invalid OPEN_TIA_MCP_LOG_LEVEL value '{configuredLevel}'. Valid values: Trace, Debug, Information, Warning, Error, Critical, None.");
            return LogLevel.Warning;
        }

        private static McpRuntimeOptions ResolveRuntimeOptions()
        {
            return new McpRuntimeOptions
            {
                ToolTimeout = ResolveToolTimeout()
            };
        }

        private static TimeSpan? ResolveToolTimeout()
        {
            var configuredSeconds = Environment.GetEnvironmentVariable("OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS");
            if (string.IsNullOrWhiteSpace(configuredSeconds))
            {
                return null;
            }

            if (double.TryParse(configuredSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            Console.Error.WriteLine(
                $"Ignoring invalid OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS value '{configuredSeconds}'. Use a positive number of seconds.");
            return null;
        }

        private static string GetHelpText()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "OpenTiaToolkit MCP Server",
                "",
                "Usage: OpenTiaToolkit.MCP.exe",
                "",
                "The server communicates over stdio using the Model Context Protocol.",
                "",
                "Environment:",
                "  OPEN_TIA_MCP_LOG_LEVEL             Trace, Debug, Information, Warning, Error, Critical, or None. Default: Warning.",
                "  OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS  Optional positive timeout for one tool call. Timed-out calls are asked to cancel; use reset-session if the attachment stays busy.",
                "  OPEN_TIA_ARTIFACT_RETENTION_DAYS   Optional retention window for temporary command artifacts. Default: 7.",
                "",
                "Built-in MCP tools:",
                "  operation-status                    Reports active operation details and latest progress.",
                "  cancel-operation                    Requests cancellation of the active operation.",
                "  reset-session                       Cancels the active operation, clears the MCP gate, and disposes the current TIA attachment.",
                "",
                "After rebuilding this executable, refresh the Siemens Openness allow-list for this exact path and hash."
            });
        }
    }
}
