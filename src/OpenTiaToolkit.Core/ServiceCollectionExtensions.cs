using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Devices;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core
{
    /// <summary>
    /// Shared dependency-injection registration for OpenTiaToolkit hosts.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenTiaToolkitCoreServices(this IServiceCollection services)
        {
            services.AddSingleton<EngineeringSessionContext>();

            services.AddSingleton<IPortalService>(serviceProvider =>
                new PortalService(
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<PortalService>>()));

            services.AddSingleton<IPlcService>(serviceProvider =>
                new PlcService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<PlcService>>()));

            return services;
        }

        public static IServiceCollection AddOpenTiaToolkitCommands(this IServiceCollection services)
        {
            // Phase 1 commands
            services.AddSingleton<ICommand, ConnectPortalCommand>();
            services.AddSingleton<ICommand, DisconnectPortalCommand>();
            services.AddSingleton<ICommand, PortalStatusCommand>();
            services.AddSingleton<ICommand, OpenProjectCommand>();
            services.AddSingleton<ICommand, CloseProjectCommand>();
            services.AddSingleton<ICommand, ListProjectsCommand>();
            services.AddSingleton<ICommand, GetProjectInfoCommand>();
            services.AddSingleton<ICommand, ListDevicesCommand>();

            // Phase 2 commands
            services.AddSingleton<ICommand, OpenTiaPortalCommand>();
            services.AddSingleton<ICommand, CloseTiaPortalCommand>();
            services.AddSingleton<ICommand, ListInstancesCommand>();
            services.AddSingleton<ICommand, ConnectInstanceCommand>();
            services.AddSingleton<ICommand, SaveProjectCommand>();
            services.AddSingleton<ICommand, SaveAsProjectCommand>();
            services.AddSingleton<ICommand, GetProjectTreeCommand>();

            // Phase 3 commands
            services.AddSingleton<ICommand, GetDeviceInfoCommand>();
            services.AddSingleton<ICommand, GetDeviceItemInfoCommand>();
            services.AddSingleton<ICommand, OpenPlcCommand>();
            services.AddSingleton<ICommand, GetSoftwareTreeCommand>();
            services.AddSingleton<ICommand, CompileSoftwareCommand>();
            services.AddSingleton<ICommand, ExportPlcCommand>();
            services.AddSingleton<ICommand, GetBlocksCommand>();
            services.AddSingleton<ICommand, ListPlcsCommand>();
            services.AddSingleton<ICommand, ListHmisCommand>();

            // Phase 4 commands
            services.AddSingleton<ICommand, DeleteBlocksCommand>();
            services.AddSingleton<ICommand, GetBlockInfoCommand>();
            services.AddSingleton<ICommand, ExportBlockCommand>();
            services.AddSingleton<ICommand, SearchBlocksCommand>();

            // Phase 4.5 commands
            services.AddSingleton<ICommand, ExportBlockAsDocumentsCommand>();
            services.AddSingleton<ICommand, ExportBlocksAsDocumentsCommand>();
            services.AddSingleton<ICommand, ExportTypeCommand>();
            services.AddSingleton<ICommand, ExportTypesCommand>();

            // Phase 4.6 commands
            services.AddSingleton<ICommand, ImportBlockCommand>();
            services.AddSingleton<ICommand, ImportBlocksCommand>();
            services.AddSingleton<ICommand, ImportTypeCommand>();
            services.AddSingleton<ICommand, ImportTypesCommand>();
            services.AddSingleton<ICommand, ImportBlockFromDocumentsCommand>();
            services.AddSingleton<ICommand, ImportBlocksFromDocumentsCommand>();

            // Phase 5 commands
            services.AddSingleton<ICommand, FindAndReplaceTagsCommand>();
            services.AddSingleton<ICommand, CreateTagCommand>();
            services.AddSingleton<ICommand, UpdateTagsFromCsvCommand>();

            return services;
        }
    }
}
