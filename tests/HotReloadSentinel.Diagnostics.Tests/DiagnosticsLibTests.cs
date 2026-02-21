namespace HotReloadSentinel.Diagnostics.Tests;

using System.Net.Http;
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
}

public class PortFileWriterTests
{
    [Fact]
    public void WritesAndCleansUpPortFile()
    {
        using var writer = new PortFileWriter(12345);
        Assert.True(File.Exists(writer.FilePath));
        Assert.Equal("12345", File.ReadAllText(writer.FilePath).Trim());

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
    }

    [Fact]
    public async Task PortFileIsDiscoverable()
    {
        using var endpoint = new DiagnosticsEndpointMiddleware();
        using var portFile = new PortFileWriter(endpoint.Port);

        // Simulate what the sentinel does: glob for port files
        var tmpDir = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        var portFiles = Directory.GetFiles(tmpDir, "hotreload-diag-*.port");
        Assert.Contains(portFiles, f => File.ReadAllText(f).Trim() == endpoint.Port.ToString());

        // Verify the heartbeat is reachable via discovered port
        var port = int.Parse(File.ReadAllText(portFile.FilePath).Trim());
        using var http = new HttpClient();
        var response = await http.GetStringAsync($"http://127.0.0.1:{port}/heartbeat");
        Assert.Contains("\"pid\"", response);
    }
}
