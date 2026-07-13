using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
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
            services.AddSingleton<ISessionResetService, EngineeringSessionResetService>();
            services.AddSingleton(serviceProvider => new PlcDocumentWorkflowService(
                serviceProvider.GetRequiredService<IPlcService>(),
                serviceProvider.GetRequiredService<EngineeringSessionContext>()));
            services.AddSingleton<PlcCodeFormatService>();

            services.AddSingleton<PortalService>(serviceProvider =>
                new PortalService(
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<PortalService>>()));
            services.AddSingleton<IPortalService>(serviceProvider =>
                SessionSerializedServiceProxy<IPortalService>.Create(
                    serviceProvider.GetRequiredService<PortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<PlcService>(serviceProvider =>
                new PlcService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<PlcService>>()));
            services.AddSingleton<IPlcService>(serviceProvider =>
                SessionSerializedServiceProxy<IPlcService>.Create(
                    serviceProvider.GetRequiredService<PlcService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<HmiService>(serviceProvider =>
                new HmiService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<HmiService>>()));
            services.AddSingleton<IHmiService>(serviceProvider =>
                SessionSerializedServiceProxy<IHmiService>.Create(
                    serviceProvider.GetRequiredService<HmiService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<VciService>(serviceProvider =>
                new VciService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<IPlcService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<VciService>>()));
            services.AddSingleton<IVciService>(serviceProvider =>
                SessionSerializedServiceProxy<IVciService>.Create(
                    serviceProvider.GetRequiredService<VciService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<HardwareService>(serviceProvider =>
                new HardwareService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<HardwareService>>()));
            services.AddSingleton<IHardwareService>(serviceProvider =>
                SessionSerializedServiceProxy<IHardwareService>.Create(
                    serviceProvider.GetRequiredService<HardwareService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));
            services.AddSingleton<IHardwareQueryCapability>(serviceProvider => serviceProvider.GetRequiredService<IHardwareService>());
            services.AddSingleton<IHardwarePatchCapability>(serviceProvider => serviceProvider.GetRequiredService<IHardwareService>());

            services.AddSingleton<LibraryService>(serviceProvider =>
                new LibraryService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<IPlcService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<LibraryService>>()));
            services.AddSingleton<ILibraryService>(serviceProvider =>
                SessionSerializedServiceProxy<ILibraryService>.Create(
                    serviceProvider.GetRequiredService<LibraryService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<TextService>(serviceProvider =>
                new TextService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<TextService>>()));
            services.AddSingleton<ITextService>(serviceProvider =>
                SessionSerializedServiceProxy<ITextService>.Create(
                    serviceProvider.GetRequiredService<TextService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<EngineeringInsightsService>();
            services.AddSingleton<IEngineeringInsightsService>(serviceProvider =>
                SessionSerializedServiceProxy<IEngineeringInsightsService>.Create(
                    serviceProvider.GetRequiredService<EngineeringInsightsService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<MultiuserService>();
            services.AddSingleton<IMultiuserService>(serviceProvider =>
                SessionSerializedServiceProxy<IMultiuserService>.Create(
                    serviceProvider.GetRequiredService<MultiuserService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            return services;
        }

        public static IServiceCollection AddOpenTiaToolkitCommands(this IServiceCollection services)
        {
            foreach (var commandType in GetCommandTypes(typeof(ServiceCollectionExtensions).Assembly))
            {
                services.AddSingleton(typeof(ICommand), commandType);
            }

            return services;
        }

        internal static IReadOnlyList<Type> GetCommandTypes(Assembly assembly)
        {
            return assembly
                .GetTypes()
                .Where(type => typeof(ICommand).IsAssignableFrom(type))
                .Where(type => type.IsClass && !type.IsAbstract)
                .Where(type => type.GetConstructor(Type.EmptyTypes) != null || type.GetConstructors().Any())
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();
        }
    }
}
