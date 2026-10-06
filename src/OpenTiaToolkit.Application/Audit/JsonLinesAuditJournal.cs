using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Audit;
using OpenTiaToolkit.Contracts.Protocol;

namespace OpenTiaToolkit.Application.Audit;

public sealed class JsonLinesAuditJournal : IAuditJournal
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public JsonLinesAuditJournal(string path)
    {
        _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
    }

    public async Task AppendAsync(MutationAuditRecord record, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(record, ContractJson.CreateOptions());
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using (var writer = new StreamWriter(_path, append: true))
            {
                await writer.WriteLineAsync(json).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
