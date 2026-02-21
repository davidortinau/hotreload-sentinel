namespace HotReloadSentinel.Monitoring;

using System.Text.Json;

/// <summary>
/// Discovers hot reload diagnostic endpoints from port files.
/// </summary>
public static class EndpointDiscovery
{
    public static List<EndpointInfo> Discover(string portGlob)
    {
        var results = new List<EndpointInfo>();
        var dir = Path.GetDirectoryName(portGlob) ?? "/tmp";
        var pattern = Path.GetFileName(portGlob);

        if (!Directory.Exists(dir))
            return results;

        foreach (var file in Directory.GetFiles(dir, pattern))
        {
            try
            {
                var content = File.ReadAllText(file).Trim();
                if (int.TryParse(content, out var port))
                {
                    results.Add(new EndpointInfo
                    {
                        Port = port,
                        FilePath = file,
                        Url = $"http://127.0.0.1:{port}"
                    });
                }
            }
            catch (IOException)
            {
                // File may be locked or deleted
            }
        }

        return results;
    }
}

public sealed class EndpointInfo
{
    public int Port { get; set; }
    public string FilePath { get; set; } = "";
    public string Url { get; set; } = "";
}
