namespace HotReloadSentinel.Mcp;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotReloadSentinel.Verdicts;

/// <summary>
/// Stdio JSON-RPC MCP server implementing MCP protocol.
/// Dispatches tool calls to sentinel commands via subprocess.
/// </summary>
public sealed class McpServer
{
    readonly string _selfPath;
    readonly VerdictStore _store;

    public McpServer(string selfPath, VerdictStore store)
    {
        _selfPath = selfPath;
        _store = store;
    }

    public Task RunAsync(CancellationToken ct)
    {
        return RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), ct);
    }

    public async Task RunAsync(Stream input, Stream output, CancellationToken ct)
    {
        var transport = new McpTransport(input, output);

        while (!ct.IsCancellationRequested)
        {
            var message = await transport.ReadMessageAsync(ct);
            if (message is null) break;

            var response = HandleMessage(message.Value);
            if (response is not null)
                await transport.WriteMessageAsync(response.Value, ct);
        }
    }

    JsonElement? HandleMessage(JsonElement message)
    {
        if (!message.TryGetProperty("id", out var idEl))
            return null; // Notification, no response needed

        var id = idEl.Clone();
        var method = message.TryGetProperty("method", out var methodEl) ? methodEl.GetString() : null;
        var paramsEl = message.TryGetProperty("params", out var p) ? p : default;

        try
        {
            return method switch
            {
                "initialize" => MakeOk(id, BuildInitResult(paramsEl)),
                "initialized" or "ping" => MakeOk(id, JsonSerializer.SerializeToElement(new { })),
                "tools/list" => MakeOk(id, JsonSerializer.SerializeToElement(new { tools = Tools.ToolList })),
                "tools/call" => MakeOk(id, HandleToolCall(paramsEl)),
                _ => MakeError(id, -32601, $"Method not found: {method}")
            };
        }
        catch (Exception ex)
        {
            return MakeError(id, -32000, ex.Message);
        }
    }

    JsonElement HandleToolCall(JsonElement paramsEl)
    {
        var name = paramsEl.TryGetProperty("name", out var n) ? n.GetString() : null;
        var arguments = paramsEl.TryGetProperty("arguments", out var a) ? a : default;

        var result = name switch
        {
            "hr_watch_start" => StartWatcher(),
            "hr_watch_stop" => WrapText(RunCommand("watch-stop")),
            "hr_status" => WrapText(RunCommand("status")),
            "hr_diagnose" => WrapText(RunCommand("diagnose")),
            "hr_report" => WrapText(BuildReport()),
            "hr_watch_follow" => RunWatchFollow(arguments),
            "hr_pending_atoms" => BuildPendingAtoms(),
            "hr_record_verdict" => HandleRecordVerdict(arguments),
            "hr_draft_issue" => RunDraftIssue(arguments),
            _ => throw new ArgumentException($"Unknown tool: {name}")
        };

        return result;
    }

    JsonElement StartWatcher()
    {
        // Start watch-start without stdio redirection to avoid blocking when watch-start
        // itself launches a long-running child process.
        var psi = new ProcessStartInfo(_selfPath)
        {
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("watch-start");

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start watch-start process");
        return WrapText("hr_watch_start: requested");
    }

    string RunCommand(string command, string[]? extraArgs = null)
    {
        var args = new List<string> { command };
        if (extraArgs is not null) args.AddRange(extraArgs);

        var psi = new ProcessStartInfo(_selfPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(15000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Command '{command}' timed out after 15s.");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        var stdout = stdoutTask.Result.Trim();
        var stderr = stderrTask.Result.Trim();

        if (proc.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"Command '{command}' failed (exit={proc.ExitCode}). {detail}".Trim());
        }

        return stdout;
    }

    JsonElement RunWatchFollow(JsonElement arguments)
    {
        var seconds = arguments.ValueKind != JsonValueKind.Undefined
            && arguments.TryGetProperty("seconds", out var s) ? s.GetInt32() : 60;
        var output = RunCommand("watch-follow", ["--seconds", seconds.ToString(), "--no-confirm"]);
        return WrapText(output);
    }

    JsonElement RunDraftIssue(JsonElement arguments)
    {
        var includeSuccessful = arguments.ValueKind != JsonValueKind.Undefined
            && arguments.TryGetProperty("include_successful", out var s) && s.GetBoolean();
        var extra = includeSuccessful ? new[] { "--include-successful" } : null;
        var output = RunCommand("draft-issue", extra);
        return WrapText(output);
    }

    JsonElement BuildPendingAtoms()
    {
        var pending = _store.GetPending();
        var result = new
        {
            pending = pending.Select(v => new
            {
                apply_index = v.ApplyIndex,
                artifact_pair = v.ArtifactPair,
                verdict = v.Verdict,
                atoms = v.Atoms.Select((a, i) => new
                {
                    index = i,
                    kind = a.Kind,
                    control_hint = a.ControlHint,
                    change_summary = a.ChangeSummary,
                    file = a.File,
                    line_hint = a.LineHint,
                })
            }),
            message = pending.Count == 0 ? "No unconfirmed atoms." : null
        };
        return WrapText(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
    }

    JsonElement HandleRecordVerdict(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("apply_index and verdicts are required");

        var applyIndex = arguments.GetProperty("apply_index").GetInt32();
        var verdictsEl = arguments.GetProperty("verdicts");
        var verdicts = new Dictionary<string, string>();
        foreach (var prop in verdictsEl.EnumerateObject())
            verdicts[prop.Name] = prop.Value.GetString() ?? "unknown";

        var (found, verdict) = _store.RecordVerdict(applyIndex, verdicts);
        if (!found)
            throw new ArgumentException($"No verdict entry found for apply_index={applyIndex}");

        return WrapText(JsonSerializer.Serialize(new { ok = true, apply_index = applyIndex, verdict }));
    }

    string BuildReport()
    {
        var state = _store.Read();
        var hints = new List<string>();

        if (state.Enc1008Count > 0)
            hints.Add("ENC1008 detected; rebuild solution and restart debug session.");
        if (state.NotAppliedCount > 0)
            hints.Add("Detected 'changes not applied'; verify target framework/build freshness.");
        if (state.ApplyCount == 0)
            hints.Add("No apply events seen; ensure Session.log is active.");
        if (!state.HeartbeatOk)
            hints.Add("No live heartbeat endpoint detected; app may be stale or not running.");
        if (hints.Count == 0)
            hints.Add("No obvious issues found.");

        return $"hr_report: status={state.Status} hints={string.Join("; ", hints)}";
    }

    static readonly HashSet<string> SupportedVersions = new(StringComparer.Ordinal)
    {
        "2024-11-05", "2025-03-26", "2025-06-18"
    };

    const string DefaultProtocolVersion = "2024-11-05";

    static JsonElement BuildInitResult(JsonElement paramsEl)
    {
        // Negotiate protocol version: use the client's version if we support it
        var clientVersion = DefaultProtocolVersion;
        if (paramsEl.ValueKind != JsonValueKind.Undefined
            && paramsEl.TryGetProperty("protocolVersion", out var vEl)
            && vEl.GetString() is string v)
        {
            clientVersion = SupportedVersions.Contains(v) ? v : DefaultProtocolVersion;
        }

        return JsonSerializer.SerializeToElement(new
        {
            protocolVersion = clientVersion,
            capabilities = new
            {
                tools = new { listChanged = false },
                prompts = new { listChanged = false },
                resources = new { listChanged = false, subscribe = false },
            },
            serverInfo = new { name = "hotreload-sentinel", version = "0.1.1" },
        });
    }

    static JsonElement WrapText(string text)
    {
        return JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text } }
        });
    }

    static JsonElement MakeOk(JsonElement id, JsonElement result)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["result"] = JsonNode.Parse(result.GetRawText()),
        };
        return JsonSerializer.SerializeToElement(obj);
    }

    static JsonElement MakeError(JsonElement id, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
        return JsonSerializer.SerializeToElement(obj);
    }
}
