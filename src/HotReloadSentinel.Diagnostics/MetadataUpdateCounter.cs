namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Tracks hot reload update count. Auto-incremented by MetadataUpdateHandler.
/// </summary>
public static class MetadataUpdateCounter
{
    static int _updateCount;
    static DateTime _lastUpdateUtc = DateTime.MinValue;

    /// <summary>Current cumulative update count.</summary>
    public static int UpdateCount => _updateCount;

    /// <summary>UTC timestamp of last update.</summary>
    public static DateTime LastUpdateUtc => _lastUpdateUtc;

    /// <summary>
    /// Called by the MetadataUpdateHandler when hot reload applies changes.
    /// </summary>
    public static void Increment()
    {
        Interlocked.Increment(ref _updateCount);
        _lastUpdateUtc = DateTime.UtcNow;
    }

    /// <summary>Reset counter (useful for testing).</summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _updateCount, 0);
        _lastUpdateUtc = DateTime.MinValue;
    }
}
