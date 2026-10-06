using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;

namespace OpenTiaToolkit.Worker.V21;

public static class Program
{
    static Program()
    {
        TiaPortalAssemblyResolver.Register();
    }

    public static async Task<int> Main(string[] args)
    {
        string? pipeName = ReadArgument(args, "--pipe");
        string? executeCommand = ReadArgument(args, "--execute");
        if (string.IsNullOrWhiteSpace(pipeName) && string.IsNullOrWhiteSpace(executeCommand))
        {
            Console.Error.WriteLine("Usage: OpenTiaToolkit.Worker.V21.exe --pipe <name> | --execute <read-only-command>");
            return 2;
        }

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace));
            services.AddOpenTiaToolkitCoreServices();
            services.AddOpenTiaToolkitCommands();
            services.AddSingleton<ICommand, VersionCommand>();
            services.AddSingleton<ICommand, DoctorCommand>();
            services.AddSingleton<ICommand, CapabilitiesCommand>();
            services.AddSingleton<BlueprintCoordinator>();
            services.AddSingleton<ICommand, ValidateBlueprintCommand>();
            services.AddSingleton<ICommand, PlanBlueprintCommand>();
            services.AddSingleton<ICommand, ApplyBlueprintCommand>();
            services.AddSingleton<ApiSelfDescriptionService>();
            services.AddSingleton<ICommand, DescribeApiCommand>();
            services.AddSingleton<ICommand, DiffApiProfileCommand>();
            services.AddSingleton<WorkerCommandExecutor>();
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                var executor = provider.GetRequiredService<WorkerCommandExecutor>();
                if (!string.IsNullOrWhiteSpace(executeCommand))
                {
                    var descriptor = executor.GetCatalog().FirstOrDefault(command =>
                        string.Equals(command.Name, executeCommand, StringComparison.OrdinalIgnoreCase));
                    if (descriptor == null || descriptor.Risk != OpenTiaToolkit.Contracts.Commands.CommandRisk.ReadOnly)
                        throw new InvalidOperationException("--execute accepts a known read-only command only.");
                    var outcome = await executor.ExecuteAsync(new OpenTiaToolkit.Contracts.Commands.CommandInvocation
                    {
                        CommandName = executeCommand!
                    }).ConfigureAwait(false);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                        outcome,
                        OpenTiaToolkit.Contracts.Protocol.ContractJson.CreateOptions(writeIndented: true)));
                    return outcome.Success ? 0 : 1;
                }

                var server = new WorkerServer(pipeName!, executor);
                await server.RunAsync().ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.InnerException?.Message ?? ex.Message);
            return 1;
        }
    }

    private static string? ReadArgument(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
