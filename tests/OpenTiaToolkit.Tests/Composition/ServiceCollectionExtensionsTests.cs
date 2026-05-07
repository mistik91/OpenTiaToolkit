using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Services;
using System;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Tests.Composition
{
    /// <summary>
    /// Unit tests for shared dependency-injection registration and session wiring.
    /// </summary>
    [TestClass]
    public class ServiceCollectionExtensionsTests
    {
        [TestMethod]
        public void AddOpenTiaToolkitCoreServices_SharesSessionContextBetweenPortalAndPlcServices()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTiaToolkitCoreServices();

            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var portalService = serviceProvider.GetRequiredService<IPortalService>();
            var plcService = serviceProvider.GetRequiredService<IPlcService>();
            var hmiService = serviceProvider.GetRequiredService<IHmiService>();

            // Assert
            var portalContext = GetPrivateFieldValue(portalService, "_sessionContext");
            var plcContext = GetPrivateFieldValue(plcService, "_sessionContext");
            var hmiContext = GetPrivateFieldValue(hmiService, "_sessionContext");

            Assert.IsNotNull(portalContext);
            Assert.IsNotNull(plcContext);
            Assert.IsNotNull(hmiContext);
            Assert.AreSame(portalContext, plcContext);
            Assert.AreSame(portalContext, hmiContext);
        }

        [TestMethod]
        public void ConstructorPath_SharesSessionContextWhenPlcServiceUsesPortalServiceInstance()
        {
            // Arrange
            using var loggerFactory = LoggerFactory.Create(builder => { });
            var portalService = new PortalService(loggerFactory.CreateLogger<PortalService>());
            var plcService = new PlcService(portalService, loggerFactory.CreateLogger<PlcService>());

            // Act
            var portalContext = GetPrivateFieldValue(portalService, "_sessionContext");
            var plcContext = GetPrivateFieldValue(plcService, "_sessionContext");

            // Assert
            Assert.IsNotNull(portalContext);
            Assert.IsNotNull(plcContext);
            Assert.AreSame(portalContext, plcContext);
        }

        [TestMethod]
        public void AddOpenTiaToolkitCommands_RegistersCommonCommandSurface()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTiaToolkitCoreServices();
            services.AddOpenTiaToolkitCommands();

            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var commands = serviceProvider.GetServices<ICommand>().Select(command => command.Name).ToList();

            // Assert
            CollectionAssert.Contains(commands, "connect-portal");
            CollectionAssert.Contains(commands, "open-project");
            CollectionAssert.Contains(commands, "open-plc");
            CollectionAssert.Contains(commands, "compile-software");
            CollectionAssert.Contains(commands, "open-unified-hmi");
            CollectionAssert.Contains(commands, "list-hmi-tags");
            CollectionAssert.Contains(commands, "list-hmi-screens");
            CollectionAssert.Contains(commands, "copy-hmi-screen-from-template");
            CollectionAssert.Contains(commands, "set-hmi-item-property");
            CollectionAssert.Contains(commands, "create-hmi-trend");
            CollectionAssert.Contains(commands, "export-hmi-json");
            CollectionAssert.Contains(commands, "diff-hmi-json");
            CollectionAssert.Contains(commands, "import-hmi-json");
            CollectionAssert.Contains(commands, "get-hmi-screen-json");
            CollectionAssert.Contains(commands, "get-hmi-screen-summary");
            CollectionAssert.Contains(commands, "hmi-status");
            CollectionAssert.Contains(commands, "audit-hmi-label-proximity");
            CollectionAssert.Contains(commands, "probe-hmi-faceplate-interface");
            CollectionAssert.Contains(commands, "apply-hmi-screen-json-edit");
            CollectionAssert.Contains(commands, "apply-hmi-screen-json-patch");
            CollectionAssert.Contains(commands, "inspect-hmi-screen-json-capabilities");
            CollectionAssert.Contains(commands, "create-block");
            CollectionAssert.Contains(commands, "find-replace-db-members");
            CollectionAssert.Contains(commands, "find-replace-udt-members");
            CollectionAssert.Contains(commands, "update-tags-from-csv");
        }

        private static object? GetPrivateFieldValue(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Could not find field '{fieldName}' on type '{target.GetType().Name}'.");
            return field!.GetValue(target);
        }
    }
}
