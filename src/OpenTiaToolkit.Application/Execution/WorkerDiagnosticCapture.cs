using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Application.Execution;

/// <summary>Drains stderr continuously, retaining only a bounded diagnostic tail.</summary>
internal sealed class WorkerDiagnosticCapture
{
    private readonly object _gate = new object();
    private readonly StringBuilder _tail = new StringBuilder();
    private readonly int _maxCharacters;
    private bool _forwardingFailed;

    public WorkerDiagnosticCapture(TextReader reader, Action<string>? forward = null, int maxCharacters = 65536)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        if (maxCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        _maxCharacters = maxCharacters;
        Completion = DrainAsync(reader, forward);
    }

    public Task Completion { get; }
    public bool ForwardingFailed { get { lock (_gate) return _forwardingFailed; } }
    public string GetTail() { lock (_gate) return _tail.ToString(); }

    private void Append(string text)
    {
        lock (_gate)
        {
            if (text.Length >= _maxCharacters)
            {
                _tail.Clear();
                _tail.Append(text, text.Length - _maxCharacters, _maxCharacters);
                return;
            }
            _tail.Append(text);
            if (_tail.Length > _maxCharacters) _tail.Remove(0, _tail.Length - _maxCharacters);
        }
    }

    private async Task DrainAsync(TextReader reader, Action<string>? forward)
    {
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            {
                var text = new string(buffer, 0, count);
                Append(text);
                if (forward == null) continue;
                try { forward(text); }
                catch (Exception)
                {
                    // A failed optional sink must not stop draining and block the worker.
                    lock (_gate) _forwardingFailed = true;
                    forward = null;
                }
            }
        }
        catch (Exception ex)
        {
            // Stream teardown can race process disposal. Do not leak exception contents.
            Append("\n[Worker stderr reader ended: " + ex.GetType().Name + "]\n");
        }
    }
}
