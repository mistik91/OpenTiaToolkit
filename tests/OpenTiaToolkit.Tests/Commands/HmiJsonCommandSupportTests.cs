using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class HmiJsonCommandSupportTests
    {
        [TestMethod]
        public void TryGetScreenArgument_ParsesJsonElementObject()
        {
            using var document = JsonDocument.Parse("{\"path\":\"Screens/Main\",\"name\":\"Main\",\"items\":[{\"name\":\"Btn1\",\"type\":\"HmiButton\",\"properties\":{\"Text\":\"Start\"}}]}");
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["edited-screen"] = document.RootElement.Clone()
            });

            var parsed = HmiJsonCommandSupport.TryGetScreenArgument(args, "edited-screen", out var screen, out var error);

            Assert.IsTrue(parsed, error);
            Assert.IsNotNull(screen);
            Assert.AreEqual("Screens/Main", screen!.Path);
            Assert.AreEqual("Btn1", screen.Items[0].Name);
            Assert.AreEqual("Start", screen.Items[0].Properties["Text"]);
        }

        [TestMethod]
        public void TryGetScreenArgument_ParsesJsonString()
        {
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["edited-screen"] = "{\"Path\":\"Screens/Main\",\"Name\":\"Main\"}"
            });

            var parsed = HmiJsonCommandSupport.TryGetScreenArgument(args, "edited-screen", out var screen, out var error);

            Assert.IsTrue(parsed, error);
            Assert.IsNotNull(screen);
            Assert.AreEqual("Screens/Main", screen!.Path);
        }

        [TestMethod]
        public void TryGetPatchOperationsArgument_ParsesJsonElementArray()
        {
            using var document = JsonDocument.Parse("[{\"op\":\"set-property\",\"item-name\":\"Btn1\",\"property\":\"Text\",\"value\":\"Start\"}]");
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["operations"] = document.RootElement.Clone()
            });

            var parsed = HmiJsonCommandSupport.TryGetPatchOperationsArgument(args, "operations", out var operations, out var error);

            Assert.IsTrue(parsed, error);
            Assert.IsNotNull(operations);
            var operation = operations!.Single();
            Assert.AreEqual("set-property", operation.Op);
            Assert.AreEqual("Text", operation.Property);
            Assert.IsNotNull(operation.ExtensionData);
            Assert.IsTrue(operation.ExtensionData!.ContainsKey("item-name"));
        }

        [TestMethod]
        public void TryGetPatchOperationsArgument_ParsesWrappedJsonString()
        {
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["operations"] = "{\"operations\":[{\"op\":\"move-item\",\"item\":\"Btn1\",\"left\":24,\"top\":36}]}"
            });

            var parsed = HmiJsonCommandSupport.TryGetPatchOperationsArgument(args, "operations", out var operations, out var error);

            Assert.IsTrue(parsed, error);
            Assert.IsNotNull(operations);
            var operation = operations!.Single();
            Assert.AreEqual("move-item", operation.Op);
            Assert.AreEqual("Btn1", operation.Item);
            Assert.AreEqual(24, operation.Left);
            Assert.AreEqual(36, operation.Top);
        }
    }
}
