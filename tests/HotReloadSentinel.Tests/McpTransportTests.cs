using System.Text;
using System.Text.Json;
using HotReloadSentinel.Mcp;
using Xunit;

namespace HotReloadSentinel.Tests;

public class McpTransportTests
{
    [Fact]
    public async Task ReadMessage_Jsonl_ParsesCorrectly()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"initialize"}""";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(json + "\n"));
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        var message = await transport.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("initialize", message.Value.GetProperty("method").GetString());
    }

    [Fact]
    public async Task ReadMessage_ContentLength_ParsesCorrectly()
    {
        var body = """{"jsonrpc":"2.0","id":1,"method":"initialize"}""";
        var frame = $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(frame));
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        var message = await transport.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("initialize", message.Value.GetProperty("method").GetString());
    }

    [Fact]
    public async Task WriteMessage_Jsonl_WritesNewlineTerminated()
    {
        // Send a JSONL message first to trigger JSONL detection
        var json = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(json + "\n"));
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        // Read to trigger JSONL detection
        await transport.ReadMessageAsync(CancellationToken.None);

        var response = JsonSerializer.Deserialize<JsonElement>("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        await transport.WriteMessageAsync(response, CancellationToken.None);

        var written = Encoding.UTF8.GetString(output.ToArray());
        Assert.EndsWith("\n", written);
        Assert.DoesNotContain("Content-Length", written);
    }

    [Fact]
    public async Task WriteMessage_ContentLength_WritesFramed()
    {
        // Send a Content-Length message to trigger that mode
        var body = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";
        var frame = $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(frame));
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        // Read to trigger Content-Length detection
        await transport.ReadMessageAsync(CancellationToken.None);

        var response = JsonSerializer.Deserialize<JsonElement>("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        await transport.WriteMessageAsync(response, CancellationToken.None);

        var written = Encoding.UTF8.GetString(output.ToArray());
        Assert.StartsWith("Content-Length:", written);
    }

    [Fact]
    public async Task ReadMessage_SkipsBlankLines()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";
        var input = new MemoryStream(Encoding.UTF8.GetBytes("\n\n" + json + "\n"));
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        var message = await transport.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("ping", message.Value.GetProperty("method").GetString());
    }

    [Fact]
    public async Task ReadMessage_ReturnsNullOnEof()
    {
        var input = new MemoryStream(Array.Empty<byte>());
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        var message = await transport.ReadMessageAsync(CancellationToken.None);

        Assert.Null(message);
    }

    [Fact]
    public async Task ReadMessage_WindowsLineEndings_HandledCorrectly()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(json + "\r\n"));
        var output = new MemoryStream();
        var transport = new McpTransport(input, output);

        var message = await transport.ReadMessageAsync(CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal("ping", message.Value.GetProperty("method").GetString());
    }
}
