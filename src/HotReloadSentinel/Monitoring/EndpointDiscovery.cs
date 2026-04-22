namespace HotReloadSentinel.Monitoring;

using System.Text.Json;

/// <summary>
/// Discovers hot reload diagnostic endpoints from port files.
/// </summary>
public static class EndpointDiscovery
{
    /// <summary>
    /// Scans the directory from <paramref name="portGlob"/> plus all known
    /// sandbox container temp dirs on macOS (Mac Catalyst / iOS sim apps
    /// write their port file inside their sandbox, not in host <c>/tmp</c>).
    /// </summary>
    public static List<EndpointInfo> Discover(string portGlob)
    {
        var results = new List<EndpointInfo>();
        var pattern = Path.GetFileName(portGlob);
        var primaryDir = Path.GetDirectoryName(portGlob) ?? "/tmp";

        foreach (var dir in EnumerateScanDirectories(primaryDir))
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.GetFiles(dir, pattern); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var file in files)
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
        }

        return results;
    }

    /// <summary>
    /// Upper bound on directories scanned per Discover() call. A typical
    /// developer machine has ~10 Mac Catalyst apps + ~5 sim devices × ~10 apps
    /// each, which fits well under this cap. The cap exists so a user with
    /// hundreds of container apps or sim devices doesn't pay unbounded I/O
    /// every 2-second poll.
    /// </summary>
    const int MaxScanDirectories = 128;

    /// <summary>
    /// Builds the list of directories to scan. On macOS this includes the
    /// per-app sandbox temp dirs for Mac Catalyst / iOS Simulator apps, since
    /// those apps' <see cref="Path.GetTempPath"/> resolves to their container
    /// rather than host <c>/tmp</c>.
    /// </summary>
    static IEnumerable<string> EnumerateScanDirectories(string primaryDir)
    {
        int yielded = 0;
        yield return primaryDir;
        yielded++;

        if (!OperatingSystem.IsMacOS()) yield break;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) yield break;

        // Mac Catalyst / macOS unsigned apps use sandboxed container temp.
        var containersRoot = Path.Combine(home, "Library", "Containers");
        if (Directory.Exists(containersRoot))
        {
            string[] bundleDirs;
            try { bundleDirs = Directory.GetDirectories(containersRoot); }
            catch (UnauthorizedAccessException) { bundleDirs = Array.Empty<string>(); }
            catch (IOException) { bundleDirs = Array.Empty<string>(); }

            foreach (var bundleDir in bundleDirs)
            {
                if (yielded >= MaxScanDirectories) yield break;
                yield return Path.Combine(bundleDir, "Data", "tmp");
                yielded++;
            }
        }

        // iOS Simulator app tmp dirs live under CoreSimulator/Devices.
        var simDevicesRoot = Path.Combine(home, "Library", "Developer", "CoreSimulator", "Devices");
        if (Directory.Exists(simDevicesRoot))
        {
            string[] deviceDirs;
            try { deviceDirs = Directory.GetDirectories(simDevicesRoot); }
            catch (UnauthorizedAccessException) { deviceDirs = Array.Empty<string>(); }
            catch (IOException) { deviceDirs = Array.Empty<string>(); }

            foreach (var deviceDir in deviceDirs)
            {
                if (yielded >= MaxScanDirectories) yield break;
                var appsRoot = Path.Combine(deviceDir, "data", "Containers", "Data", "Application");
                if (!Directory.Exists(appsRoot)) continue;

                string[] appDirs;
                try { appDirs = Directory.GetDirectories(appsRoot); }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (var appDir in appDirs)
                {
                    if (yielded >= MaxScanDirectories) yield break;
                    yield return Path.Combine(appDir, "tmp");
                    yielded++;
                }
            }
        }
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

