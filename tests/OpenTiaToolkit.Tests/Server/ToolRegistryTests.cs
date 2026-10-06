using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.MCP.Server;

namespace OpenTiaToolkit.Tests.Server
{
    [TestClass]
    public class ToolRegistryTests
    {
        [TestMethod]
        public void ListTools_IsDeterministicAndMatchesRuntimeCatalogPlusInfrastructure()
        {
            var session = new FakeToolSession(
                Descriptor("zeta-tool"),
                Descriptor("alpha-tool"));
            using (var registry = CreateRegistry(() => session))
            {
                List<Tool> first = registry.ListTools();
                List<Tool> second = registry.ListTools();

                CollectionAssert.AreEqual(
                    first.Select(tool => tool.Name).ToArray(),
                    second.Select(tool => tool.Name).ToArray());
                CollectionAssert.AreEqual(
                    first.Select(tool => tool.Name)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    first.Select(tool => tool.Name).ToArray());

                string[] infrastructure =
                {
                    "operation-status",
                    "cancel-operation",
                    "reset-session"
                };
                CollectionAssert.AreEquivalent(
                    session.Commands.Select(command => command.Name)
                        .Concat(infrastructure)
                        .ToArray(),
                    first.Select(tool => tool.Name).ToArray());
                Assert.AreEqual(
                    first.Count,
                    first.Select(tool => tool.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count());
            }
        }

        [TestMethod]
        public void ListTools_PreservesRequiredArrayObjectPathAndDefaultSchemaSemantics()
        {
            var descriptor = Descriptor(
                "schema-tool",
                new CommandParameterContract
                {
                    Name = "required-name",
                    Kind = CommandParameterKind.String,
                    Required = true,
                    Description = "Required string"
                },
                new CommandParameterContract
                {
                    Name = "names",
                    Kind = CommandParameterKind.StringArray,
                    Description = "String array"
                },
                new CommandParameterContract
                {
                    Name = "objects",
                    Kind = CommandParameterKind.ObjectArray,
                    Description = "Object array"
                },
                new CommandParameterContract
                {
                    Name = "settings",
                    Kind = CommandParameterKind.Object,
                    Description = "Object"
                },
                new CommandParameterContract
                {
                    Name = "input-path",
                    Kind = CommandParameterKind.Path,
                    Description = "Path"
                },
                new CommandParameterContract
                {
                    Name = "dry-run",
                    Kind = CommandParameterKind.Boolean,
                    Description = "Boolean with default",
                    DefaultValue = JsonSerializer.SerializeToElement(true)
                });
            using (var registry = CreateRegistry(() => new FakeToolSession(descriptor)))
            {
                Tool tool = registry.ListTools().Single(candidate => candidate.Name == descriptor.Name);
                JsonElement schema = tool.InputSchema;
                Assert.AreEqual(JsonValueKind.Object, schema.ValueKind);
                Assert.AreEqual("object", schema.GetProperty("type").GetString());
                Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());

                JsonElement properties = schema.GetProperty("properties");
                Assert.AreEqual(
                    "string",
                    properties.GetProperty("names").GetProperty("items").GetProperty("type").GetString());
                Assert.AreEqual(
                    "object",
                    properties.GetProperty("objects").GetProperty("items").GetProperty("type").GetString());
                Assert.AreEqual(
                    "object",
                    properties.GetProperty("settings").GetProperty("type").GetString());
                Assert.AreEqual(
                    "string",
                    properties.GetProperty("input-path").GetProperty("type").GetString());
                Assert.IsTrue(properties.GetProperty("dry-run").GetProperty("default").GetBoolean());
                CollectionAssert.AreEqual(
                    new[] { "required-name" },
                    schema.GetProperty("required")
                        .EnumerateArray()
                        .Select(value => value.GetString())
                        .ToArray());
            }
        }

        [DataTestMethod]
        [DataRow("Bad_Name")]
        [DataRow("operation-status")]
        public void Constructor_RejectsInvalidOrInfrastructureCollidingRuntimeName(string name)
        {
            var session = new FakeToolSession(Descriptor(name));

            Assert.ThrowsException<InvalidOperationException>(
                () => CreateRegistry(() => session));
            Assert.IsTrue(session.Disposed);
        }

        [TestMethod]
        public void Constructor_RejectsCaseInsensitiveDuplicateRuntimeNames()
        {
            var session = new FakeToolSession(
                Descriptor("duplicate-tool"),
                Descriptor("duplicate-tool"));

            Assert.ThrowsException<ArgumentException>(
                () => CreateRegistry(() => session));
            Assert.IsTrue(session.Disposed);
        }

        [TestMethod]
        public async Task CallToolAsync_SuccessReturnsStructuredAndTextFallbackContent()
        {
            var session = new FakeToolSession(Descriptor("echo-tool"));
            session.Execute = (invocation, cancellationToken) => Task.FromResult(
                Outcome(
                    success: true,
                    message: "ok",
                    data: JsonSerializer.SerializeToElement(new { value = 42 })));
            using (var registry = CreateRegistry(() => session))
            {
                CallToolResult result = await registry.CallToolAsync(
                    "echo-tool",
                    null,
                    CancellationToken.None);

                Assert.AreEqual(false, result.IsError);
                JsonElement structured = result.StructuredContent!.Value;
                Assert.IsTrue(structured.GetProperty("success").GetBoolean());
                Assert.AreEqual(
                    42,
                    structured.GetProperty("data").GetProperty("value").GetInt32());
                var text = (TextContentBlock)result.Content.Single();
                using (JsonDocument fallback = JsonDocument.Parse(text.Text))
                {
                    Assert.IsTrue(fallback.RootElement.GetProperty("success").GetBoolean());
                    Assert.AreEqual(
                        42,
                        fallback.RootElement
                            .GetProperty("data")
                            .GetProperty("value")
                            .GetInt32());
                }
            }
        }

        [TestMethod]
        public async Task CallToolAsync_FailedOutcomeMapsToToolExecutionError()
        {
            var session = new FakeToolSession(Descriptor("failure-tool"));
            session.Execute = (invocation, cancellationToken) => Task.FromResult(
                Outcome(
                    success: false,
                    message: "denied",
                    errorCode: ErrorCode.PolicyDenied));
            using (var registry = CreateRegistry(() => session))
            {
                CallToolResult result = await registry.CallToolAsync(
                    "failure-tool",
                    null,
                    CancellationToken.None);

                Assert.AreEqual(true, result.IsError);
                JsonElement structured = result.StructuredContent!.Value;
                Assert.IsFalse(structured.GetProperty("success").GetBoolean());
                Assert.AreEqual(
                    "PolicyDenied",
                    structured.GetProperty("error").GetProperty("code").GetString());
            }
        }

        [TestMethod]
        public async Task CallToolAsync_LargeResultSpillsToUtf8Artifact()
        {
            string payload = new string('x', (512 * 1024) + 1);
            var session = new FakeToolSession(Descriptor("large-result-tool"));
            session.Execute = (invocation, cancellationToken) => Task.FromResult(
                Outcome(
                    success: true,
                    message: "large",
                    data: JsonSerializer.SerializeToElement(new { payload })));
            string? outputPath = null;
            try
            {
                using (var registry = CreateRegistry(() => session))
                {
                    CallToolResult result = await registry.CallToolAsync(
                        "large-result-tool",
                        null,
                        CancellationToken.None);

                    JsonElement data = result.StructuredContent!.Value.GetProperty("data");
                    outputPath = data.GetProperty("outputPath").GetString();
                    Assert.IsFalse(result.IsError);
                    Assert.IsTrue(data.GetProperty("inlineOutputSuppressed").GetBoolean());
                    Assert.AreEqual("json", data.GetProperty("format").GetString());
                    Assert.IsNotNull(outputPath);
                    Assert.IsTrue(File.Exists(outputPath));
                    Assert.IsTrue(new FileInfo(outputPath).Length > 512 * 1024);
                    using (JsonDocument artifact = JsonDocument.Parse(File.ReadAllText(outputPath)))
                    {
                        Assert.AreEqual(
                            payload.Length,
                            artifact.RootElement.GetProperty("payload").GetString()!.Length);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }

        [TestMethod]
        public async Task CallToolAsync_WorkerExceptionMapsToWorkerUnavailable()
        {
            var session = new FakeToolSession(Descriptor("broken-worker-tool"));
            session.Execute = (invocation, cancellationToken) =>
                Task.FromException<CommandOutcome>(
                    new IOException("The isolated worker pipe closed."));
            using (var registry = CreateRegistry(() => session))
            {
                CallToolResult result = await registry.CallToolAsync(
                    "broken-worker-tool",
                    null,
                    CancellationToken.None);

                Assert.IsTrue(result.IsError);
                Assert.AreEqual(
                    "WorkerUnavailable",
                    result.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
                StringAssert.Contains(
                    result.StructuredContent!.Value.GetProperty("message").GetString(),
                    "pipe closed");
            }
        }

        [TestMethod]
        public async Task CallToolAsync_UnknownToolThrowsInvalidParamsProtocolError()
        {
            using (var registry = CreateRegistry(() => new FakeToolSession(Descriptor("known-tool"))))
            {
                try
                {
                    await registry.CallToolAsync(
                        "missing-tool",
                        null,
                        CancellationToken.None);
                    Assert.Fail("Expected an MCP protocol exception.");
                }
                catch (McpProtocolException ex)
                {
                    Assert.AreEqual(McpErrorCode.InvalidParams, ex.ErrorCode);
                    StringAssert.Contains(ex.Message, "missing-tool");
                }
            }
        }

        [TestMethod]
        public async Task CallToolAsync_RejectsOverlappingWorkerCallsWithConflictResult()
        {
            var completion = new TaskCompletionSource<CommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new FakeToolSession(Descriptor("serialized-tool"));
            session.Execute = (invocation, cancellationToken) => completion.Task;
            using (var registry = CreateRegistry(() => session))
            {
                Task<CallToolResult> first = registry.CallToolAsync(
                    "serialized-tool",
                    null,
                    CancellationToken.None).AsTask();
                await session.ExecutionStarted.Task;

                CallToolResult conflict = await registry.CallToolAsync(
                    "serialized-tool",
                    null,
                    CancellationToken.None);

                Assert.AreEqual(true, conflict.IsError);
                Assert.AreEqual(
                    "Conflict",
                    conflict.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
                completion.SetResult(Outcome(true, "done"));
                Assert.AreEqual(false, (await first).IsError);
            }
        }

        [TestMethod]
        public async Task CallToolAsync_PropagatesCancellationAndMapsCancelledOutcome()
        {
            var session = new FakeToolSession(Descriptor("cancel-tool"));
            session.Execute = async (invocation, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                return Outcome(true, "unexpected");
            };
            using (var registry = CreateRegistry(() => session))
            using (var cancellation = new CancellationTokenSource())
            {
                Task<CallToolResult> call = registry.CallToolAsync(
                    "cancel-tool",
                    null,
                    cancellation.Token).AsTask();
                await session.ExecutionStarted.Task;
                cancellation.Cancel();

                CallToolResult result = await call;
                Assert.AreEqual(true, result.IsError);
                Assert.IsTrue(session.LastCancellationToken.CanBeCanceled);
                Assert.AreEqual(
                    "Cancelled",
                    result.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
            }
        }

        [TestMethod]
        public async Task CallToolAsync_TimeoutTerminatesWorkerAndUsesReplacement()
        {
            var completion = new TaskCompletionSource<CommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstSession = new FakeToolSession(Descriptor("timeout-tool"));
            firstSession.Execute = (invocation, cancellationToken) => completion.Task;
            firstSession.OnTerminate = () => completion.TrySetResult(Outcome(false, "terminated"));
            var replacement = new FakeToolSession(Descriptor("timeout-tool"));
            var sessions = new Queue<IToolSession>(new IToolSession[]
            {
                firstSession,
                replacement
            });
            using (var registry = CreateRegistry(
                () => sessions.Dequeue(),
                new McpRuntimeOptions { ToolTimeout = TimeSpan.FromMilliseconds(25) }))
            {
                CallToolResult result = await registry.CallToolAsync(
                    "timeout-tool",
                    null,
                    CancellationToken.None);

                Assert.AreEqual(true, result.IsError);
                Assert.IsTrue(firstSession.Terminated);
                Assert.AreEqual(
                    "TimedOut",
                    result.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());

                CallToolResult status = await registry.CallToolAsync(
                    "operation-status",
                    null,
                    CancellationToken.None);
                Assert.AreEqual(false, status.IsError);
            }
        }

        [TestMethod]
        public async Task CancelOperation_TerminatesActiveWorkerAndReplacementRemainsUsable()
        {
            var completion = new TaskCompletionSource<CommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstSession = new FakeToolSession(Descriptor("long-read-tool"));
            firstSession.Execute = (invocation, cancellationToken) => completion.Task;
            firstSession.OnTerminate = () => completion.TrySetResult(
                Outcome(false, "terminated", errorCode: ErrorCode.Cancelled));
            var replacement = new FakeToolSession(Descriptor("long-read-tool"));
            var sessions = new Queue<IToolSession>(new IToolSession[]
            {
                firstSession,
                replacement
            });
            using (var registry = CreateRegistry(() => sessions.Dequeue()))
            {
                Task<CallToolResult> active = registry.CallToolAsync(
                    "long-read-tool",
                    null,
                    CancellationToken.None).AsTask();
                await firstSession.ExecutionStarted.Task;

                CallToolResult cancelled = await registry.CallToolAsync(
                    "cancel-operation",
                    null,
                    CancellationToken.None);
                CallToolResult activeResult = await active;
                CallToolResult recovered = await registry.CallToolAsync(
                    "long-read-tool",
                    null,
                    CancellationToken.None);

                Assert.IsFalse(cancelled.IsError);
                Assert.IsTrue(firstSession.Terminated);
                Assert.IsTrue(activeResult.IsError);
                Assert.AreEqual(
                    "Cancelled",
                    activeResult.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
                Assert.IsFalse(recovered.IsError);
                Assert.AreEqual(1, replacement.ExecutionCount);
            }
        }

        [TestMethod]
        public async Task ResetSession_DisposesWorkerAndReplacementRemainsUsable()
        {
            var firstSession = new FakeToolSession(Descriptor("read-tool"));
            var replacement = new FakeToolSession(Descriptor("read-tool"));
            var sessions = new Queue<IToolSession>(new IToolSession[]
            {
                firstSession,
                replacement
            });
            using (var registry = CreateRegistry(() => sessions.Dequeue()))
            {
                CallToolResult reset = await registry.CallToolAsync(
                    "reset-session",
                    null,
                    CancellationToken.None);
                CallToolResult result = await registry.CallToolAsync(
                    "read-tool",
                    null,
                    CancellationToken.None);

                Assert.AreEqual(false, reset.IsError);
                Assert.IsTrue(firstSession.Disposed);
                Assert.AreEqual(1, replacement.ExecutionCount);
                Assert.AreEqual(false, result.IsError);
            }
        }

        [TestMethod]
        public async Task ResetSession_CancelsActiveCallThenReplacesQuiescentWorker()
        {
            var firstSession = new FakeToolSession(Descriptor("long-read-tool"));
            firstSession.Execute = async (invocation, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                return Outcome(true, "unexpected");
            };
            var replacement = new FakeToolSession(Descriptor("long-read-tool"));
            var sessions = new Queue<IToolSession>(new IToolSession[]
            {
                firstSession,
                replacement
            });
            using (var registry = CreateRegistry(
                () => sessions.Dequeue(),
                new McpRuntimeOptions
                {
                    SessionResetGracePeriod = TimeSpan.FromSeconds(1)
                }))
            {
                Task<CallToolResult> active = registry.CallToolAsync(
                    "long-read-tool",
                    null,
                    CancellationToken.None).AsTask();
                await firstSession.ExecutionStarted.Task;

                CallToolResult reset = await registry.CallToolAsync(
                    "reset-session",
                    null,
                    CancellationToken.None);
                CallToolResult activeResult = await active;

                Assert.IsFalse(reset.IsError);
                Assert.IsTrue(activeResult.IsError);
                Assert.AreEqual(
                    "Cancelled",
                    activeResult.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
                Assert.IsTrue(firstSession.LastCancellationToken.IsCancellationRequested);
                Assert.IsTrue(firstSession.Disposed);
                Assert.IsFalse(firstSession.Terminated);
            }
        }

        [TestMethod]
        public async Task ResetSession_FailsClosedWhenActiveCallDoesNotQuiesce()
        {
            var completion = new TaskCompletionSource<CommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstSession = new FakeToolSession(Descriptor("stuck-read-tool"));
            firstSession.Execute = (invocation, cancellationToken) => completion.Task;
            var replacement = new FakeToolSession(Descriptor("stuck-read-tool"));
            var sessions = new Queue<IToolSession>(new IToolSession[]
            {
                firstSession,
                replacement
            });
            using (var registry = CreateRegistry(
                () => sessions.Dequeue(),
                new McpRuntimeOptions
                {
                    SessionResetGracePeriod = TimeSpan.FromMilliseconds(25)
                }))
            {
                Task<CallToolResult> active = registry.CallToolAsync(
                    "stuck-read-tool",
                    null,
                    CancellationToken.None).AsTask();
                await firstSession.ExecutionStarted.Task;

                CallToolResult reset = await registry.CallToolAsync(
                    "reset-session",
                    null,
                    CancellationToken.None);

                Assert.IsTrue(reset.IsError);
                Assert.AreEqual(
                    "Conflict",
                    reset.StructuredContent!.Value
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
                Assert.IsTrue(firstSession.LastCancellationToken.IsCancellationRequested);
                Assert.IsFalse(firstSession.Disposed);
                Assert.IsFalse(firstSession.Terminated);
                Assert.AreEqual(1, sessions.Count);

                completion.SetResult(Outcome(true, "finished"));
                Assert.IsFalse((await active).IsError);
            }
        }

        private static ToolRegistry CreateRegistry(
            Func<IToolSession> startSession,
            McpRuntimeOptions? options = null)
        {
            return new ToolRegistry(
                startSession,
                options ?? new McpRuntimeOptions(),
                NullLogger<ToolRegistry>.Instance);
        }

        private static CommandDescriptor Descriptor(
            string name,
            params CommandParameterContract[] parameters)
        {
            return new CommandDescriptor
            {
                Name = name,
                Description = name,
                Risk = CommandRisk.ReadOnly,
                Parameters = parameters
            };
        }

        private static CommandOutcome Outcome(
            bool success,
            string message,
            JsonElement? data = null,
            ErrorCode errorCode = ErrorCode.None)
        {
            return new CommandOutcome
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                Success = success,
                Message = message,
                Data = data,
                Error = success
                    ? null
                    : new ErrorDetail { Code = errorCode, Message = message },
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private sealed class FakeToolSession : IToolSession
        {
            public FakeToolSession(params CommandDescriptor[] commands)
            {
                Commands = commands;
            }

            public IReadOnlyList<CommandDescriptor> Commands { get; }

            public Func<CommandInvocation, CancellationToken, Task<CommandOutcome>> Execute { get; set; } =
                (invocation, cancellationToken) => Task.FromResult(Outcome(true, "ok"));

            public TaskCompletionSource<bool> ExecutionStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Action? OnTerminate { get; set; }

            public CancellationToken LastCancellationToken { get; private set; }

            public int ExecutionCount { get; private set; }

            public bool Disposed { get; private set; }

            public bool Terminated { get; private set; }

            public Task<CommandOutcome> ExecuteAsync(
                CommandInvocation invocation,
                CancellationToken cancellationToken)
            {
                ExecutionCount++;
                LastCancellationToken = cancellationToken;
                ExecutionStarted.TrySetResult(true);
                return Execute(invocation, cancellationToken);
            }

            public void Dispose()
            {
                Disposed = true;
            }

            public void Terminate()
            {
                Terminated = true;
                OnTerminate?.Invoke();
            }
        }
    }
}
