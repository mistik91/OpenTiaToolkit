using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class TestAssemblyBootstrapTests
    {
        [TestMethod]
        public void ConfiguredPublicApiPath_InitializesResolverRootForThisTestProcess()
        {
            var publicApiPath = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "TiaPortalPublicApiPath")
                .Value;
            var expectedRoot = new DirectoryInfo(publicApiPath!).Parent?.Parent?.Parent?.FullName;

            Assert.IsFalse(string.IsNullOrWhiteSpace(expectedRoot));
            Assert.AreEqual(
                Path.GetFullPath(expectedRoot!),
                Path.GetFullPath(Environment.GetEnvironmentVariable("OPEN_TIA_PORTAL_ROOT")!),
                ignoreCase: true);
        }
    }
}
