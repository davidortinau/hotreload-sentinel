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
                LastUpdateTimestamp = payload.TryGetProperty("lastUpdateTimestampUtc", out var tsEl) ? tsEl.GetString() : null,
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
    public string? LastUpdateTimestamp { get; set; }
    public string? Error { get; set; }
    public JsonElement? RawPayload { get; set; }
}
