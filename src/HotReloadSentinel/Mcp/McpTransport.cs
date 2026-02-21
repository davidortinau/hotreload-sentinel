namespace HotReloadSentinel.Mcp;

using System.Text.Json;

/// <summary>
/// Stdio JSON-RPC transport with auto-detect for JSONL vs Content-Length headers.
/// </summary>
public sealed class McpTransport
{
    readonly StreamReader _reader;
    readonly Stream _output;
    bool _useJsonl;
    bool _detected;

    public McpTransport(Stream input, Stream output)
    {
        _reader = new StreamReader(input, leaveOpen: true);
        _output = output;
    }

    public async Task<JsonElement?> ReadMessageAsync(CancellationToken ct)
    {
        var firstLine = await _reader.ReadLineAsync(ct);
        if (firstLine is null) return null;

        var trimmed = firstLine.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            // Skip blank lines between messages
            return await ReadMessageAsync(ct);
        }

        // Auto-detect on first message
        if (!_detected)
        {
            _useJsonl = trimmed.StartsWith('{');
            _detected = true;
        }

        if (_useJsonl)
        {
            return JsonSerializer.Deserialize<JsonElement>(trimmed);
        }

        // Content-Length header mode
        var headers = new Dictionary<string, string>();
        var line = firstLine;
        while (!string.IsNullOrWhiteSpace(line))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
                headers[line[..colonIdx].Trim().ToLower()] = line[(colonIdx + 1)..].Trim();
            line = await _reader.ReadLineAsync(ct);
        }

        if (!headers.TryGetValue("content-length", out var lengthStr) || !int.TryParse(lengthStr, out var length))
            throw new InvalidOperationException("Missing Content-Length header");

        var buffer = new char[length];
        var read = await _reader.ReadBlockAsync(buffer, 0, length);
        if (read != length)
            throw new InvalidOperationException("Incomplete message body");

        return JsonSerializer.Deserialize<JsonElement>(new string(buffer, 0, read));
    }

    public async Task WriteMessageAsync(JsonElement message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        if (_useJsonl)
        {
            await _output.WriteAsync(bytes, ct);
            await _output.WriteAsync(new byte[] { (byte)'\n' }, ct);
        }
        else
        {
            var header = $"Content-Length: {bytes.Length}\r\n\r\n";
            await _output.WriteAsync(System.Text.Encoding.ASCII.GetBytes(header), ct);
            await _output.WriteAsync(bytes, ct);
        }

        await _output.FlushAsync(ct);
    }
}
