using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.CLI.Routing;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Tests.Routing
{
    [TestClass]
    public class RemoteCommandRouterTests
    {
        private RecordingClient _client = null!;
        private RemoteCommandRouter _router = null!;

        [TestInitialize]
        public void Setup()
        {
            _client = new RecordingClient(new CommandDescriptor
            {
                Name = "test-command",
                Description = "Test command",
                Category = "Other",
                Risk = CommandRisk.ReadOnly,
                Parameters = new[]
                {
                    new CommandParameterContract { Name = "limit", Kind = CommandParameterKind.Integer },
                    new CommandParameterContract { Name = "plc-name", Kind = CommandParameterKind.String, Required = true },
                    new CommandParameterContract { Name = "enabled", Kind = CommandParameterKind.Boolean }
                }
            });
            _router = new RemoteCommandRouter(_client);
        }

        [TestMethod]
        public async Task ExecuteAsync_ParsesTypedArgumentsAndRoutesToWorkerClient()
        {
            CommandOutcome result = await _router.ExecuteAsync(new[]
            {
                "test-command", "--plc-name=PLC_1", "--limit", "-5", "--enabled"
            });

            Assert.IsTrue(result.Success);
            Assert.AreEqual(-5, _client.LastInvocation!.Arguments["limit"].GetInt32());
            Assert.AreEqual("PLC_1", _client.LastInvocation.Arguments["plc-name"].GetString());
            Assert.IsTrue(_client.LastInvocation.Arguments["enabled"].GetBoolean());
        }

        [DataTestMethod]
        [DataRow("unknown-command", "Unknown command")]
        [DataRow("test-command --unknown x", "Unknown parameter")]
        [DataRow("test-command stray", "Unexpected positional")]
        [DataRow("test-command", "Missing required parameter")]
        public async Task ExecuteAsync_RejectsInvalidInputBeforeTransport(string line, string expected)
        {
            CommandOutcome result = await _router.ExecuteAsync(line.Split(' '));
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, expected);
            Assert.IsNull(_client.LastInvocation);
        }

        [TestMethod]
        public void GetHelp_UsesWorkerDescriptorAndIncludesRisk()
        {
            string help = _router.GetHelp("test-command");
            StringAssert.Contains(help, "Test command");
            StringAssert.Contains(help, "Risk: ReadOnly");
            StringAssert.Contains(help, "--plc-name");
        }

        private sealed class RecordingClient : IToolkitClient
        {
            public RecordingClient(params CommandDescriptor[] commands) { Commands = commands; }
            public CommandDescriptor[] Commands { get; }
            public CommandInvocation? LastInvocation { get; private set; }

            public Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken)
            {
                LastInvocation = invocation;
                return Task.FromResult(new CommandOutcome
                {
                    CorrelationId = invocation.CorrelationId,
                    Success = true,
                    Message = "Executed",
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }
    }
}
