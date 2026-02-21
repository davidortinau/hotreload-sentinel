using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using HotReloadSentinel.Diagnostics;
using HotReloadSentinel.IssueGeneration;
using HotReloadSentinel.Mcp;
using HotReloadSentinel.Monitoring;
using HotReloadSentinel.Parsing;
using HotReloadSentinel.Verdicts;

var platformIsWindows = OperatingSystem.IsWindows();
var tmpDir = platformIsWindows ? Path.GetTempPath() : "/tmp";
var hotReloadDir = Path.Combine(tmpDir, "HotReloadLog");
var sessionLogPath = Path.Combine(hotReloadDir, "Session.log");
var statePath = Path.Combine(tmpDir, "hotreload-sentinel.state.json");
var pidPath = Path.Combine(tmpDir, "hotreload-sentinel.pid");
var portGlob = platformIsWindows ? "hotreload-diag-*.port" : "hotreload-diag-*.port";
var watchLogPath = Path.Combine(tmpDir, "hotreload-sentinel.watch.log");

var store = new VerdictStore(statePath);

// Root command
var rootCommand = new RootCommand("AI-assisted .NET MAUI Hot Reload diagnostics sentinel.");

// watch-start
var watchStartCmd = new Command("watch-start", "Start background watch loop.");
watchStartCmd.SetHandler(() =>
{
    if (File.Exists(pidPath))
    {
        var existingPid = int.TryParse(File.ReadAllText(pidPath).Trim(), out var p) ? p : 0;
        if (existingPid > 0 && IsProcessAlive(existingPid))
        {
            Console.WriteLine($"hr_watch_start: already_running pid={existingPid}");
            return;
        }
        File.Delete(pidPath);
    }

    var selfPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "hotreload-sentinel";
    var psi = new ProcessStartInfo(selfPath, "_watch-run")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    using var logFile = File.Open(watchLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
    var proc = Process.Start(psi);
    if (proc is not null)
    {
        Console.WriteLine($"hr_watch_start: started pid={proc.Id}");
        // Detach — don't wait
    }
});
rootCommand.AddCommand(watchStartCmd);

// watch-stop
var watchStopCmd = new Command("watch-stop", "Stop background watch loop.");
watchStopCmd.SetHandler(() =>
{
    if (!File.Exists(pidPath))
    {
        Console.WriteLine("hr_watch_stop: not_running");
        return;
    }
    var pid = int.TryParse(File.ReadAllText(pidPath).Trim(), out var p) ? p : 0;
    if (pid > 0 && IsProcessAlive(pid))
    {
        try { Process.GetProcessById(pid).Kill(); } catch { }
        Console.WriteLine($"hr_watch_stop: stopped pid={pid}");
    }
    else
    {
        Console.WriteLine("hr_watch_stop: not_running");
    }
    File.Delete(pidPath);
});
rootCommand.AddCommand(watchStopCmd);

// status
var statusCmd = new Command("status", "Show current sentinel status.");
statusCmd.SetHandler(() =>
{
    var state = store.Read();
    var pid = File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath).Trim(), out var p) ? p : 0;
    state.WatcherAlive = pid > 0 && IsProcessAlive(pid);
    var status = WatchLoop.ComputeStatus(state);
    Console.WriteLine($"hr_status: {status}");
    Console.WriteLine($"watcher_alive={state.WatcherAlive} last_heartbeat={state.LastHeartbeatTs} last_log_activity={state.LastLogActivityTs} selected_endpoint={state.SelectedEndpoint}");
});
rootCommand.AddCommand(statusCmd);

// diagnose
var diagnoseCmd = new Command("diagnose", "Full environment validation and hot reload diagnostics.");
var projectDirOption = new Option<string?>("--project-dir", "Project directory to analyze (auto-detected if not specified).");
diagnoseCmd.AddOption(projectDirOption);
diagnoseCmd.SetHandler((string? projDir) =>
{
    projDir ??= Directory.GetCurrentDirectory();
    var state = store.Read();
    var status = WatchLoop.ComputeStatus(state);

    Console.WriteLine($"hr_diagnose: status={status}");
    Console.WriteLine($"summary save_count={state.SaveCount} apply_count={state.ApplyCount} result_success={state.ResultSuccessCount} result_failure={state.ResultFailureCount} not_applied={state.NotAppliedCount} not_applied_other_tfm={state.NotAppliedOtherTfmCount} enc1008={state.Enc1008Count} app_heartbeat_recent={state.HeartbeatOk} heartbeat_update_count={state.LastHeartbeatUpdateCount}");

    // Run all diagnostic checks
    var checks = new List<DiagnosticCheck>();
    checks.AddRange(EnvironmentChecker.Run());
    checks.Add(EncodingChecker.Run(projDir));
    checks.AddRange(ProjectAnalyzer.Run(projDir));
    checks.AddRange(IdeSettingsChecker.Run(projDir));

    // Heartbeat check
    checks.Add(new DiagnosticCheck
    {
        Id = "heartbeat",
        Name = "App Heartbeat Endpoint",
        Status = state.HeartbeatOk ? CheckStatus.Pass : CheckStatus.Warn,
        Message = state.HeartbeatOk
            ? $"Heartbeat reachable at {state.SelectedEndpoint} (pid={state.SelectedPid})"
            : "No heartbeat endpoint reachable. App may not be running or diagnostics NuGet not installed.",
        AutoFixable = false,
    });

    var passed = checks.Count(c => c.Status == CheckStatus.Pass);
    var warned = checks.Count(c => c.Status == CheckStatus.Warn);
    var failed = checks.Count(c => c.Status == CheckStatus.Fail);
    var fixable = checks.Count(c => c.AutoFixable && c.Status != CheckStatus.Pass);

    Console.WriteLine();
    foreach (var c in checks)
    {
        var icon = c.Status switch { CheckStatus.Pass => "✅", CheckStatus.Warn => "⚠️", _ => "❌" };
        Console.WriteLine($"  {icon} [{c.Id}] {c.Name}: {c.Message}");
        if (c.AffectedFiles is { Count: > 0 })
        {
            foreach (var f in c.AffectedFiles.Take(5))
                Console.WriteLine($"       → {f}");
            if (c.AffectedFiles.Count > 5)
                Console.WriteLine($"       ... and {c.AffectedFiles.Count - 5} more");
        }
    }
    Console.WriteLine();
    Console.WriteLine($"checks: {passed} passed, {warned} warnings, {failed} failed, {fixable} auto-fixable");

    // JSON output for MCP
    var jsonResult = JsonSerializer.Serialize(new
    {
        checks = checks.Select(c => new
        {
            id = c.Id, name = c.Name,
            status = c.Status.ToString().ToLower(),
            message = c.Message,
            auto_fixable = c.AutoFixable,
            fix_command = c.FixCommand,
            affected_files = c.AffectedFiles,
        }),
        summary = $"{passed}/{checks.Count} checks passed, {fixable} auto-fixable issues",
        auto_fix_available = fixable > 0,
    }, new JsonSerializerOptions { WriteIndented = false });
    Console.WriteLine($"diagnose_json={jsonResult}");

    var artifact = ArtifactDiffer.FindLatest(hotReloadDir);
    if (artifact is not null)
    {
        Console.WriteLine($"artifact_file={artifact.SourceFile}");
        var preview = ArtifactDiffer.GenerateDiffPreview(artifact.OldPath, artifact.NewPath);
        if (!string.IsNullOrEmpty(preview))
            Console.WriteLine($"artifact_diff_preview={preview}");
    }
}, projectDirOption);
rootCommand.AddCommand(diagnoseCmd);

// fix
var fixCmd = new Command("fix", "Auto-fix diagnosed issues.");
var fixCheckOption = new Option<string>("--check", "Check ID to fix (e.g., bom_encoding, metadata_handler, vscode_verbosity).") { IsRequired = true };
var fixProjectDirOption = new Option<string?>("--project-dir", "Project directory.");
fixCmd.AddOption(fixCheckOption);
fixCmd.AddOption(fixProjectDirOption);
fixCmd.SetHandler((string checkId, string? projDir) =>
{
    projDir ??= Directory.GetCurrentDirectory();
    var (success, message) = AutoFixer.Fix(checkId, projDir);
    Console.WriteLine(success ? $"✅ Fixed [{checkId}]: {message}" : $"❌ [{checkId}]: {message}");
}, fixCheckOption, fixProjectDirOption);
rootCommand.AddCommand(fixCmd);

// watch-follow
var watchFollowCmd = new Command("watch-follow", "Stream status/alerts in foreground.");
var secondsOption = new Option<int>("--seconds", () => 60, "Follow duration in seconds (0 for unlimited).");
var intervalOption = new Option<int>("--interval", () => 2, "Polling interval seconds.");
var noConfirmOption = new Option<bool>("--no-confirm", "Skip interactive confirmation prompts.");
watchFollowCmd.AddOption(secondsOption);
watchFollowCmd.AddOption(intervalOption);
watchFollowCmd.AddOption(noConfirmOption);
watchFollowCmd.SetHandler(async (int seconds, int interval, bool noConfirm) =>
{
    // Auto-start watcher if not running
    var pid = File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath).Trim(), out var p) ? p : 0;
    if (pid == 0 || !IsProcessAlive(pid))
        watchStartCmd.Invoke(Array.Empty<string>());

    var deadline = seconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(seconds) : (DateTimeOffset?)null;
    var parser = new SessionLogParser();
    if (File.Exists(sessionLogPath))
    {
        try { parser.Offset = new FileInfo(sessionLogPath).Length; } catch { }
    }

    int prevApply = -1;
    string prevStatus = "";
    double? prevArtifactMtime = ArtifactDiffer.FindLatest(hotReloadDir)?.Mtime;

    Console.WriteLine($"hr_watch_follow: streaming (interval={interval}s, duration={deadline?.ToString() ?? "unlimited"}, confirm={!noConfirm})");

    while (deadline is null || DateTimeOffset.UtcNow < deadline)
    {
        var state = store.Read();
        pid = File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath).Trim(), out var pp) ? pp : 0;
        state.WatcherAlive = pid > 0 && IsProcessAlive(pid);
        var status = WatchLoop.ComputeStatus(state);
        var applyCount = state.ApplyCount;

        if (status != prevStatus || applyCount != prevApply)
            Console.WriteLine($"follow status={status} apply_count={applyCount} heartbeat_update_count={state.LastHeartbeatUpdateCount} selected_pid={state.SelectedPid}");

        if (applyCount > prevApply && prevApply >= 0)
        {
            var hbOk = state.LastHeartbeatUpdateCount.HasValue && state.LastHeartbeatUpdateCount.Value >= applyCount;
            Console.WriteLine(hbOk
                ? "follow_hint=Hot Reload apply observed and heartbeat advanced (likely successful)."
                : "follow_hint=Hot Reload apply observed but heartbeat did not advance (possible failure/stale process).");

            var artifact = ArtifactDiffer.FindLatest(hotReloadDir, prevArtifactMtime);
            if (artifact is not null)
            {
                prevArtifactMtime = artifact.Mtime;
                Console.WriteLine($"follow_change_file={artifact.SourceFile}");
                var preview = ArtifactDiffer.GenerateDiffPreview(artifact.OldPath, artifact.NewPath);
                Console.WriteLine($"follow_change_preview={preview}");

                var atoms = ChangeAtomExtractor.Extract(artifact.OldPath, artifact.NewPath);
                var artifactName = artifact.Name;

                if (atoms.Count > 0)
                {
                    Console.WriteLine($"follow_atoms_count={atoms.Count}");
                    for (int ai = 0; ai < atoms.Count; ai++)
                    {
                        var atom = atoms[ai];
                        Console.WriteLine($"follow_atom[{ai}]={atom.ChangeSummary} | control={atom.ControlHint} | {atom.File}:{atom.LineHint}");
                    }
                    Console.WriteLine($"follow_pending_confirmation=true apply_index={applyCount}");

                    // Store unconfirmed verdict
                    var entry = new VerdictEntry
                    {
                        ApplyIndex = applyCount,
                        ArtifactPair = artifactName,
                        Verdict = null,
                        Atoms = atoms.Select(a => new AtomInfo
                        {
                            Kind = a.Kind.ToString().ToLower(),
                            ControlHint = a.ControlHint,
                            ChangeSummary = a.ChangeSummary,
                            File = a.File,
                            LineHint = a.LineHint,
                        }).ToList(),
                    };
                    store.AppendVerdict(entry);
                }
            }
        }

        prevApply = applyCount;
        prevStatus = status;

        await Task.Delay(interval * 1000);
    }

    Console.WriteLine("hr_watch_follow: completed");

}, secondsOption, intervalOption, noConfirmOption);
rootCommand.AddCommand(watchFollowCmd);

// pending-atoms
var pendingAtomsCmd = new Command("pending-atoms", "Return unconfirmed change atoms as JSON.");
pendingAtomsCmd.SetHandler(() =>
{
    var pending = store.GetPending();
    var result = new
    {
        pending = pending.Select(v => new
        {
            apply_index = v.ApplyIndex,
            artifact_pair = v.ArtifactPair,
            verdict = v.Verdict,
            atoms = v.Atoms.Select((a, i) => new
            {
                index = i, kind = a.Kind, control_hint = a.ControlHint,
                change_summary = a.ChangeSummary, file = a.File, line_hint = a.LineHint,
            })
        }),
        message = pending.Count == 0 ? "No unconfirmed atoms." : null
    };
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
});
rootCommand.AddCommand(pendingAtomsCmd);

// record-verdict
var recordVerdictCmd = new Command("record-verdict", "Record per-atom verdicts for an apply event.");
var applyIndexOption = new Option<int>("--apply-index", "Apply index to record verdict for.") { IsRequired = true };
var verdictsJsonOption = new Option<string>("--verdicts-json", "JSON dict of atom index to verdict.") { IsRequired = true };
recordVerdictCmd.AddOption(applyIndexOption);
recordVerdictCmd.AddOption(verdictsJsonOption);
recordVerdictCmd.SetHandler((int applyIndex, string verdictsJson) =>
{
    var atomVerdicts = JsonSerializer.Deserialize<Dictionary<string, string>>(verdictsJson);
    if (atomVerdicts is null)
    {
        Console.WriteLine($"error: invalid JSON for verdicts: {verdictsJson}");
        return;
    }

    var (found, verdict) = store.RecordVerdict(applyIndex, atomVerdicts);
    if (!found)
    {
        Console.WriteLine($"error: no verdict entry found for apply_index={applyIndex}");
        return;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, apply_index = applyIndex, verdict }));

}, applyIndexOption, verdictsJsonOption);
rootCommand.AddCommand(recordVerdictCmd);

// draft-issue
var draftIssueCmd = new Command("draft-issue", "Generate a GitHub issue draft from hot reload verdicts.");
var includeSuccessfulOption = new Option<bool>("--include-successful", "Include successful verdicts in the draft.");
draftIssueCmd.AddOption(includeSuccessfulOption);
draftIssueCmd.SetHandler((bool includeSuccessful) =>
{
    var state = store.Read();
    var verdicts = state.Verdicts;
    var failed = verdicts.Where(v => v.Verdict is "all_failed" or "mixed" or "partial").ToList();

    string body;
    if (failed.Count == 0)
        body = IssueDraftBuilder.BuildSessionSummary(verdicts, state);
    else
        body = string.Join("\n\n---\n\n", failed.Select(f => IssueDraftBuilder.BuildIssueDraft(f, state)));

    var outPath = Path.Combine(tmpDir, $"hotreload-issue-draft-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.md");
    File.WriteAllText(outPath, body);
    Console.WriteLine($"draft_issue: written to {outPath}");

}, includeSuccessfulOption);
rootCommand.AddCommand(draftIssueCmd);

// mcp (stdio MCP server mode)
var mcpCmd = new Command("mcp", "Run as MCP stdio server for Copilot CLI.");
mcpCmd.SetHandler(async () =>
{
    var selfPath = Environment.ProcessPath ?? "hotreload-sentinel";
    var server = new McpServer(selfPath, store);
    await server.RunAsync(CancellationToken.None);
});
rootCommand.AddCommand(mcpCmd);

// init
var initCmd = new Command("init", "Initialize hotreload-sentinel in your project.");
initCmd.SetHandler(() =>
{
    Console.WriteLine("hotreload-sentinel init");

    // Add to mcp.json
    var mcpJsonPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot", "mcp.json");

    Console.WriteLine($"  MCP config: {mcpJsonPath}");
    Console.WriteLine("  To add manually, include in your mcp.json:");
    Console.WriteLine("""
    "hotreload-sentinel": {
      "command": "hotreload-sentinel",
      "args": ["mcp"]
    }
    """);

    // Copy skill
    var skillDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot", "skills", "hotreload-sentinel");
    var bundledSkill = Path.Combine(AppContext.BaseDirectory, "skill", "SKILL.md");

    if (File.Exists(bundledSkill))
    {
        Directory.CreateDirectory(skillDir);
        File.Copy(bundledSkill, Path.Combine(skillDir, "SKILL.md"), overwrite: true);
        Console.WriteLine($"  Skill installed to: {skillDir}");
    }
    else
    {
        Console.WriteLine("  Skill file not found in bundle; skipping.");
    }

    // Check env vars
    var encLogDir = Environment.GetEnvironmentVariable("Microsoft_CodeAnalysis_EditAndContinue_LogDir");
    if (string.IsNullOrEmpty(encLogDir))
        Console.WriteLine("  ⚠️  ENC LogDir not set. Run: export Microsoft_CodeAnalysis_EditAndContinue_LogDir=/tmp/HotReloadLog");
    else
        Console.WriteLine($"  ✅ ENC LogDir: {encLogDir}");

    Console.WriteLine("  Done. Restart Copilot CLI to activate MCP tools.");
});
rootCommand.AddCommand(initCmd);

// _watch-run (internal — background daemon entry point)
var watchRunCmd = new Command("_watch-run", "Internal: background watcher daemon.") { IsHidden = true };
watchRunCmd.SetHandler(async () =>
{
    var loop = new WatchLoop(sessionLogPath, Path.Combine(tmpDir, portGlob), hotReloadDir, store);
    File.WriteAllText(pidPath, Environment.ProcessId.ToString());
    try
    {
        await loop.RunAsync(CancellationToken.None);
    }
    finally
    {
        try { File.Delete(pidPath); } catch { }
    }
});
rootCommand.AddCommand(watchRunCmd);

return await rootCommand.InvokeAsync(args);

static bool IsProcessAlive(int pid)
{
    try { Process.GetProcessById(pid); return true; }
    catch { return false; }
}
