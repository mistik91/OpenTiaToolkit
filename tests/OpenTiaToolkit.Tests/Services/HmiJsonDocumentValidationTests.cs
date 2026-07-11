using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonDocumentValidationTests
    {
        [DataTestMethod]
        [DataRow("{\"SchemaVersion\":\"2.0\",\"DocumentKind\":\"Export\",\"Screens\":[]}", "schemaVersion")]
        [DataRow("{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Unknown\",\"Screens\":[]}", "documentKind")]
        [DataRow("{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Patch\",\"Screens\":[{\"Path\":\"Main\",\"Action\":\"Upsert\",\"Items\":[]}]}", "action")]
        [DataRow("{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Patch\",\"Unknown\":true,\"Screens\":[]}", "Unknown")]
        [DataRow("{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Patch\",\"Screens\":[{\"Path\":\"Main\",\"Unknown\":true,\"Items\":[]}]}", "Unknown")]
        public void LoadDocument_RejectsInvalidOrUnknownContract(string json, string expectedMessage)
        {
            var path = WriteJson(json);
            try
            {
                Exception? exception = null;
                try
                {
                    HmiJsonDocumentStore.LoadDocument(path);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                Assert.IsNotNull(exception, "Expected strict HMI JSON validation to reject the document.");
                StringAssert.Contains(exception.ToString(), expectedMessage);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void LoadDocument_RejectsDuplicateScreenAndItemIdentities()
        {
            var duplicateScreens = WriteJson(
                "{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Patch\",\"Screens\":[{\"Path\":\"Main\",\"Items\":[]},{\"Path\":\"main\",\"Items\":[]}]}");
            var duplicateItems = WriteJson(
                "{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Patch\",\"Screens\":[{\"Path\":\"Main\",\"Items\":[{\"Name\":\"Button\"},{\"Name\":\"button\"}]}]}");

            try
            {
                StringAssert.Contains(
                    Assert.ThrowsException<InvalidDataException>(() => HmiJsonDocumentStore.LoadDocument(duplicateScreens)).Message,
                    "Duplicate");
                StringAssert.Contains(
                    Assert.ThrowsException<InvalidDataException>(() => HmiJsonDocumentStore.LoadDocument(duplicateItems)).Message,
                    "Duplicate");
            }
            finally
            {
                File.Delete(duplicateScreens);
                File.Delete(duplicateItems);
            }
        }

        [TestMethod]
        public void LoadDocument_AcceptsSupportedStrictDocument()
        {
            var path = WriteJson(
                "{\"SchemaVersion\":\"1.0\",\"DocumentKind\":\"Patch\",\"Screens\":[{\"Path\":\"Main\",\"Action\":\"Update\",\"Items\":[{\"Name\":\"Button\",\"Type\":\"Button\",\"Action\":\"Create\"}]}]}");
            try
            {
                var document = HmiJsonDocumentStore.LoadDocument(path);
                Assert.AreEqual("Patch", document.DocumentKind);
                Assert.AreEqual(1, document.Screens.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string WriteJson(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), $"opentia-hmi-json-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);
            return path;
        }
    }
}
