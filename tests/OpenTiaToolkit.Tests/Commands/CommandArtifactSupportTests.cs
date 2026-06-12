using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;
using System.IO;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class CommandArtifactSupportTests
    {
        [TestMethod]
        public void InlineResultExceededReason_UsesSharedThreshold()
        {
            Assert.AreEqual(
                "inline result exceeded 32768 bytes",
                CommandArtifactSupport.InlineResultExceededReason());
        }

        [TestMethod]
        public void ExplicitOutputPathReason_UsesSharedWording()
        {
            Assert.AreEqual(
                "output-path was provided",
                CommandArtifactSupport.ExplicitOutputPathReason());
        }

        [TestMethod]
        public void ResolveOutputPath_AppendsDefaultFileName_WhenPathHasNoExtension()
        {
            var outputPath = CommandArtifactSupport.ResolveOutputPath(
                Path.Combine(Path.GetTempPath(), "OpenTiaToolkitArtifactTest"),
                "result.csv");

            Assert.AreEqual(
                Path.Combine(Path.GetTempPath(), "OpenTiaToolkitArtifactTest", "result.csv"),
                outputPath);
        }
    }
}
