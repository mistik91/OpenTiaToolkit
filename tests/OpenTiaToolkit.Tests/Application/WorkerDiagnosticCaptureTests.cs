using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Application;

[TestClass]
public class WorkerDiagnosticCaptureTests
{
    [TestMethod]
    public async Task LargeStderr_IsDrainedWithOnlyBoundedTailRetained()
    {
        string text = new string('x', 2 * 1024 * 1024) + "native failure tail";
        var capture = new WorkerDiagnosticCapture(new StringReader(text), maxCharacters: 137);
        await capture.Completion;
        Assert.AreEqual(text.Substring(text.Length - 137), capture.GetTail());
        Assert.IsFalse(capture.ForwardingFailed);
    }

    [TestMethod]
    public async Task OptInSink_ReceivesExactFullStreamWithoutTailTruncation()
    {
        string text = "first\r\n" + new string('x', 12000) + "\nSiemens: Δ\n";
        var forwarded = new StringBuilder();
        var capture = new WorkerDiagnosticCapture(new StringReader(text), value => forwarded.Append(value), 31);
        await capture.Completion;
        Assert.AreEqual(text, forwarded.ToString());
        Assert.AreEqual(text.Substring(text.Length - 31), capture.GetTail());
    }

    [TestMethod]
    public async Task StreamIsDrainedBeforeEof_AndDoesNotRequireProcessExit()
    {
        var reader = new GatedReader();
        var capture = new WorkerDiagnosticCapture(reader);
        Assert.AreEqual("live diagnostic", capture.GetTail());
        Assert.IsFalse(capture.Completion.IsCompleted);
        reader.Finish.SetResult(0);
        await capture.Completion;
    }

    [TestMethod]
    public async Task FailedOptionalSink_DoesNotStopDrain()
    {
        string text = new string('a', 24000) + "end";
        int sinkCalls = 0;
        var capture = new WorkerDiagnosticCapture(new StringReader(text), value =>
        {
            sinkCalls++;
            throw new IOException("unavailable sink");
        }, 17);
        await capture.Completion;
        Assert.AreEqual(1, sinkCalls);
        Assert.IsTrue(capture.ForwardingFailed);
        Assert.AreEqual(text.Substring(text.Length - 17), capture.GetTail());
    }

    [TestMethod]
    public async Task ReaderFailure_IsObservedWithoutLeakingItsMessage()
    {
        var capture = new WorkerDiagnosticCapture(new FailingReader());
        await capture.Completion;
        StringAssert.Contains(capture.GetTail(), "IOException");
        Assert.IsFalse(capture.GetTail().Contains("sensitive-message"));
    }

    [TestMethod]
    public void InvalidBounds_AreRejected()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new WorkerDiagnosticCapture(new StringReader(""), maxCharacters: 0));
        Assert.ThrowsException<ArgumentNullException>(() => new WorkerDiagnosticCapture(null!));
    }

    private sealed class GatedReader : TextReader
    {
        private bool _started;
        public TaskCompletionSource<int> Finish { get; } = new TaskCompletionSource<int>();
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            if (_started) return Finish.Task;
            _started = true;
            const string text = "live diagnostic";
            text.CopyTo(0, buffer, index, text.Length);
            return Task.FromResult(text.Length);
        }
    }

    private sealed class FailingReader : TextReader
    {
        public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
            Task.FromException<int>(new IOException("sensitive-message"));
    }
}
