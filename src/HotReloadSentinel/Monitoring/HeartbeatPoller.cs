namespace HotReloadSentinel.Monitoring;

using System.Text.Json;

/// <summary>
/// Polls a hot reload diagnostic HTTP endpoint for heartbeat info.
/// </summary>
public sealed class HeartbeatPoller
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static async Task<HeartbeatResult> PollAsync(string url)
    {
        try
        {
            var heartbeatUrl = url.TrimEnd('/') + "/heartbeat";
            var response = await Http.GetStringAsync(heartbeatUrl);
            var payload = JsonSerializer.Deserialize<JsonElement>(response);

            return new HeartbeatResult
            {
                Ok = true,
                Url = url,
                Pid = payload.TryGetProperty("pid", out var pidEl) ? pidEl.GetInt32() : null,
                UpdateCount = payload.TryGetProperty("updateCount", out var ucEl) ? ucEl.GetInt32() : null,
                FailedCount = payload.TryGetProperty("failedCount", out var fcEl) ? fcEl.GetInt32() : null,
                ApplySequence = payload.TryGetProperty("applySequence", out var asEl) && asEl.TryGetInt64(out var asV) ? asV : null,
                LastUpdateTimestamp = payload.TryGetProperty("lastUpdateTimestampUtc", out var tsEl) && tsEl.ValueKind == JsonValueKind.String ? tsEl.GetString() : null,
                LastFailureTimestamp = payload.TryGetProperty("lastFailureTimestampUtc", out var ftsEl) && ftsEl.ValueKind == JsonValueKind.String ? ftsEl.GetString() : null,
                LastFailureReason = payload.TryGetProperty("lastFailureReason", out var lfrEl) && lfrEl.ValueKind == JsonValueKind.String ? lfrEl.GetString() : null,
                RawPayload = payload
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new HeartbeatResult { Ok = false, Url = url, Error = ex.Message };
        }
    }
}

public sealed class HeartbeatResult
{
    public bool Ok { get; set; }
    public string Url { get; set; } = "";
    public int? Pid { get; set; }
    public int? UpdateCount { get; set; }
    public int? FailedCount { get; set; }
    public long? ApplySequence { get; set; }
    public string? LastUpdateTimestamp { get; set; }
    public string? LastFailureTimestamp { get; set; }
    public string? LastFailureReason { get; set; }
    public string? Error { get; set; }
    public JsonElement? RawPayload { get; set; }
}
