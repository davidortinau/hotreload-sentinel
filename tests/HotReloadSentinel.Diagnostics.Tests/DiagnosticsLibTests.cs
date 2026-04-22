namespace HotReloadSentinel.Diagnostics.Tests;

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public class MetadataUpdateCounterTests
{
    [Fact]
    public void Increment_AdvancesCount()
    {
        MetadataUpdateCounter.Reset();
        Assert.Equal(0, MetadataUpdateCounter.UpdateCount);

        MetadataUpdateCounter.Increment();
        Assert.Equal(1, MetadataUpdateCounter.UpdateCount);

        MetadataUpdateCounter.Increment();
        Assert.Equal(2, MetadataUpdateCounter.UpdateCount);
        Assert.True(MetadataUpdateCounter.LastUpdateUtc > DateTime.MinValue);
    }

    [Fact]
    public void Reset_ClearsCount()
    {
        MetadataUpdateCounter.Increment();
        MetadataUpdateCounter.Reset();
        Assert.Equal(0, MetadataUpdateCounter.UpdateCount);
    }

    [Fact]
    public void Applied_RaisesOnIncrement_WithMonotonicSequence()
    {
        MetadataUpdateCounter.Reset();
        var observed = new List<long>();
        EventHandler<HotReloadAppliedEventArgs> handler = (_, e) => observed.Add(e.ApplySequence);
        MetadataUpdateCounter.Applied += handler;
        try
        {
            MetadataUpdateCounter.Increment();
            MetadataUpdateCounter.Increment();
        }
        finally { MetadataUpdateCounter.Applied -= handler; }

        Assert.Equal(new long[] { 1, 2 }, observed);
    }

    [Fact]
    public void ReportFailure_RaisesAndAdvancesSequence()
    {
        MetadataUpdateCounter.Reset();
        HotReloadFailedEventArgs? captured = null;
        EventHandler<HotReloadFailedEventArgs> handler = (_, e) => captured = e;
        MetadataUpdateCounter.Failed += handler;
        try
        {
            MetadataUpdateCounter.Increment();
            MetadataUpdateCounter.ReportFailure("test reason");
        }
        finally { MetadataUpdateCounter.Failed -= handler; }

        Assert.NotNull(captured);
        Assert.Equal("test reason", captured!.Reason);
        Assert.Equal(2, captured.ApplySequence);
        Assert.Equal("test reason", MetadataUpdateCounter.LastFailureReason);
    }
}

public class PortFileWriterTests
{
    [Fact]
    public void WritesJsonAndCleansUpPortFile()
    {
        using var writer = new PortFileWriter(12345);
        Assert.True(File.Exists(writer.FilePath));

        var content = File.ReadAllText(writer.FilePath).Trim();
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(12345, doc.RootElement.GetProperty("port").GetInt32());
        Assert.Equal(Environment.ProcessId, doc.RootElement.GetProperty("pid").GetInt32());
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("assembly").ValueKind);

        writer.Dispose();
        Assert.False(File.Exists(writer.FilePath));
    }
}

public class DiagnosticsEndpointTests
{
    [Fact]
    public async Task HeartbeatReturnsJson()
    {
        MetadataUpdateCounter.Reset();
        MetadataUpdateCounter.Increment();

        using var endpoint = new DiagnosticsEndpointMiddleware();
        using var http = new HttpClient();

        var response = await http.GetStringAsync($"http://127.0.0.1:{endpoint.Port}/heartbeat");
        Assert.Contains("\"pid\"", response);
        Assert.Contains("\"updateCount\":1", response);
        Assert.Contains("\"applySequence\":", response);
        Assert.Contains("\"failedCount\":0", response);
    }

    [Fact]
    public async Task PortFileIsDiscoverable()
    {
        using var endpoint = new DiagnosticsEndpointMiddleware();
        using var portFile = new PortFileWriter(endpoint.Port);

        // Read JSON port file
        var json = File.ReadAllText(portFile.FilePath).Trim();
        using var doc = JsonDocument.Parse(json);
        var port = doc.RootElement.GetProperty("port").GetInt32();
        Assert.Equal(endpoint.Port, port);

        using var http = new HttpClient();
        var response = await http.GetStringAsync($"http://127.0.0.1:{port}/heartbeat");
        Assert.Contains("\"pid\"", response);
    }

    [Fact]
    public async Task FailedPostIncrementsFailedCounterAndExposesReason()
    {
        MetadataUpdateCounter.Reset();
        using var endpoint = new DiagnosticsEndpointMiddleware();
        using var http = new HttpClient();

        var response = await http.PostAsJsonAsync(
            $"http://127.0.0.1:{endpoint.Port}/failed",
            new { reason = "applied but no app-side tick", applySequence = 1 });

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);

        var heartbeat = await http.GetStringAsync($"http://127.0.0.1:{endpoint.Port}/heartbeat");
        Assert.Contains("\"failedCount\":1", heartbeat);
        Assert.Contains("applied but no app-side tick", heartbeat);
    }
}

