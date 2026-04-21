namespace HotReloadSentinel.Diagnostics.Maui;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// Observable state for the hot reload overlay. Listens to
/// <see cref="MetadataUpdateCounter.Applied"/> and
/// <see cref="MetadataUpdateCounter.Failed"/> and projects the latest
/// per-window UI state.
///
/// Transitions are monotonic with respect to <see cref="MetadataUpdateCounter.ApplySequence"/>:
/// a Failed event with a sequence higher than the most-recently-observed
/// success will switch the overlay to red and stay there until the next
/// successful apply (which carries an even higher sequence).
/// </summary>
public sealed class HotReloadOverlayState : INotifyPropertyChanged
{
    public enum OverlayStatus { Idle, Applied, Failed }

    long _lastObservedSequence;
    OverlayStatus _status = OverlayStatus.Idle;
    int _appliedCount;
    int _failedCount;
    string? _failureReason;
    DateTime _statusAtUtc;

    public HotReloadOverlayState()
    {
        MetadataUpdateCounter.Applied += OnApplied;
        MetadataUpdateCounter.Failed += OnFailed;
    }

    public OverlayStatus Status
    {
        get => _status;
        private set { if (_status != value) { _status = value; Raise(); } }
    }

    public int AppliedCount
    {
        get => _appliedCount;
        private set { if (_appliedCount != value) { _appliedCount = value; Raise(); } }
    }

    public int FailedCount
    {
        get => _failedCount;
        private set { if (_failedCount != value) { _failedCount = value; Raise(); } }
    }

    public string? FailureReason
    {
        get => _failureReason;
        private set { if (_failureReason != value) { _failureReason = value; Raise(); } }
    }

    public DateTime StatusAtUtc
    {
        get => _statusAtUtc;
        private set { if (_statusAtUtc != value) { _statusAtUtc = value; Raise(); } }
    }

    void OnApplied(object? sender, HotReloadAppliedEventArgs e)
    {
        // Atomic monotonic guard: only advance if our sequence is the newest.
        // Concurrent OnApplied/OnFailed can race from arbitrary runtime threads.
        long observed;
        do
        {
            observed = Interlocked.Read(ref _lastObservedSequence);
            if (e.ApplySequence < observed) return;
        }
        while (Interlocked.CompareExchange(ref _lastObservedSequence, e.ApplySequence, observed) != observed);

        AppliedCount = e.UpdateCount;
        StatusAtUtc = e.TimestampUtc;
        // A successful apply always clears prior failure state.
        FailureReason = null;
        Status = OverlayStatus.Applied;
    }

    void OnFailed(object? sender, HotReloadFailedEventArgs e)
    {
        long observed;
        do
        {
            observed = Interlocked.Read(ref _lastObservedSequence);
            if (e.ApplySequence < observed) return;
        }
        while (Interlocked.CompareExchange(ref _lastObservedSequence, e.ApplySequence, observed) != observed);

        FailedCount = e.FailedCount;
        FailureReason = e.Reason;
        StatusAtUtc = e.TimestampUtc;
        Status = OverlayStatus.Failed;
    }

    /// <summary>Caller invokes after a transient Applied state has been
    /// shown long enough to fade. Returns to Idle only if no failure has
    /// occurred since.</summary>
    public void DismissApplied(long observedAtSequence)
    {
        if (Status == OverlayStatus.Applied && observedAtSequence == Interlocked.Read(ref _lastObservedSequence))
        {
            Status = OverlayStatus.Idle;
        }
    }

    /// <summary>Test-only: detach handlers (also useful when an overlay
    /// host shuts down).</summary>
    public void Detach()
    {
        MetadataUpdateCounter.Applied -= OnApplied;
        MetadataUpdateCounter.Failed -= OnFailed;
    }

    public long ObservedSequence => Interlocked.Read(ref _lastObservedSequence);

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
}
