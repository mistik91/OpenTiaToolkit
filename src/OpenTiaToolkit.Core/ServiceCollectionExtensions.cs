using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.Core.Services.PlcTags;
using OpenTiaToolkit.Core.Services.V21;

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
            services.AddSingleton<V21TypeServiceResolver>();
            services.AddSingleton<V21EngineeringObjectAccess>();
            services.AddSingleton<V21SoftwareDiscovery>();
            services.AddSingleton<V21PlcTagStructureAccess>();
            services.AddSingleton<V21CompilationAccess>();
            services.AddSingleton<V21OnlineVerificationAccess>();
            services.AddSingleton<V21DocumentAccess>();
            services.AddSingleton<V21HardwareAdapter>();
            services.AddSingleton<V21PlcTagRepository>();
            services.AddSingleton<IPlcTagRepository>(serviceProvider =>
                serviceProvider.GetRequiredService<V21PlcTagRepository>());
            services.AddSingleton<PlcCodeEvidenceRegistry>();
            services.AddSingleton<PlcBehaviorVectorService>();
            services.AddSingleton<PlcDocumentValidator>();
            services.AddSingleton<IPlcSimulationRuntime, PlcSimulationRuntime>();
            services.AddSingleton<ISessionResetService, EngineeringSessionResetService>();
            services.AddSingleton(serviceProvider => new PlcDocumentWorkflowService(
                serviceProvider.GetRequiredService<IPlcService>(),
                serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                serviceProvider.GetRequiredService<PlcCodeCapabilityDiscoveryService>(),
                serviceProvider.GetRequiredService<PlcCodeEvidenceRegistry>(),
                serviceProvider.GetRequiredService<IPlcSimulationRuntime>(),
                serviceProvider.GetRequiredService<V21DocumentAccess>(),
                serviceProvider.GetRequiredService<V21EngineeringObjectAccess>()));
            services.AddSingleton(serviceProvider => new PlcCodeFormatService(serviceProvider.GetRequiredService<PlcDocumentValidator>()));

            services.AddSingleton<PortalService>(serviceProvider =>
                new PortalService(
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<PortalService>>(),
                    serviceProvider.GetRequiredService<V21TypeServiceResolver>(),
                    serviceProvider.GetRequiredService<V21SoftwareDiscovery>()));
            services.AddSingleton<IPortalService>(serviceProvider =>
                SessionSerializedServiceProxy<IPortalService>.Create(
                    serviceProvider.GetRequiredService<PortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>()));

            services.AddSingleton<PlcService>(serviceProvider =>
                new PlcService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetService<ILogger<PlcService>>(),
                    serviceProvider.GetRequiredService<V21SoftwareDiscovery>(),
                    serviceProvider.GetRequiredService<V21CompilationAccess>(),
                    serviceProvider.GetRequiredService<V21TypeServiceResolver>(),
                    serviceProvider.GetRequiredService<V21OnlineVerificationAccess>(),
                    serviceProvider.GetRequiredService<V21DocumentAccess>(),
                    serviceProvider.GetRequiredService<V21EngineeringObjectAccess>(),
                    serviceProvider.GetRequiredService<V21PlcTagStructureAccess>(),
                    serviceProvider.GetRequiredService<IPlcTagRepository>()));
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
            services.AddSingleton(serviceProvider =>
            {
                IPortalService portal = serviceProvider.GetRequiredService<IPortalService>();
                EngineeringSessionContext context = serviceProvider.GetRequiredService<EngineeringSessionContext>();
                return new PlcCodeCapabilityDiscoveryService(
                    serviceProvider.GetRequiredService<IPlcService>(),
                    serviceProvider.GetRequiredService<IVciService>(),
                    context,
                    async () =>
                    {
                        IReadOnlyList<Models.TiaPortalInstance> instances = await portal.GetInstancesAsync().ConfigureAwait(false);
                        return instances.FirstOrDefault(instance => instance.ProcessId == context.AttachedPortalProcessId)?.Version ?? portal.Version ?? string.Empty;
                    });
            });

            services.AddSingleton<HardwareService>(serviceProvider =>
                new HardwareService(
                    serviceProvider.GetRequiredService<IPortalService>(),
                    serviceProvider.GetRequiredService<EngineeringSessionContext>(),
                    serviceProvider.GetRequiredService<V21HardwareAdapter>(),
                    serviceProvider.GetRequiredService<V21SoftwareDiscovery>(),
                    serviceProvider.GetRequiredService<IPlcTagRepository>(),
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
