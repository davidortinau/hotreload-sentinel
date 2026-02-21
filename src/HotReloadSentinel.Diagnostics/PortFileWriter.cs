namespace HotReloadSentinel.Diagnostics;

using System.Diagnostics;

/// <summary>
/// Writes a port file so the sentinel CLI can discover the diagnostic endpoint.
/// File: {tmpDir}/hotreload-diag-{pid}.port containing the port number.
/// Cleans up on dispose.
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

        try
        {
            File.WriteAllText(_filePath, port.ToString());
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
}
