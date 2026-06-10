using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hardware;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.MCP.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Server
{
    /// <summary>
    /// Unit tests for the MCP command-to-tool bridge.
    /// </summary>
    [TestClass]
    public class McpToolBridgeTests
    {
        [TestMethod]
        public void ToolAdapter_ProtocolTool_ExposesTypedJsonSchema()
        {
            var commandMock = CreateCommandMock(
                "rename-tool",
                new CommandSchema(
                    "Renames items",
                    new[]
                    {
                        new CommandParameter("rename-map", CommandParameterType.Object, "Rename mappings", required: true),
                        new CommandParameter("configs", CommandParameterType.ObjectArray, "Batch configs"),
                        new CommandParameter("target-blocks", CommandParameterType.StringArray, "Target blocks"),
                        new CommandParameter("instance-id", CommandParameterType.Integer, "Instance id"),
                        new CommandParameter("with-ui", CommandParameterType.Boolean, "With UI", defaultValue: true)
                    }));

            var adapter = new ToolAdapter(commandMock.Object, Mock.Of<ILogger>());
            var schema = adapter.ProtocolTool.InputSchema;

            Assert.AreEqual("object", schema.GetProperty("type").GetString());
            Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());

            var properties = schema.GetProperty("properties");
            Assert.AreEqual("object", properties.GetProperty("rename-map").GetProperty("type").GetString());
            Assert.IsTrue(properties.GetProperty("rename-map").GetProperty("additionalProperties").GetBoolean());
            Assert.AreEqual("array", properties.GetProperty("configs").GetProperty("type").GetString());
            Assert.AreEqual("object", properties.GetProperty("configs").GetProperty("items").GetProperty("type").GetString());
            Assert.AreEqual("array", properties.GetProperty("target-blocks").GetProperty("type").GetString());
            Assert.AreEqual("string", properties.GetProperty("target-blocks").GetProperty("items").GetProperty("type").GetString());
            Assert.AreEqual("integer", properties.GetProperty("instance-id").GetProperty("type").GetString());
            Assert.IsTrue(properties.GetProperty("with-ui").GetProperty("default").GetBoolean());

            var required = schema.GetProperty("required").EnumerateArray();
            bool hasRenameMap = false;
            foreach (var item in required)
            {
                if (item.GetString() == "rename-map")
                {
                    hasRenameMap = true;
                    break;
                }
            }

            Assert.IsTrue(hasRenameMap);
        }

        [TestMethod]
        public void ToolAdapter_ProtocolTool_ExposesHardwareArtifactOutputPathSchemas()
        {
            var hardwareService = Mock.Of<IHardwareService>();
            var treeAdapter = new ToolAdapter(new GetHardwareTreeCommand(hardwareService), Mock.Of<ILogger>());
            var ioAdapter = new ToolAdapter(new GetHardwareIoAddressesCommand(hardwareService), Mock.Of<ILogger>());
            var driveTelegramAdapter = new ToolAdapter(new GetHardwareDriveTelegramsCommand(hardwareService), Mock.Of<ILogger>());
            var auditAdapter = new ToolAdapter(new AuditHardwareTagMappingsCommand(hardwareService), Mock.Of<ILogger>());
            var networksAdapter = new ToolAdapter(new GetHardwareNetworksCommand(hardwareService), Mock.Of<ILogger>());

            AssertOutputPathSchema(treeAdapter.ProtocolTool.InputSchema, "JSON");
            AssertOutputPathSchema(ioAdapter.ProtocolTool.InputSchema, "CSV");
            AssertOutputPathSchema(driveTelegramAdapter.ProtocolTool.InputSchema, "CSV");
            AssertOutputPathSchema(auditAdapter.ProtocolTool.InputSchema, "CSV");
            AssertOutputPathSchema(networksAdapter.ProtocolTool.InputSchema, "JSON");
        }

        [TestMethod]
        public void ToolAdapter_ProtocolTool_ExposesPlcDiscoveryArtifactOutputPathSchemas()
        {
            var plcService = Mock.Of<IPlcService>();
            var treeAdapter = new ToolAdapter(new GetSoftwareTreeCommand(plcService), Mock.Of<ILogger>());
            var blocksAdapter = new ToolAdapter(new GetBlocksCommand(plcService), Mock.Of<ILogger>());
            var searchAdapter = new ToolAdapter(new SearchBlocksCommand(plcService), Mock.Of<ILogger>());

            AssertOutputPathSchema(treeAdapter.ProtocolTool.InputSchema, "JSON");
            AssertOutputPathSchema(blocksAdapter.ProtocolTool.InputSchema, "CSV");
            AssertOutputPathSchema(searchAdapter.ProtocolTool.InputSchema, "CSV");
        }

        [TestMethod]
        public async Task ToolAdapter_ExecuteAsync_PreservesJsonArgumentTypes()
        {
            var commandMock = CreateCommandMock(
                "rename-tool",
                new CommandSchema(
                    "Renames items",
                    new[]
                    {
                        new CommandParameter("rename-map", CommandParameterType.Object, "Rename mappings", required: true),
                        new CommandParameter("target-blocks", CommandParameterType.StringArray, "Target blocks"),
                        new CommandParameter("dry-run", CommandParameterType.Boolean, "Dry run"),
                        new CommandParameter("limit", CommandParameterType.Integer, "Limit")
                    }));

            commandMock
                .Setup(command => command.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .ReturnsAsync((CommandArgs args, ILogger _) =>
                {
                    Assert.IsTrue(args.GetObject("rename-map") is JsonElement);

                    var renameMap = (JsonElement)args.GetObject("rename-map")!;
                    Assert.AreEqual(JsonValueKind.Object, renameMap.ValueKind);
                    Assert.AreEqual("NewName", renameMap.GetProperty("OldName").GetString());

                    CollectionAssert.AreEqual(new[] { "DB1", "DB2" }, args.GetStringArray("target-blocks"));
                    Assert.IsTrue(args.GetBool("dry-run"));
                    Assert.AreEqual(7, args.GetInt("limit"));

                    return CommandResult.CreateSuccess(new { renamed = 1 }, "Applied");
                });

            var adapter = new ToolAdapter(commandMock.Object, Mock.Of<ILogger>());
            var result = await adapter.ExecuteAsync(CreateRenameToolArguments());
            var structuredContent = ToJsonObject(result.StructuredContent);

            Assert.IsFalse(result.IsError);
            Assert.AreEqual("Applied", structuredContent["message"]?.GetValue<string>());
            Assert.AreEqual(1, structuredContent["data"]?["renamed"]?.GetValue<int>());
            StringAssert.Contains(result.Text, "\"success\"");
        }

        [TestMethod]
        public async Task ToolRegistry_CallToolAsync_ReturnsStructuredContentAndText()
        {
            var commandMock = CreateCommandMock(
                "rename-tool",
                new CommandSchema("Renames items"));

            commandMock
                .Setup(command => command.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .ReturnsAsync(CommandResult.CreateSuccess(new { renamed = 2 }, "Applied"));

            var registry = new ToolRegistry(new[] { commandMock.Object }, Mock.Of<ILogger<ToolRegistry>>());
            var result = await registry.CallToolAsync("rename-tool", CreateArguments(new { }));

            Assert.IsFalse(result.IsError.GetValueOrDefault());
            Assert.AreEqual("Applied", result.StructuredContent?["message"]?.GetValue<string>());
            Assert.AreEqual(2, result.StructuredContent?["data"]?["renamed"]?.GetValue<int>());
            Assert.AreEqual(1, result.Content.Count);
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));
            StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "\"success\"");
        }

        [TestMethod]
        public async Task ToolRegistry_CallToolAsync_SpillsLargeStructuredDataToArtifact()
        {
            var commandMock = CreateCommandMock(
                "large-tool",
                new CommandSchema("Returns large data"));

            commandMock
                .Setup(command => command.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .ReturnsAsync(CommandResult.CreateSuccess(
                    new
                    {
                        text = new string('x', CommandArtifactSupport.InlineResultThresholdBytes + 1024)
                    },
                    "Large result"));

            var registry = new ToolRegistry(new[] { commandMock.Object }, Mock.Of<ILogger<ToolRegistry>>());
            var result = await registry.CallToolAsync("large-tool", CreateArguments(new { }));

            Assert.IsFalse(result.IsError.GetValueOrDefault());
            Assert.AreEqual("json", result.StructuredContent?["data"]?["format"]?.GetValue<string>());
            Assert.AreEqual("large-tool-result", result.StructuredContent?["data"]?["dataKind"]?.GetValue<string>());
            var outputPath = result.StructuredContent?["data"]?["outputPath"]?.GetValue<string>();
            Assert.IsFalse(string.IsNullOrWhiteSpace(outputPath));
            Assert.IsTrue(File.Exists(outputPath));
            StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "outputPath");
            File.Delete(outputPath!);
        }

        [TestMethod]
        public async Task ToolRegistry_CallToolAsync_UsesIsErrorForCommandFailure()
        {
            var commandMock = CreateCommandMock(
                "failing-tool",
                new CommandSchema("Fails on purpose"));

            commandMock
                .Setup(command => command.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .ReturnsAsync(CommandResult.CreateFailure("Command failed"));

            var registry = new ToolRegistry(new[] { commandMock.Object }, Mock.Of<ILogger<ToolRegistry>>());
            var result = await registry.CallToolAsync("failing-tool", CreateArguments(new { }));

            Assert.IsTrue(result.IsError.GetValueOrDefault());
            Assert.IsFalse(result.StructuredContent?["success"]?.GetValue<bool>() ?? true);
            Assert.AreEqual("Command failed", result.StructuredContent?["message"]?.GetValue<string>());
        }

        [TestMethod]
        public async Task ToolRegistry_CallToolAsync_ThrowsProtocolError_WhenToolDoesNotExist()
        {
            var registry = new ToolRegistry(new List<ICommand>(), Mock.Of<ILogger<ToolRegistry>>());

            await Assert.ThrowsExceptionAsync<McpException>(
                async () => await registry.CallToolAsync("missing-tool", CreateArguments(new { })));
        }

        [TestMethod]
        public async Task ToolRegistry_CallToolAsync_RejectsOverlappingTiaOperationsWithActiveOperationDetails()
        {
            var started = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            var commandMock = CreateCommandMock("long-tool", new CommandSchema("Long operation"));
            commandMock
                .Setup(command => command.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .Returns(async () =>
                {
                    started.SetResult(true);
                    await release.Task;
                    return CommandResult.CreateSuccess(message: "Complete");
                });

            var registry = new ToolRegistry(new[] { commandMock.Object }, Mock.Of<ILogger<ToolRegistry>>());
            var firstCall = registry.CallToolAsync("long-tool", CreateArguments(new { })).AsTask();
            await started.Task;

            var overlapping = await registry.CallToolAsync("long-tool", CreateArguments(new { }));

            Assert.IsTrue(overlapping.IsError.GetValueOrDefault());
            Assert.IsTrue(overlapping.StructuredContent?["data"]?["busy"]?.GetValue<bool>() ?? false);
            Assert.AreEqual("long-tool", overlapping.StructuredContent?["data"]?["activeToolName"]?.GetValue<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(overlapping.StructuredContent?["data"]?["activeOperationId"]?.GetValue<string>()));

            release.SetResult(true);
            var completed = await firstCall;
            Assert.IsFalse(completed.IsError.GetValueOrDefault());
        }

        private static Mock<ICommand> CreateCommandMock(string name, CommandSchema schema)
        {
            var commandMock = new Mock<ICommand>();
            commandMock.Setup(command => command.Name).Returns(name);
            commandMock.Setup(command => command.Description).Returns(schema.Description);
            commandMock.Setup(command => command.Schema).Returns(schema);
            commandMock.Setup(command => command.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .ReturnsAsync(CommandResult.CreateSuccess());

            return commandMock;
        }

        private static IReadOnlyDictionary<string, JsonElement> CreateArguments(object value)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            var arguments = new Dictionary<string, JsonElement>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                arguments[property.Name] = property.Value.Clone();
            }

            return arguments;
        }

        private static IReadOnlyDictionary<string, JsonElement> CreateRenameToolArguments()
        {
            return CreateArguments(new Dictionary<string, object?>
            {
                ["rename_map"] = new Dictionary<string, string>
                {
                    ["OldName"] = "NewName"
                },
                ["target_blocks"] = new[] { "DB1", "DB2" },
                ["dry_run"] = true,
                ["limit"] = 7
            });
        }

        private static JsonObject ToJsonObject(object value)
        {
            return JsonNode.Parse(JsonSerializer.Serialize(value))!.AsObject();
        }

        private static void AssertOutputPathSchema(JsonElement schema, string format)
        {
            var outputPath = schema
                .GetProperty("properties")
                .GetProperty("output-path");

            Assert.AreEqual("string", outputPath.GetProperty("type").GetString());
            StringAssert.Contains(outputPath.GetProperty("description").GetString(), format);
        }
    }
}
