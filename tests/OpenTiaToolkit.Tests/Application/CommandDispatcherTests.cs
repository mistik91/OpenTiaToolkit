using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Audit;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Tests.Application
{
    [TestClass]
    public class CommandDispatcherTests
    {
        [TestMethod]
        public async Task ExecuteAsync_DeniesUnconfirmedMutationBeforeTransportAndAuditsDecision()
        {
            var transport = new RecordingTransport();
            var journal = new RecordingJournal();
            var dispatcher = CreateDispatcher(transport, journal, CommandRisk.Mutating, supportsPreview: false);

            CommandOutcome outcome = await dispatcher.ExecuteAsync(
                Invocation(new Dictionary<string, JsonElement>()),
                CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(ErrorCode.PolicyDenied, outcome.Error?.Code);
            Assert.AreEqual(0, transport.CallCount);
            Assert.AreEqual(1, journal.Records.Count);
            Assert.IsFalse(journal.Records[0].Allowed);
        }

        [TestMethod]
        public async Task ExecuteAsync_AllowsPreviewAndAuditsRedactedArguments()
        {
            var transport = new RecordingTransport();
            var journal = new RecordingJournal();
            var dispatcher = CreateDispatcher(transport, journal, CommandRisk.Mutating, supportsPreview: true);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["dry-run"] = Json("true"),
                ["safety-password-env"] = Json("\"OPENTIA_SAFETY_PASSWORD\"")
            };

            CommandOutcome outcome = await dispatcher.ExecuteAsync(Invocation(arguments), CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(1, transport.CallCount);
            Assert.IsTrue(journal.Records[0].Preview);
            Assert.AreEqual("[REDACTED]", journal.Records[0].RedactedArguments["safety-password-env"]);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReadOnlyCommandBypassesMutationJournal()
        {
            var transport = new RecordingTransport();
            var journal = new RecordingJournal();
            var dispatcher = CreateDispatcher(transport, journal, CommandRisk.ReadOnly, supportsPreview: false);

            CommandOutcome outcome = await dispatcher.ExecuteAsync(Invocation(new Dictionary<string, JsonElement>()), CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(1, transport.CallCount);
            Assert.AreEqual(0, journal.Records.Count);
        }

        [TestMethod]
        public async Task ExecuteAsync_MapsTransportFailureToTypedWorkerUnavailableOutcome()
        {
            var transport = new RecordingTransport { Failure = new IOException("pipe closed") };
            var dispatcher = CreateDispatcher(transport, new RecordingJournal(), CommandRisk.ReadOnly, supportsPreview: false);

            CommandOutcome outcome = await dispatcher.ExecuteAsync(Invocation(new Dictionary<string, JsonElement>()), CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(ErrorCode.WorkerUnavailable, outcome.Error?.Code);
            StringAssert.Contains(outcome.Message, "pipe closed");
        }

        private static CommandDispatcher CreateDispatcher(
            RecordingTransport transport,
            RecordingJournal journal,
            CommandRisk risk,
            bool supportsPreview)
        {
            return new CommandDispatcher(
                transport,
                new MutationPolicy(),
                journal,
                new[]
                {
                    new CommandDescriptor
                    {
                        Name = "test-command",
                        Description = "test",
                        Risk = risk,
                        SupportsPreview = supportsPreview
                    }
                });
        }

        private static CommandInvocation Invocation(IReadOnlyDictionary<string, JsonElement> arguments)
        {
            return new CommandInvocation { CommandName = "test-command", Arguments = arguments };
        }

        private static JsonElement Json(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return document.RootElement.Clone();
            }
        }

        private sealed class RecordingTransport : ICommandTransport
        {
            public int CallCount { get; private set; }
            public Exception? Failure { get; set; }

            public Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken)
            {
                CallCount++;
                if (Failure != null) throw Failure;
                return Task.FromResult(new CommandOutcome
                {
                    CorrelationId = invocation.CorrelationId,
                    Success = true,
                    Message = "ok",
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingJournal : IAuditJournal
        {
            public List<MutationAuditRecord> Records { get; } = new List<MutationAuditRecord>();

            public Task AppendAsync(MutationAuditRecord record, CancellationToken cancellationToken)
            {
                Records.Add(record);
                return Task.CompletedTask;
            }
        }
    }
}
