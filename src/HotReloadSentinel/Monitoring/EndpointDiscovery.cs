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
                var info = TryParse(content, file);
                if (info is not null)
                    results.Add(info);
            }
            catch (IOException)
            {
                // File may be locked or deleted
            }
        }

        return results;
    }

    /// <summary>
    /// Accepts both the new JSON shape (<c>{ "port": N, "pid": M, "assembly": "X" }</c>)
    /// and the legacy bare-int shape for backward compatibility.
    /// </summary>
    static EndpointInfo? TryParse(string content, string filePath)
    {
        if (string.IsNullOrEmpty(content)) return null;

        // Legacy bare-int port file.
        if (int.TryParse(content, out var bareInt))
        {
            return new EndpointInfo
            {
                Port = bareInt,
                FilePath = filePath,
                Url = $"http://127.0.0.1:{bareInt}",
            };
        }

        // New JSON shape.
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("port", out var portEl) || !portEl.TryGetInt32(out var port))
                return null;

            int? pid = root.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out var p) ? p : null;
            string? assembly = root.TryGetProperty("assembly", out var aEl) && aEl.ValueKind == JsonValueKind.String
                ? aEl.GetString() : null;

            return new EndpointInfo
            {
                Port = port,
                Pid = pid,
                Assembly = assembly,
                FilePath = filePath,
                Url = $"http://127.0.0.1:{port}",
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class EndpointInfo
{
    public int Port { get; set; }
    public int? Pid { get; set; }
    public string? Assembly { get; set; }
    public string FilePath { get; set; } = "";
    public string Url { get; set; } = "";
}

