[assembly: System.Reflection.Metadata.MetadataUpdateHandler(
    typeof(HotReloadSentinel.Diagnostics.HotReloadSentinelUpdateHandler))]

namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Extension methods for integrating hot reload diagnostics into a MAUI app.
/// </summary>
public static class HotReloadDiagnosticsExtensions
{
#pragma warning disable CS0649 // Assigned in #if DEBUG block
    static DiagnosticsEndpointMiddleware? _endpoint;
    static PortFileWriter? _portFile;
#pragma warning restore CS0649

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
/// Public so the .NET hot reload runtime can reflect onto it under any
/// trimming / visibility policy.
/// </summary>
public static class HotReloadSentinelUpdateHandler
{
    public static void ClearCache(Type[]? updatedTypes)
    {
        System.Diagnostics.Debug.WriteLine($"[HotReloadSentinel] ClearCache invoked (types: {updatedTypes?.Length ?? 0})");
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        System.Diagnostics.Debug.WriteLine($"[HotReloadSentinel] UpdateApplication invoked (types: {updatedTypes?.Length ?? 0})");
        MetadataUpdateCounter.Increment();
    }
}
