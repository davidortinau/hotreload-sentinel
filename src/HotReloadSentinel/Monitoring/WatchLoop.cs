namespace HotReloadSentinel.Monitoring;

using System.Net.Http;
using System.Text;
using HotReloadSentinel.Parsing;
using HotReloadSentinel.Verdicts;

/// <summary>
/// Background watch loop that polls Session.log and heartbeat endpoints.
/// </summary>
public sealed class WatchLoop
{
    static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>How long to wait after a successful-apply log line before
    /// declaring an apply 'stuck' if the app-side counter hasn't advanced.</summary>
    static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(10);

    readonly string _sessionLogPath;
    readonly string _portGlob;
    readonly VerdictStore _store;
    readonly SessionLogParser _parser = new();

    int _lastObservedResultSuccessCount;
    int _lastPostedAppliedResultCount;
    readonly List<PendingApplyCheck> _pendingChecks = new();
    int _lastReportedFailureForResultCount;

    public WatchLoop(string sessionLogPath, string portGlob, VerdictStore store)
    {
        _sessionLogPath = sessionLogPath;
        _portGlob = portGlob;
        _store = store;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Start from current end of log
        if (File.Exists(_sessionLogPath))
        {
            try { _parser.Offset = new FileInfo(_sessionLogPath).Length; }
            catch (IOException) { _parser.Offset = 0; }
        }

        // Preserve existing verdicts
        var state = _store.Read();
        var existingVerdicts = state.Verdicts.ToList();

        state.WatcherAlive = true;
        state.WatcherPid = Environment.ProcessId;
        state.StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        state.Verdicts = existingVerdicts;
        _store.Write(state);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                state = _store.Read(); // Re-read to pick up verdicts from other processes
                state.WatcherAlive = true;
                state.WatcherPid = Environment.ProcessId;

                // Parse log
                var markers = _parser.Parse(_sessionLogPath);
                if (markers.HasActivity)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    state.LastLogActivityTs = now;
                    state.SaveCount += markers.SaveCount;
                    state.ApplyCount += markers.ApplyCount;
                    state.XamlCodeBehindChangeCount += markers.XamlCodeBehindChangeCount;
                    state.XamlChangeCount += markers.XamlChangeCount;
                    state.XamlApplyCount += markers.XamlApplyCount;
                    state.Enc1008Count += markers.Enc1008Count;
                    state.ResultSuccessCount += markers.ResultSuccessCount;
                    state.ResultFailureCount += markers.ResultFailureCount;
                    state.NotAppliedCount += markers.NotAppliedCount;
                    state.NotAppliedOtherTfmCount += markers.NotAppliedOtherTfmCount;
                    state.ConnectionLostCount += markers.ConnectionLostCount;
                    if (markers.LastSolutionUpdate is not null)
                        state.LastSolutionUpdate = markers.LastSolutionUpdate;
                }

                // Poll heartbeat endpoints
                var endpoints = EndpointDiscovery.Discover(_portGlob);
                state.Endpoints = endpoints.Select(e => e.Url).ToList();
                state.HeartbeatOk = false;
                state.LastPollError = null;

                HeartbeatResult? selectedResult = null;
                EndpointInfo? selectedEndpoint = null;

                foreach (var ep in endpoints)
                {
                    var result = await HeartbeatPoller.PollAsync(ep.Url);
                    if (result.Ok)
                    {
                        state.HeartbeatOk = true;
                        state.LastHeartbeatTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                        state.SelectedEndpoint = ep.Url;
                        state.SelectedPid = result.Pid;
                        state.LastHeartbeatUpdateCount = result.UpdateCount;
                        state.LastHeartbeatUpdateTs = result.LastUpdateTimestamp;
                        selectedResult = result;
                        selectedEndpoint = ep;
                        break; // Use first reachable endpoint
                    }
                    else
                    {
                        state.LastPollError = result.Error;
                    }
                }

                // Failure detection: if log shows a new successful apply but
                // the app-side counter hasn't advanced within FailureWindow,
                // POST /failed so the in-app overlay can flip red.
                await CheckForStuckAppliesAsync(state, selectedResult, selectedEndpoint, ct);

                // Notify the app immediately on every newly-observed successful
                // apply so the overlay flashes green without depending on the
                // in-process MetadataUpdateHandler (which is not reliably called
                // on all platforms — notably Mac Catalyst in sandbox mode).
                // We key off _lastPostedAppliedResultCount which is updated
                // AFTER CheckForStuckApplies reads _lastObservedResultSuccessCount.
                await NotifyAppliedAsync(state, selectedEndpoint, ct);

                state.Status = ComputeStatus(state);
                _store.Write(state);
            }
            catch (Exception)
            {
                // Don't crash the watcher on transient errors
            }

            await Task.Delay(2000, ct);
        }
    }

    public static string ComputeStatus(SentinelState state)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        bool recentHeartbeat = state.LastHeartbeatTs.HasValue && (now - state.LastHeartbeatTs.Value) < 10;
        bool recentLog = state.LastLogActivityTs.HasValue && (now - state.LastLogActivityTs.Value) < 20;

        if (!state.WatcherAlive)
            return "IDLE";
        if (recentHeartbeat && recentLog)
            return "ACTIVE";
        if (recentHeartbeat || recentLog)
            return "DEGRADED";
        if (state.HeartbeatOk)
            return "DEGRADED";
        return "IDLE";
    }

    async Task CheckForStuckAppliesAsync(
        SentinelState state,
        HeartbeatResult? heartbeat,
        EndpointInfo? endpoint,
        CancellationToken ct)
    {
        // 1) Enqueue a single check for any newly-observed successful apply(s).
        // We can't reliably split a burst into per-apply checks because the
        // heartbeat baseline may already include some of the burst's applies
        // (we sample heartbeat asynchronously from the log). Instead we ask:
        // did the app-side counter advance AT ALL within FailureWindow after
        // we saw at least one success in the log? That's enough to flip red.
        if (state.ResultSuccessCount > _lastObservedResultSuccessCount && heartbeat?.UpdateCount is int baseline)
        {
            _pendingChecks.Add(new PendingApplyCheck(
                ResultCount: state.ResultSuccessCount,
                HeartbeatBaseline: baseline + 1,
                Deadline: DateTime.UtcNow + FailureWindow));
            _lastObservedResultSuccessCount = state.ResultSuccessCount;
        }
        else if (state.ResultSuccessCount > _lastObservedResultSuccessCount)
        {
            // No heartbeat yet — we can't measure baseline; skip these.
            _lastObservedResultSuccessCount = state.ResultSuccessCount;
        }

        if (_pendingChecks.Count == 0) return;

        // 2) Resolve any expired checks.
        var now = DateTime.UtcNow;
        var stillPending = new List<PendingApplyCheck>();
        var stuck = new List<PendingApplyCheck>();
        foreach (var check in _pendingChecks)
        {
            if (heartbeat?.UpdateCount is int current && current >= check.HeartbeatBaseline)
            {
                // App-side caught up — this apply was OK.
                continue;
            }
            if (now >= check.Deadline)
            {
                stuck.Add(check);
            }
            else
            {
                stillPending.Add(check);
            }
        }
        _pendingChecks.Clear();
        _pendingChecks.AddRange(stillPending);

        if (stuck.Count == 0 || endpoint is null) return;

        // 3) POST /failed once per stuck apply, but never more than one per
        // result-count tick (avoid spamming on flaky polls).
        foreach (var s in stuck)
        {
            if (s.ResultCount <= _lastReportedFailureForResultCount) continue;
            _lastReportedFailureForResultCount = s.ResultCount;

            try
            {
                var url = endpoint.Url.TrimEnd('/') + "/failed";
                var json = $"{{\"reason\":\"applied but no app-side tick\",\"applySequence\":{s.ResultCount}}}";
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await s_http.PostAsync(url, content, ct);
                _ = resp; // ignore response
            }
            catch
            {
                // Best effort — overlay will simply not flip red this time.
            }
        }
    }

    readonly record struct PendingApplyCheck(int ResultCount, int HeartbeatBaseline, DateTime Deadline);

    async Task NotifyAppliedAsync(SentinelState state, EndpointInfo? endpoint, CancellationToken ct)
    {
        if (endpoint is null) return;
        if (state.ResultSuccessCount <= _lastPostedAppliedResultCount) return;
        _lastPostedAppliedResultCount = state.ResultSuccessCount;
        try
        {
            var url = endpoint.Url.TrimEnd('/') + "/applied";
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await s_http.PostAsync(url, content, ct);
            _ = resp;
        }
        catch
        {
            // Best effort — overlay will simply not flash green this time.
        }
    }
}
