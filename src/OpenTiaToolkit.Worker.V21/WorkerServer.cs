using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.Worker.V21;

internal sealed class WorkerServer
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    private readonly string _pipeName;
    private readonly WorkerCommandExecutor _executor;

    public WorkerServer(string pipeName, WorkerCommandExecutor executor)
    {
        _pipeName = pipeName;
        _executor = executor;
    }

    public async Task RunAsync()
    {
        using (var pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous))
        {
            await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true })
            {
                bool shutdown = false;
                while (!shutdown && pipe.IsConnected)
                {
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;

                    WorkerResponse response;
                    try
                    {
                        if (Encoding.UTF8.GetByteCount(line) > ProtocolConstants.DefaultMaxMessageBytes)
                        {
                            throw new InvalidDataException("Worker IPC request exceeded the maximum message size.");
                        }

                        WorkerRequest request = JsonSerializer.Deserialize<WorkerRequest>(line, JsonOptions)
                            ?? throw new InvalidDataException("Worker IPC request was empty.");
                        response = await HandleAsync(request).ConfigureAwait(false);
                        shutdown = request.Operation == WorkerOperation.Shutdown;
                    }
                    catch (Exception ex)
                    {
                        response = new WorkerResponse
                        {
                            Success = false,
                            Error = new ErrorDetail { Code = ErrorCode.InvalidRequest, Message = ex.Message }
                        };
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<WorkerResponse> HandleAsync(WorkerRequest request)
    {
        if (!string.Equals(request.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
        {
            return Error(request, ErrorCode.ProtocolMismatch,
                $"Worker protocol '{request.ProtocolVersion}' is incompatible with '{ProtocolConstants.Version}'.");
        }

        switch (request.Operation)
        {
            case WorkerOperation.Health:
                return new WorkerResponse { RequestId = request.RequestId, Success = true };
            case WorkerOperation.Catalog:
                return new WorkerResponse { RequestId = request.RequestId, Success = true, Catalog = _executor.GetCatalog() };
            case WorkerOperation.Execute:
                if (request.Invocation == null) return Error(request, ErrorCode.InvalidRequest, "Execute requires an invocation.");
                CommandOutcome outcome = await _executor.ExecuteAsync(request.Invocation).ConfigureAwait(false);
                return new WorkerResponse { RequestId = request.RequestId, Success = outcome.Success, Outcome = outcome, Error = outcome.Error };
            case WorkerOperation.Shutdown:
                return new WorkerResponse { RequestId = request.RequestId, Success = true };
            default:
                return Error(request, ErrorCode.InvalidRequest, "Unsupported worker operation.");
        }
    }

    private static WorkerResponse Error(WorkerRequest request, ErrorCode code, string message)
    {
        return new WorkerResponse
        {
            RequestId = request.RequestId,
            Success = false,
            Error = new ErrorDetail { Code = code, Message = message }
        };
    }
}
