using System.Text;
using System.Text.Json;
using HotReloadSentinel.Mcp;
using HotReloadSentinel.Verdicts;
using Xunit;

namespace HotReloadSentinel.Tests;

public class McpServerTests
{
    [Theory]
    [InlineData("2024-11-05", "2024-11-05")]
    [InlineData("2025-03-26", "2025-03-26")]
    [InlineData("2025-06-18", "2025-06-18")]
    public async Task Initialize_EchoesClientProtocolVersion(string clientVersion, string expectedVersion)
    {
        var initMsg = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = clientVersion, capabilities = new { }, clientInfo = new { name = "test", version = "1.0" } }
        });

        var response = await SendAndReceive(initMsg);

        var result = response.GetProperty("result");
        Assert.Equal(expectedVersion, result.GetProperty("protocolVersion").GetString());
        Assert.Equal("hotreload-sentinel", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Initialize_UnknownVersion_FallsBackToDefault()
    {
        var initMsg = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "1999-01-01", capabilities = new { }, clientInfo = new { name = "test", version = "1.0" } }
        });

        var response = await SendAndReceive(initMsg);

        var result = response.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Initialize_NoVersionProvided_UsesDefault()
    {
        var initMsg = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { capabilities = new { } }
        });

        var response = await SendAndReceive(initMsg);

        var result = response.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task ToolsList_ReturnsTools()
    {
        // Send initialize first, then tools/list
        var init = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2024-11-05", capabilities = new { } }
        });
        var toolsList = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 2, method = "tools/list",
            @params = new { }
        });

        var input = Encoding.UTF8.GetBytes(init + "\n" + toolsList + "\n");
        var inputStream = new MemoryStream(input);
        var outputStream = new MemoryStream();

        var statePath = Path.Combine(Path.GetTempPath(), $"test-mcp-{Guid.NewGuid()}.json");
        try
        {
            var store = new VerdictStore(statePath);
            var server = new McpServer("test", store);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await server.RunAsync(inputStream, outputStream, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (File.Exists(statePath)) File.Delete(statePath);
        }

        var responses = ParseJsonlResponses(outputStream.ToArray());
        Assert.True(responses.Count >= 2);

        var toolsResponse = responses[1];
        var tools = toolsResponse.GetProperty("result").GetProperty("tools");
        Assert.True(tools.GetArrayLength() > 0);
    }

    [Fact]
    public async Task UnknownMethod_ReturnsError()
    {
        var msg = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "unknown/method",
            @params = new { }
        });

        var response = await SendAndReceive(msg);

        Assert.True(response.TryGetProperty("error", out var error));
        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task WatchStartTool_WithInvalidExecutable_ReturnsErrorResponse()
    {
        var init = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2024-11-05", capabilities = new { } }
        });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 2, method = "tools/call",
            @params = new { name = "hr_watch_start", arguments = new { } }
        });

        var input = new MemoryStream(Encoding.UTF8.GetBytes(init + "\n" + call + "\n"));
        var output = new MemoryStream();
        var statePath = Path.Combine(Path.GetTempPath(), $"test-mcp-{Guid.NewGuid()}.json");
        try
        {
            var store = new VerdictStore(statePath);
            var server = new McpServer("definitely-not-a-real-executable", store);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await server.RunAsync(input, output, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (File.Exists(statePath)) File.Delete(statePath);
        }

        var responses = ParseJsonlResponses(output.ToArray());
        Assert.True(responses.Count >= 2);
        var toolCallResponse = responses[1];
        Assert.True(toolCallResponse.TryGetProperty("error", out var err));
        Assert.Equal(-32000, err.GetProperty("code").GetInt32());
    }

    static async Task<JsonElement> SendAndReceive(string jsonlMessage)
    {
        var input = new MemoryStream(Encoding.UTF8.GetBytes(jsonlMessage + "\n"));
        var output = new MemoryStream();

        var statePath = Path.Combine(Path.GetTempPath(), $"test-mcp-{Guid.NewGuid()}.json");
        try
        {
            var store = new VerdictStore(statePath);
            var server = new McpServer("test", store);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await server.RunAsync(input, output, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (File.Exists(statePath)) File.Delete(statePath);
        }

        var responses = ParseJsonlResponses(output.ToArray());
        Assert.NotEmpty(responses);
        return responses[0];
    }

    static List<JsonElement> ParseJsonlResponses(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var results = new List<JsonElement>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('{'))
                results.Add(JsonSerializer.Deserialize<JsonElement>(trimmed));
        }
        return results;
    }
}
