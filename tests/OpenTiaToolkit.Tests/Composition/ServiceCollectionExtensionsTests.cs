using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
            var vciService = serviceProvider.GetRequiredService<IVciService>();
            var hardwareService = serviceProvider.GetRequiredService<IHardwareService>();
            var libraryService = serviceProvider.GetRequiredService<ILibraryService>();
            var textService = serviceProvider.GetRequiredService<ITextService>();

            // Assert
            var portalContext = GetPrivateFieldValue(portalService, "_sessionContext");
            var plcContext = GetPrivateFieldValue(plcService, "_sessionContext");
            var hmiContext = GetPrivateFieldValue(hmiService, "_sessionContext");
            var vciContext = GetPrivateFieldValue(vciService, "_sessionContext");
            var hardwareContext = GetPrivateFieldValue(hardwareService, "_sessionContext");
            var libraryContext = GetPrivateFieldValue(libraryService, "_sessionContext");
            var textContext = GetPrivateFieldValue(textService, "_sessionContext");

            Assert.IsNotNull(portalContext);
            Assert.IsNotNull(plcContext);
            Assert.IsNotNull(hmiContext);
            Assert.IsNotNull(vciContext);
            Assert.IsNotNull(hardwareContext);
            Assert.IsNotNull(libraryContext);
            Assert.IsNotNull(textContext);
            Assert.AreSame(portalContext, plcContext);
            Assert.AreSame(portalContext, hmiContext);
            Assert.AreSame(portalContext, vciContext);
            Assert.AreSame(portalContext, hardwareContext);
            Assert.AreSame(portalContext, libraryContext);
            Assert.AreSame(portalContext, textContext);
        }

        [TestMethod]
        public async Task SessionSerializedServiceProxy_SerializesConcurrentAsyncCalls()
        {
            // Arrange
            var context = new EngineeringSessionContext();
            var target = new SerializedProbeService();
            var proxy = SessionSerializedServiceProxy<ISerializedProbeService>.Create(target, context);

            // Act
            await Task.WhenAll(
                proxy.EnterAsync(),
                proxy.EnterAsync(),
                proxy.EnterAsync());

            // Assert
            Assert.AreEqual(1, target.MaxObservedConcurrency);
            Assert.AreEqual(3, target.CompletedCalls);
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
        public void PublicServiceConstructors_RejectPortalServiceWithoutSharedSessionContext()
        {
            // Arrange
            using var loggerFactory = LoggerFactory.Create(builder => { });
            var portalService = new Mock<IPortalService>().Object;

            // Act / Assert
            Assert.ThrowsException<ArgumentException>(() =>
                new PlcService(portalService, loggerFactory.CreateLogger<PlcService>()));
            Assert.ThrowsException<ArgumentException>(() =>
                new HmiService(portalService, loggerFactory.CreateLogger<HmiService>()));
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
            CollectionAssert.Contains(commands, "compare-software");
            CollectionAssert.Contains(commands, "download-software");
            CollectionAssert.Contains(commands, "get-online-status");
            CollectionAssert.Contains(commands, "start-cpu");
            CollectionAssert.Contains(commands, "stop-cpu");
            CollectionAssert.Contains(commands, "open-global-library");
            CollectionAssert.Contains(commands, "list-global-libraries");
            CollectionAssert.Contains(commands, "close-global-library");
            CollectionAssert.Contains(commands, "list-master-copies");
            CollectionAssert.Contains(commands, "copy-from-master-copy");
            CollectionAssert.Contains(commands, "list-library-types");
            CollectionAssert.Contains(commands, "get-library-type-versions");
            CollectionAssert.Contains(commands, "check-library-updates");
            CollectionAssert.Contains(commands, "list-project-languages");
            CollectionAssert.Contains(commands, "export-texts");
            CollectionAssert.Contains(commands, "import-texts");
            CollectionAssert.Contains(commands, "open-unified-hmi");
            CollectionAssert.Contains(commands, "list-hmi-tags");
            CollectionAssert.Contains(commands, "list-hmi-screens");
            CollectionAssert.Contains(commands, "copy-hmi-screen-from-template");
            CollectionAssert.Contains(commands, "set-hmi-item-property");
            CollectionAssert.Contains(commands, "create-hmi-trend");
            CollectionAssert.Contains(commands, "remove-hmi-logging-tag");
            CollectionAssert.Contains(commands, "delete-hmi-data-log");
            CollectionAssert.Contains(commands, "create-hmi-trends");
            CollectionAssert.Contains(commands, "remove-hmi-logging-tags");
            CollectionAssert.Contains(commands, "delete-hmi-data-logs");
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
            CollectionAssert.Contains(commands, "export-tags");
            CollectionAssert.Contains(commands, "import-source");
            CollectionAssert.Contains(commands, "delete-external-source");
            CollectionAssert.Contains(commands, "generate-blocks-from-source");
            CollectionAssert.Contains(commands, "export-block-as-source");
            CollectionAssert.Contains(commands, "validate-plc-documents");
            CollectionAssert.Contains(commands, "plan-block-document-import");
            CollectionAssert.Contains(commands, "apply-block-document-import");
            CollectionAssert.Contains(commands, "get-cross-references");
            CollectionAssert.Contains(commands, "list-external-sources");
            CollectionAssert.Contains(commands, "list-watch-tables");
            CollectionAssert.Contains(commands, "list-technology-objects");
            CollectionAssert.Contains(commands, "list-plc-alarm-text-lists");
            CollectionAssert.Contains(commands, "modify-tags");
            CollectionAssert.DoesNotContain(commands, "create-tag");
            CollectionAssert.DoesNotContain(commands, "update-tags-from-csv");
            CollectionAssert.Contains(commands, "list-vci-workspaces");
            CollectionAssert.Contains(commands, "create-vci-workspace");
            CollectionAssert.Contains(commands, "export-vci-block");
            CollectionAssert.Contains(commands, "connect-vci-block");
            CollectionAssert.Contains(commands, "get-vci-block-status");
            CollectionAssert.Contains(commands, "sync-vci-block");
            CollectionAssert.Contains(commands, "sync-vci-blocks");
            CollectionAssert.Contains(commands, "sync-vci-plc");
            CollectionAssert.Contains(commands, "find-hardware-catalog-items");
            CollectionAssert.Contains(commands, "get-hardware-tree");
            CollectionAssert.Contains(commands, "get-hardware-io-addresses");
            CollectionAssert.Contains(commands, "get-hardware-drive-telegrams");
            CollectionAssert.Contains(commands, "get-hardware-item-capabilities");
            CollectionAssert.Contains(commands, "get-hardware-feature-attributes");
            CollectionAssert.Contains(commands, "get-hardware-network-item-attributes");
            CollectionAssert.Contains(commands, "find-hardware-module-types");
            CollectionAssert.Contains(commands, "can-plug-hardware-item");
            CollectionAssert.Contains(commands, "get-hardware-networks");
            CollectionAssert.Contains(commands, "get-gsd-parameter-data");
            CollectionAssert.Contains(commands, "apply-hardware-patch");
        }

        [TestMethod]
        public void AddOpenTiaToolkitCommands_RegistersEveryCoreCommandImplementation()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTiaToolkitCoreServices();
            services.AddOpenTiaToolkitCommands();

            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var registeredTypes = serviceProvider.GetServices<ICommand>()
                .Select(command => command.GetType())
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            var discoveredTypes = ServiceCollectionExtensions
                .GetCommandTypes(typeof(ServiceCollectionExtensions).Assembly)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            // Assert
            CollectionAssert.AreEqual(discoveredTypes, registeredTypes);
        }

        [TestMethod]
        public void CoreAbstractionsAndServices_DoNotDependOnCommandNamespaces()
        {
            // Arrange
            var sourceRoot = FindSourceRoot();
            var scopedFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}Abstractions{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}"))
                .Where(path => !path.EndsWith($"{Path.DirectorySeparatorChar}ICommand.cs", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Act
            var offenders = scopedFiles
                .Where(path => File.ReadAllText(path).IndexOf("using OpenTiaToolkit.Core.Commands.", StringComparison.Ordinal) >= 0)
                .Select(path => path.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            // Assert
            Assert.AreEqual(0, offenders.Length, "Core abstractions/services must depend on Models, not Commands: " + string.Join(", ", offenders));
        }

        private static object? GetPrivateFieldValue(object target, string fieldName)
        {
            target = UnwrapSessionSerializedProxy(target);

            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Could not find field '{fieldName}' on type '{target.GetType().Name}'.");
            return field!.GetValue(target);
        }

        private static object UnwrapSessionSerializedProxy(object target)
        {
            var targetField = FindField(target.GetType(), "_target");
            return targetField?.GetValue(target) ?? target;
        }

        private static FieldInfo? FindField(Type type, string fieldName)
        {
            for (var currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                var field = currentType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static string FindSourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "OpenTiaToolkit.Core");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not locate src/OpenTiaToolkit.Core from test output directory.");
            throw new InvalidOperationException();
        }

        public interface ISerializedProbeService
        {
            Task EnterAsync();
        }

        private sealed class SerializedProbeService : ISerializedProbeService
        {
            private int _activeCalls;

            public int CompletedCalls { get; private set; }

            public int MaxObservedConcurrency { get; private set; }

            public async Task EnterAsync()
            {
                var activeCalls = Interlocked.Increment(ref _activeCalls);
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, activeCalls);
                await Task.Delay(25);
                CompletedCalls++;
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }
}
