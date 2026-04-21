namespace HotReloadSentinel.Diagnostics;

using System.Reflection;
using System.Text.Json;

/// <summary>
/// Writes a port file so the sentinel CLI can discover the diagnostic endpoint.
/// File: {tmpDir}/hotreload-diag-{pid}.port containing JSON with port + identity.
/// Cleans up on dispose. The CLI accepts both the new JSON shape and the legacy
/// bare-int shape for backward compatibility.
/// </summary>
public sealed class PortFileWriter : IDisposable
{
    readonly string _filePath;
    bool _disposed;

    public PortFileWriter(int port)
    {
        var tmpDir = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        var pid = Environment.ProcessId;
        _filePath = Path.Combine(tmpDir, $"hotreload-diag-{pid}.port");

        var assembly = SafeAssemblyName();
        var payload = JsonSerializer.Serialize(new { port, pid, assembly });

        try
        {
            File.WriteAllText(_filePath, payload);
        }
        catch (IOException)
        {
            // Best effort — may fail in sandboxed environments
        }
    }

    public string FilePath => _filePath;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
    }

    static string SafeAssemblyName()
    {
        try { return Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown"; }
        catch { return "unknown"; }
    }
}

