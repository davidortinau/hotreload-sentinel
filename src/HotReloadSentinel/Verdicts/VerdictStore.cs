namespace HotReloadSentinel.Verdicts;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Atomic read/write of sentinel state including verdicts.
/// </summary>
public sealed class VerdictStore
{
    readonly string _statePath;

    public VerdictStore(string statePath)
    {
        _statePath = statePath;
    }

    public SentinelState Read()
    {
        if (!File.Exists(_statePath))
            return new SentinelState();

        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<SentinelState>(json, JsonOpts) ?? new SentinelState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new SentinelState();
        }
    }

    public void Write(SentinelState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOpts);
        var tmp = _statePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _statePath, overwrite: true);
    }

    /// <summary>
    /// Append a verdict entry, preserving max 50 entries.
    /// </summary>
    public void AppendVerdict(VerdictEntry entry)
    {
        var state = Read();
        state.Verdicts.Add(entry);
        if (state.Verdicts.Count > 50)
            state.Verdicts.RemoveRange(0, state.Verdicts.Count - 50);
        Write(state);
    }

    /// <summary>
    /// Get unconfirmed verdict entries (no atom_verdicts recorded yet).
    /// </summary>
    public List<VerdictEntry> GetPending()
    {
        var state = Read();
        return state.Verdicts
            .Where(v => v.AtomVerdicts.Count == 0 && v.Atoms.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Record developer verdicts for a specific apply event.
    /// </summary>
    public (bool found, string verdict) RecordVerdict(int applyIndex, Dictionary<string, string> atomVerdicts)
    {
        var state = Read();
        var entry = state.Verdicts.FirstOrDefault(v => v.ApplyIndex == applyIndex);
        if (entry is null)
            return (false, "not_found");

        entry.AtomVerdicts = atomVerdicts;

        // Recompute overall verdict
        var values = atomVerdicts.Values.ToList();
        if (values.Count == 0)
            entry.Verdict = "skipped";
        else if (values.All(v => v == "yes"))
            entry.Verdict = "all_good";
        else if (values.All(v => v == "no"))
            entry.Verdict = "all_failed";
        else
            entry.Verdict = "mixed";

        Write(state);
        return (true, entry.Verdict);
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class SentinelState
{
    public bool WatcherAlive { get; set; }
    public int? WatcherPid { get; set; }
    public double? StartedAt { get; set; }
    public double? LastLogActivityTs { get; set; }
    public double? LastHeartbeatTs { get; set; }
    public bool HeartbeatOk { get; set; }
    public string? LastPollError { get; set; }
    public string? Status { get; set; }
    public List<string> Endpoints { get; set; } = [];
    public string? SelectedEndpoint { get; set; }
    public int? SelectedPid { get; set; }
    public int? LastHeartbeatUpdateCount { get; set; }
    public string? LastHeartbeatUpdateTs { get; set; }
    public long SessionLogOffset { get; set; }

    // Cumulative log counters
    public int SaveCount { get; set; }
    public int ApplyCount { get; set; }
    public int ResultSuccessCount { get; set; }
    public int ResultFailureCount { get; set; }
    public int Enc1008Count { get; set; }
    public int NotAppliedCount { get; set; }
    public int NotAppliedOtherTfmCount { get; set; }
    public int ConnectionLostCount { get; set; }
    public string? LastSolutionUpdate { get; set; }

    public List<VerdictEntry> Verdicts { get; set; } = [];
}

public sealed class VerdictEntry
{
    public int ApplyIndex { get; set; }
    public string ArtifactPair { get; set; } = "";
    public string? Verdict { get; set; }
    public List<AtomInfo> Atoms { get; set; } = [];
    public Dictionary<string, string> AtomVerdicts { get; set; } = [];
}

public sealed class AtomInfo
{
    public string Kind { get; set; } = "";
    public string ControlHint { get; set; } = "unknown";
    public string ChangeSummary { get; set; } = "";
    public string File { get; set; } = "";
    public int LineHint { get; set; }
}
