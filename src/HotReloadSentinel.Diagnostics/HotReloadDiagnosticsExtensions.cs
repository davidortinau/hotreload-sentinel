[assembly: System.Reflection.Metadata.MetadataUpdateHandler(
    typeof(HotReloadSentinel.Diagnostics.HotReloadSentinelUpdateHandler))]

namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Extension methods for integrating hot reload diagnostics into a MAUI app.
/// </summary>
public static class HotReloadDiagnosticsExtensions
{
    static DiagnosticsEndpointMiddleware? _endpoint;
    static PortFileWriter? _portFile;

    /// <summary>
    /// Enables hot reload diagnostics: heartbeat endpoint, update counter, and port file.
    /// Call in MauiProgram.cs: builder.UseHotReloadDiagnostics();
    /// </summary>
    public static void UseHotReloadDiagnostics(this object builder)
    {
        if (_endpoint is not null) return; // Already initialized

#if DEBUG
        _endpoint = new DiagnosticsEndpointMiddleware();
        _portFile = new PortFileWriter(_endpoint.Port);

        // Register cleanup on process exit
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _portFile?.Dispose();
            _endpoint?.Dispose();
        };
#endif
    }

    /// <summary>Get the diagnostic endpoint port, or null if not initialized.</summary>
    public static int? DiagnosticsPort => _endpoint?.Port;
}

/// <summary>
/// MetadataUpdateHandler that auto-increments the update counter.
/// Registered via assembly attribute — no manual wiring needed.
/// </summary>
internal static class HotReloadSentinelUpdateHandler
{
    public static void ClearCache(Type[]? updatedTypes) { }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        MetadataUpdateCounter.Increment();
    }
}
