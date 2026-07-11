#if NET48
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.Application.Execution;

public sealed class WorkerProcessTransport : ICommandTransport, ICommandCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    private readonly Process _process;
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
    private bool _disposed;

    private WorkerProcessTransport(Process process, NamedPipeClientStream pipe)
    {
        _process = process;
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
    }

    public static async Task<WorkerProcessTransport> StartAsync(
        string workerExecutablePath,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!File.Exists(workerExecutablePath))
        {
            throw new FileNotFoundException("OpenTiaToolkit V21 worker executable was not found.", workerExecutablePath);
        }

        string pipeName = "opentia-v21-" + Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(workerExecutablePath),
            Arguments = "--pipe " + pipeName,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(workerExecutablePath)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (environment != null)
        {
            foreach (System.Collections.Generic.KeyValuePair<string, string> pair in environment)
                startInfo.EnvironmentVariables[pair.Key] = pair.Value;
        }
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the OpenTiaToolkit V21 worker.");
        }

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            int timeoutMilliseconds = checked((int)Math.Min(int.MaxValue, startupTimeout.TotalMilliseconds));
            await Task.Run(() => pipe.Connect(timeoutMilliseconds), cancellationToken).ConfigureAwait(false);
            var transport = new WorkerProcessTransport(process, pipe);
            WorkerResponse health = await transport.SendAsync(new WorkerRequest { Operation = WorkerOperation.Health }, cancellationToken)
                .ConfigureAwait(false);
            if (!health.Success)
            {
                throw new InvalidOperationException(health.Error?.Message ?? "V21 worker health check failed.");
            }

            return transport;
        }
        catch
        {
            pipe.Dispose();
            if (!process.HasExited) process.Kill();
            process.Dispose();
            throw;
        }
    }

    public async Task<CommandDescriptor[]> GetCommandsAsync(CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(
            new WorkerRequest { Operation = WorkerOperation.Catalog },
            cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error?.Message ?? "Worker command catalog failed.");
        }

        return response.Catalog ?? Array.Empty<CommandDescriptor>();
    }

    public async Task<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(new WorkerRequest
        {
            Operation = WorkerOperation.Execute,
            Invocation = invocation
        }, cancellationToken).ConfigureAwait(false);
        return response.Outcome ?? new CommandOutcome
        {
            CorrelationId = invocation.CorrelationId,
            Success = false,
            Message = response.Error?.Message ?? "Worker returned no command outcome.",
            Error = response.Error ?? new ErrorDetail { Code = ErrorCode.WorkerUnavailable, Message = "Worker returned no command outcome." },
            StartedAtUtc = invocation.RequestedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<WorkerResponse> SendAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process.HasExited)
            {
                string stderr = await _process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"V21 worker exited with code {_process.ExitCode}: {stderr}");
            }

            string requestJson = JsonSerializer.Serialize(request, JsonOptions);
            if (Encoding.UTF8.GetByteCount(requestJson) > ProtocolConstants.DefaultMaxMessageBytes)
            {
                throw new InvalidDataException("Worker IPC request exceeded the maximum message size.");
            }

            await _writer.WriteLineAsync(requestJson).ConfigureAwait(false);
            string? responseJson = await _reader.ReadLineAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (responseJson == null)
            {
                throw new EndOfStreamException("V21 worker closed the IPC channel without a response.");
            }

            return JsonSerializer.Deserialize<WorkerResponse>(responseJson, JsonOptions)
                ?? throw new InvalidDataException("V21 worker returned an empty response.");
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (!_process.HasExited)
            {
                SendAsync(new WorkerRequest { Operation = WorkerOperation.Shutdown }, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (!_process.WaitForExit(5000)) _process.Kill();
            }
        }
        catch
        {
            if (!_process.HasExited) _process.Kill();
        }
        finally
        {
            _disposed = true;
            _writer.Dispose();
            _reader.Dispose();
            _pipe.Dispose();
            _process.Dispose();
            _requestGate.Dispose();
        }
    }

    public void Terminate()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        finally
        {
            _writer.Dispose();
            _reader.Dispose();
            _pipe.Dispose();
            _process.Dispose();
            _requestGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WorkerProcessTransport));
    }
}
#endif
