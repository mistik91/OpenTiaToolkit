using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.MCP.Server;

namespace OpenTiaToolkit.MCP
{
    /// <summary>
    /// Main entry point for the OpenTiaToolkit MCP server.
    /// </summary>
    public static class Program
    {
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
                services.AddSingleton(ResolveRuntimeOptions());
                services.AddSingleton(serviceProvider => new ToolRegistry(
                    ToolkitSession.ResolveWorkerPath(typeof(Program).Assembly.Location),
                    ToolkitSession.ResolveDefaultAuditPath(),
                    serviceProvider.GetRequiredService<McpRuntimeOptions>(),
                    serviceProvider.GetRequiredService<ILogger<ToolRegistry>>()));
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
                        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? arguments =
                            request.Params?.Arguments == null
                                ? null
                                : new Dictionary<string, System.Text.Json.JsonElement>(
                                    request.Params.Arguments,
                                    StringComparer.OrdinalIgnoreCase);
                        return toolRegistry.CallToolAsync(request.Params?.Name, arguments, cancellationToken);
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
                ToolTimeout = ResolveToolTimeout(),
                SessionResetGracePeriod = ResolvePositiveDuration(
                    "OPEN_TIA_MCP_RESET_GRACE_SECONDS",
                    TimeSpan.FromSeconds(5)),
                AllowedFileRoots = ResolveAllowedFileRoots(),
                MaxInputFileBytes = ResolvePositiveLong(
                    "OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES",
                    16L * 1024L * 1024L),
                AllowFileOverwrite = ResolveBoolean("OPEN_TIA_MCP_ALLOW_FILE_OVERWRITE", false)
            };
        }

        private static IReadOnlyList<string> ResolveAllowedFileRoots()
        {
            var configured = Environment.GetEnvironmentVariable("OPEN_TIA_MCP_ALLOWED_ROOTS");
            if (string.IsNullOrWhiteSpace(configured))
            {
                return Array.Empty<string>();
            }

            return configured!
                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .ToList();
        }

        private static long ResolvePositiveLong(string variableName, long defaultValue)
        {
            var configured = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return defaultValue;
            }

            if (long.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                return value;
            }

            Console.Error.WriteLine($"Ignoring invalid {variableName} value '{configured}'. Use a positive integer.");
            return defaultValue;
        }

        private static bool ResolveBoolean(string variableName, bool defaultValue)
        {
            var configured = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return defaultValue;
            }

            if (bool.TryParse(configured, out var value))
            {
                return value;
            }

            Console.Error.WriteLine($"Ignoring invalid {variableName} value '{configured}'. Use true or false.");
            return defaultValue;
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

        private static TimeSpan ResolvePositiveDuration(string variableName, TimeSpan defaultValue)
        {
            var configuredSeconds = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(configuredSeconds))
            {
                return defaultValue;
            }

            if (double.TryParse(configuredSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            Console.Error.WriteLine(
                $"Ignoring invalid {variableName} value '{configuredSeconds}'. Use a positive number of seconds.");
            return defaultValue;
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
                "  OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS  Optional positive timeout for one tool call. A timed-out worker is terminated and replaced.",
                "  OPEN_TIA_MCP_RESET_GRACE_SECONDS   Seconds reset-session waits after requesting cooperative cancellation. Default: 5. A still-running call is left intact.",
                "  OPEN_TIA_MCP_ALLOWED_ROOTS         Semicolon-separated roots allowed for MCP path parameters. Defaults: Documents, Desktop, and the OpenTiaToolkit temp directory.",
                "  OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES Maximum existing input file size. Default: 16777216 (16 MiB).",
                "  OPEN_TIA_MCP_ALLOW_FILE_OVERWRITE Allow explicit MCP output paths to overwrite existing files. Default: false.",
                "  OPEN_TIA_ARTIFACT_RETENTION_DAYS   Optional retention window for temporary command artifacts. Default: 7.",
                "",
                "Built-in MCP tools:",
                "  operation-status                    Reports active operation details.",
                "  cancel-operation                    Requests cancellation, then terminates and replaces the active worker.",
                "  reset-session                       Requests cancellation and waits for quiescence before replacing the worker; fails closed after the grace period.",
                "",
                "Register only the exact isolated V21 worker path/hash with Siemens Openness. Do not register this vendor-free MCP host."
            });
        }
    }
}
