using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PortalSessionPolicyTests
    {
        [DataTestMethod]
        [DataRow("session.als21")]
        [DataRow("SESSION.ALS99")]
        public void IsLocalSessionFile_IsCaseInsensitive(string path) => Assert.IsTrue(PortalSessionPolicy.IsLocalSessionFile(path));

        [TestMethod]
        public void ArchiveMode_EmptyDefaultsToCompressed() => Assert.AreEqual("Compressed", PortalSessionPolicy.ParseArchivationMode(string.Empty).ToString());

        [TestMethod]
        public void ArchiveMode_ParsingIsCaseInsensitive() => Assert.AreEqual("Compressed", PortalSessionPolicy.ParseArchivationMode("compressed").ToString());

        [TestMethod]
        public void ArchivePath_IsNormalizedAndSplitIntoDirectoryAndName()
        {
            string path = Path.Combine(Path.GetTempPath(), "archives", "Line1.zap21");
            var result = PortalSessionPolicy.SplitArchivePath(path);
            Assert.AreEqual("Line1.zap21", result.Name);
            Assert.AreEqual(Path.GetFullPath(path), result.ExpectedPath);
        }

        [TestMethod]
        public void UmacUserType_PreservesProjectAndGlobalBehavior()
        {
            Assert.AreEqual("Global", PortalSessionPolicy.ParseUmacUserType("GLOBAL").ToString());
            Assert.AreEqual("Project", PortalSessionPolicy.ParseUmacUserType("project").ToString());
        }

        [TestMethod]
        public void DeviceIdentifier_DoesNotDuplicateExistingVersion()
        {
            Assert.AreEqual("Order/V1", PortalSessionPolicy.BuildDeviceTypeIdentifier("Order/V1", "V1"));
            Assert.AreEqual("Order/V1", PortalSessionPolicy.BuildDeviceTypeIdentifier("Order", "V1"));
        }

        [TestMethod]
        public void ExceptionProjection_PreservesInnerSiemensMessage()
        {
            var error = new InvalidOperationException("outer", new InvalidOperationException("inner"));
            Assert.AreEqual("inner", PortalSessionPolicy.FormatExceptionMessage(error));
        }
    }
}
