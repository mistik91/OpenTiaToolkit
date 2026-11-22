using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services;

internal static class HmiWebViewCapture
{
    private const int MaxResponseBytes = 24 * 1024 * 1024;

    internal static async Task<(byte[] Png, int Width, int Height, string TargetId)> CaptureAsync(
        Uri endpoint, string browserPath, string? targetId, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            MaxResponseContentBufferSize = 256 * 1024
        };
        using var version = await ReadJsonAsync(http, endpoint, "json/version", cancellationToken).ConfigureAwait(false);
        ReadSocket(version.RootElement, endpoint, browserPath);
        using var targets = await ReadJsonAsync(http, endpoint, "json/list", cancellationToken).ConfigureAwait(false);
        var editors = targets.RootElement.EnumerateArray()
            .Where(target => target.GetProperty("type").GetString() == "page" &&
                             target.GetProperty("title").GetString() == "Hmi Web Editor")
            .ToArray();
        var selected = editors.Where(target => string.IsNullOrWhiteSpace(targetId) ||
            target.GetProperty("id").GetString() == targetId).ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("No matching HMI editor target. Open the intended screen in the Next Gen editor.");
        if (selected.Length > 1)
            throw new InvalidOperationException("Multiple HMI editor targets are open. Close the other HMI editors, or retry with an explicit target-id: " +
                string.Join(", ", selected.Select(target => target.GetProperty("id").GetString())));

        var id = selected[0].GetProperty("id").GetString()!;
        var socketUri = ReadSocket(selected[0], endpoint, "/devtools/page/" + id);
        using var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        await socket.ConnectAsync(socketUri, cancellationToken).ConfigureAwait(false);
        var request = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Page.captureScreenshot\",\"params\":{\"format\":\"png\"}}");
        await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        using var reply = await ReadScreenshotReplyAsync(socket, cancellationToken).ConfigureAwait(false);
        var png = reply.RootElement.GetProperty("result").GetProperty("data").GetBytesFromBase64();
        var (width, height) = ReadPngDimensions(png);
        return (png, width, height, id);
    }

    internal static (int Width, int Height) ReadPngDimensions(byte[] png)
    {
        if (png.Length < 24 || BinaryPrimitives.ReadUInt64BigEndian(png.AsSpan(0, 8)) != 0x89504E470D0A1A0AUL ||
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(12, 4)) != 0x49484452)
            throw new InvalidDataException("DevTools did not return a valid PNG header.");
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (width < 1 || height < 1)
            throw new InvalidDataException("DevTools returned an empty PNG viewport.");
        return (width, height);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpClient http, Uri endpoint, string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(endpoint, path), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
    }

    private static Uri ReadSocket(JsonElement target, Uri endpoint, string expectedPath)
    {
        if (!target.TryGetProperty("webSocketDebuggerUrl", out var url) ||
            !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "ws" || uri.Host != endpoint.Host || uri.Port != endpoint.Port ||
            uri.PathAndQuery != expectedPath || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("DevTools endpoint identity did not match the selected TIA WebView2 session.");
        return uri;
    }

    private static async Task<JsonDocument> ReadScreenshotReplyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (true)
        {
            var chunk = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (chunk.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("DevTools closed the connection or sent an unexpected response.");
            if (message.Length + chunk.Count > MaxResponseBytes)
                throw new InvalidDataException("The HMI capture exceeds the maximum response size.");
            message.Write(buffer, 0, chunk.Count);
            if (!chunk.EndOfMessage) continue;
            var reply = JsonDocument.Parse(new ReadOnlyMemory<byte>(message.GetBuffer(), 0, (int)message.Length));
            if (reply.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == 1)
            {
                if (!reply.RootElement.TryGetProperty("error", out var error)) return reply;
                var detail = error.GetProperty("message").GetString();
                reply.Dispose();
                throw new InvalidOperationException("DevTools could not capture the HMI viewport: " + detail);
            }
            reply.Dispose();
            message.SetLength(0);
        }
    }
}
