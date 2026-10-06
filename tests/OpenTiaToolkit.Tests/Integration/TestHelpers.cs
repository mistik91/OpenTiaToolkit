using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Services;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration
{
    internal static class TestHelpers
    {
        private static bool _isTiaInitialized = false;

        static TestHelpers()
        {
            // Register TIA Portal assembly resolver BEFORE any Openness types are loaded
            TiaPortalAssemblyResolver.Register();
        }

        public static void InitializeTiaOpenness()
        {
            if (!_isTiaInitialized)
            {
                try
                {
                    // Set TIA Portal location environment variable if not set
                    var tiaPath = System.Environment.GetEnvironmentVariable("TiaPortalLocation");
                    if (string.IsNullOrEmpty(tiaPath))
                    {
                        // Try to detect TIA V21 installation (primary target)
                        tiaPath = @"C:\Program Files\Siemens\Automation\Portal V21";
                        if (System.IO.Directory.Exists(tiaPath))
                        {
                            System.Environment.SetEnvironmentVariable("TiaPortalLocation", tiaPath);
                        }
                    }

                    // Note: Custom TiaPortalAssemblyResolver handles assembly loading
                    // No explicit initialization needed (unlike official resolver)
                    _isTiaInitialized = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Failed to initialize TIA Openness API. Ensure TIA Portal V21+ is installed.",
                        ex);
                }
            }
        }

        public static IPortalService CreatePortalService(ILoggerFactory loggerFactory)
        {
            InitializeTiaOpenness();
            var logger = loggerFactory.CreateLogger<PortalService>();
            return new PortalService(logger);
        }

        public static async Task<bool> OpenProjectAsync(IPortalService portal, string projectPath, ILogger logger)
        {
            try
            {
                logger.LogInformation("Opening project: {ProjectPath}", projectPath);
                return await portal.OpenProjectAsync(projectPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to open project: {ProjectPath}", projectPath);
                return false;
            }
        }

        public static async Task<bool> CloseProjectAsync(IPortalService portal, bool save, ILogger logger)
        {
            try
            {
                logger.LogInformation("Closing project (save={Save})", save);
                return await portal.CloseProjectAsync(save);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to close project");
                return false;
            }
        }

        public static CommandArgs CreateArgs(string key, object value)
        {
            return new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { key, value }
            });
        }

        public static CommandArgs CreateEmptyArgs()
        {
            return new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>());
        }
    }
}
