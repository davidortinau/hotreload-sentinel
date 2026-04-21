namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Tracks hot reload update count. Auto-incremented by MetadataUpdateHandler.
/// </summary>
public static class MetadataUpdateCounter
{
    static int _updateCount;
    static int _failedCount;
    static long _applySequence;
    static DateTime _lastUpdateUtc = DateTime.MinValue;
    static DateTime _lastFailureUtc = DateTime.MinValue;
    static string? _lastFailureReason;

    /// <summary>Current cumulative update count.</summary>
    public static int UpdateCount => _updateCount;

    /// <summary>Cumulative count of CLI-reported failures.</summary>
    public static int FailedCount => _failedCount;

    /// <summary>
    /// Monotonic sequence that ticks for both successful applies and CLI-reported
    /// failures. Subscribers can compare sequence numbers to enforce monotonic
    /// state transitions and avoid races between delayed timers.
    /// </summary>
    public static long ApplySequence => Interlocked.Read(ref _applySequence);

    /// <summary>UTC timestamp of last update.</summary>
    public static DateTime LastUpdateUtc => _lastUpdateUtc;

    /// <summary>UTC timestamp of last CLI-reported failure.</summary>
    public static DateTime LastFailureUtc => _lastFailureUtc;

    /// <summary>Most recent CLI-reported failure reason, if any.</summary>
    public static string? LastFailureReason => _lastFailureReason;

    /// <summary>
    /// Raised after every successful apply. Sender is null. Subscribers are
    /// invoked synchronously on the runtime thread that delivered the
    /// MetadataUpdateHandler callback — UI-bound consumers must marshal to
    /// the main thread themselves.
    /// </summary>
    public static event EventHandler<HotReloadAppliedEventArgs>? Applied;

    /// <summary>
    /// Raised when the CLI sentinel reports that a hot reload apply failed
    /// (typically because the IDE log shows success but no <see cref="Applied"/>
    /// event followed within the expected window).
    /// </summary>
    public static event EventHandler<HotReloadFailedEventArgs>? Failed;

    /// <summary>
    /// Called by the MetadataUpdateHandler when hot reload applies changes.
    /// </summary>
    public static void Increment()
    {
        Interlocked.Increment(ref _updateCount);
        var seq = Interlocked.Increment(ref _applySequence);
        _lastUpdateUtc = DateTime.UtcNow;
        try { Applied?.Invoke(null, new HotReloadAppliedEventArgs(seq, _updateCount, _lastUpdateUtc)); }
        catch { /* never propagate handler exceptions back into the runtime */ }
    }

    /// <summary>
    /// Called by the diagnostics middleware when a CLI sentinel POSTs to /failed.
    /// </summary>
    public static void ReportFailure(string? reason)
    {
        Interlocked.Increment(ref _failedCount);
        var seq = Interlocked.Increment(ref _applySequence);
        _lastFailureUtc = DateTime.UtcNow;
        _lastFailureReason = reason;
        try { Failed?.Invoke(null, new HotReloadFailedEventArgs(seq, _failedCount, _lastFailureUtc, reason)); }
        catch { }
    }

    /// <summary>Reset counter (useful for testing).</summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _updateCount, 0);
        Interlocked.Exchange(ref _failedCount, 0);
        Interlocked.Exchange(ref _applySequence, 0);
        _lastUpdateUtc = DateTime.MinValue;
        _lastFailureUtc = DateTime.MinValue;
        _lastFailureReason = null;
    }
}

public sealed class HotReloadAppliedEventArgs : EventArgs
{
    public long ApplySequence { get; }
    public int UpdateCount { get; }
    public DateTime TimestampUtc { get; }

    public HotReloadAppliedEventArgs(long applySequence, int updateCount, DateTime timestampUtc)
    {
        ApplySequence = applySequence;
        UpdateCount = updateCount;
        TimestampUtc = timestampUtc;
    }
}

public sealed class HotReloadFailedEventArgs : EventArgs
{
    public long ApplySequence { get; }
    public int FailedCount { get; }
    public DateTime TimestampUtc { get; }
    public string? Reason { get; }

    public HotReloadFailedEventArgs(long applySequence, int failedCount, DateTime timestampUtc, string? reason)
    {
        ApplySequence = applySequence;
        FailedCount = failedCount;
        TimestampUtc = timestampUtc;
        Reason = reason;
    }
}

