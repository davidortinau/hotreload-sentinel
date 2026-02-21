namespace HotReloadSentinel.Monitoring;

using HotReloadSentinel.Parsing;
using HotReloadSentinel.Verdicts;

/// <summary>
/// Background watch loop that polls Session.log and heartbeat endpoints.
/// </summary>
public sealed class WatchLoop
{
    readonly string _sessionLogPath;
    readonly string _portGlob;
    readonly string _hotReloadDir;
    readonly VerdictStore _store;
    readonly SessionLogParser _parser = new();

    public WatchLoop(string sessionLogPath, string portGlob, string hotReloadDir, VerdictStore store)
    {
        _sessionLogPath = sessionLogPath;
        _portGlob = portGlob;
        _hotReloadDir = hotReloadDir;
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
                        break; // Use first reachable endpoint
                    }
                    else
                    {
                        state.LastPollError = result.Error;
                    }
                }

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
}
