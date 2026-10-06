using OpenTiaToolkit.Core;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTiaToolkit.Tests
{
    internal static class TestAssemblyBootstrap
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            var publicApiPath = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "TiaPortalPublicApiPath")
                ?.Value;

            if (!string.IsNullOrWhiteSpace(publicApiPath))
            {
                var publicApiDirectory = new DirectoryInfo(publicApiPath);
                var portalRoot = publicApiDirectory.Parent?.Parent?.Parent;
                if (portalRoot?.Exists == true)
                {
                    Environment.SetEnvironmentVariable("OPEN_TIA_PORTAL_ROOT", portalRoot.FullName);
                }
            }

            TiaPortalAssemblyResolver.Register();
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
