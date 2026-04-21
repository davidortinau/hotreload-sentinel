namespace HotReloadSentinel.Tests;

using HotReloadSentinel.Monitoring;
using Xunit;

public class EndpointDiscoveryTests : IDisposable
{
    readonly string _dir;

    public EndpointDiscoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hr-endpoint-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Parses_LegacyBareIntPortFile()
    {
        var path = Path.Combine(_dir, "hotreload-diag-1234.port");
        File.WriteAllText(path, "55555");

        var results = EndpointDiscovery.Discover(Path.Combine(_dir, "hotreload-diag-*.port"));
        var info = Assert.Single(results);
        Assert.Equal(55555, info.Port);
        Assert.Null(info.Pid);
        Assert.Null(info.Assembly);
        Assert.Equal("http://127.0.0.1:55555", info.Url);
    }

    [Fact]
    public void Parses_NewJsonPortFile_WithIdentity()
    {
        var path = Path.Combine(_dir, "hotreload-diag-9999.port");
        File.WriteAllText(path, "{\"port\":44321,\"pid\":9999,\"assembly\":\"BaristaNotes\"}");

        var results = EndpointDiscovery.Discover(Path.Combine(_dir, "hotreload-diag-*.port"));
        var info = Assert.Single(results);
        Assert.Equal(44321, info.Port);
        Assert.Equal(9999, info.Pid);
        Assert.Equal("BaristaNotes", info.Assembly);
    }

    [Fact]
    public void Skips_MalformedPortFile()
    {
        File.WriteAllText(Path.Combine(_dir, "hotreload-diag-1.port"), "not-json-not-int");
        File.WriteAllText(Path.Combine(_dir, "hotreload-diag-2.port"), "{\"missing\":\"port\"}");

        var results = EndpointDiscovery.Discover(Path.Combine(_dir, "hotreload-diag-*.port"));
        Assert.Empty(results);
    }
}
