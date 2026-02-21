# Hot Reload Sentinel

AI-assisted .NET MAUI Hot Reload diagnostics — monitors sessions, validates environment, tracks per-change confirmations, and generates bug reports.

## Install

```bash
dotnet tool install -g HotReloadSentinel
hotreload-sentinel init
```

## What it does

| Feature | Description |
|---|---|
| **Environment validation** | Checks ENC LogDir, BOM encoding, MetadataUpdateHandler, IDE settings |
| **Live monitoring** | Watches Session.log + app heartbeat in real-time |
| **Per-change confirmation** | Asks you about each individual code change via Copilot CLI |
| **Bug report generation** | Generates structured GitHub issues from failed changes |
| **MCP server** | 9 tools for Copilot CLI integration |

## Quick Start

```bash
# 1. Install
dotnet tool install -g HotReloadSentinel

# 2. Set up environment (one time)
hotreload-sentinel init

# 3. In Copilot CLI, ask:
#    "Watch my hot reload session"
#    "Diagnose hot reload"
#    "Generate bug report from hot reload failures"
```

## MCP Tools

| Tool | Description |
|------|-------------|
| `hr_watch_start` | Start background watcher |
| `hr_watch_stop` | Stop background watcher |
| `hr_status` | Current status (IDLE/ACTIVE/DEGRADED) |
| `hr_diagnose` | Full environment validation |
| `hr_report` | Quick state summary |
| `hr_watch_follow` | Stream events for N seconds |
| `hr_pending_atoms` | Get unconfirmed change atoms |
| `hr_record_verdict` | Record per-atom developer verdicts |
| `hr_draft_issue` | Generate GitHub issue from failures |

## How it works

```
Developer saves file in VS Code
  → Sentinel detects Session.log changes
  → Copilot CLI polls hr_watch_follow → sees new apply event
  → Calls hr_pending_atoms → gets change atoms
  → Asks developer per atom: "Did this render? [yes/no/partial]"
  → Records with hr_record_verdict
  → hr_draft_issue generates bug report splitting ✅ worked vs ❌ failed
```

## Optional: App-Side Diagnostics

For deeper diagnostics (heartbeat confirmation), add the diagnostics NuGet:

```bash
dotnet add package HotReloadSentinel.Diagnostics
```

```csharp
// In MauiProgram.cs
builder.UseHotReloadDiagnostics();
```

## License

MIT
