# Hot Reload Sentinel

A diagnostic tool for .NET MAUI Hot Reload. Monitors reload sessions, validates your environment, confirms each code change with the developer, and generates structured bug reports when things go wrong.

Built for developers who use GitHub Copilot CLI but works standalone too.

---

## The Problem

Hot Reload in .NET MAUI is powerful but fragile. A missing environment variable, a file saved without a BOM, a handler that was never registered -- any of these will silently break your reload loop. You save a file, nothing happens, and you have no idea why.

Worse, some changes partially succeed: the Edit and Continue engine applies the delta, but the UI never updates. The framework reports success while the screen shows stale content. Diagnosing this requires cross-referencing Session.log entries, artifact diffs, framework-specific handler requirements, and IDE settings -- a process that is tedious, error-prone, and deeply specific to your project's UI approach.

Hot Reload Sentinel automates all of it.

## What It Does

**Diagnose** -- Validates your environment before you start debugging. Checks that the ENC log directory is configured, all `.cs` files are UTF-8 with BOM, your project has the correct `MetadataUpdateHandler` for its UI framework (MauiReactor, C# Markup, Blazor Hybrid, or XAML), and your VS Code settings are correct.

**Monitor** -- Watches `Session.log` and an optional app-side heartbeat endpoint in real-time. Detects new apply events, extracts the specific code changes (what we call "atoms"), and tracks session health as IDLE, ACTIVE, or DEGRADED.

**Confirm** -- After each reload, asks the developer about every individual change: did the label text update? Did the shadow render? Did the layout shift? Records per-atom verdicts of `yes`, `no`, or `partial`. This granularity matters because a single save can contain multiple changes, and some may succeed while others fail.

**Report** -- Generates a structured GitHub issue from the session, splitting confirmed successes from failures. Includes the exact code diffs, ENC log excerpts, framework context, and environment details needed to file a reproducible bug report.

## Install

```
dotnet tool install -g HotReloadSentinel
```

Run the one-time setup to configure your environment, install the Copilot skill, and register the MCP server:

```
hotreload-sentinel init
```

This does three things:

1. Adds the MCP server configuration to `~/.copilot/config/mcp.json`
2. Installs the Copilot skill to `~/.copilot/skills/hotreload-sentinel/`
3. Validates your environment and reports anything that needs attention

## Usage

### With GitHub Copilot CLI

Once installed, just ask naturally:

- "Watch my hot reload session"
- "Diagnose my hot reload environment"
- "Check if my project is set up for hot reload"
- "Generate a bug report from my hot reload failures"

The Copilot skill teaches the agent how to use the MCP tools, interpret diagnostic results, and walk you through per-change confirmations.

### Standalone CLI

Every capability is also available directly from the command line.

```
hotreload-sentinel diagnose --project-dir ./src/MyApp
```

Returns structured JSON with pass/warn/fail results for every check:

```json
{
  "environment": {
    "encLogDir": { "status": "pass", "detail": "/tmp/HotReloadLog" },
    "sessionLog": { "status": "pass", "detail": "Last modified 12s ago" }
  },
  "encoding": {
    "status": "pass",
    "filesScanned": 47,
    "missingBom": []
  },
  "project": {
    "framework": "MauiReactor",
    "hasMetadataUpdateHandler": true,
    "targetFrameworks": ["net9.0-ios", "net9.0-android", "net9.0-maccatalyst"]
  },
  "ide": {
    "hotReloadEnabled": true,
    "hotReloadOnSave": true
  }
}
```

Other commands:

```
hotreload-sentinel watch-start        # Start background monitoring
hotreload-sentinel watch-stop         # Stop background monitoring
hotreload-sentinel status             # Current session state
hotreload-sentinel watch-follow 60    # Stream events for 60 seconds
hotreload-sentinel pending-atoms      # List unconfirmed change atoms
hotreload-sentinel record-verdict     # Record per-atom verdicts
hotreload-sentinel draft-issue        # Generate GitHub issue markdown
hotreload-sentinel fix                # Auto-fix common problems
```

### Auto-Fix

The `fix` command (and the `hr_diagnose` MCP tool with `--fix`) can automatically repair common issues:

- Add UTF-8 BOM to `.cs` files missing it
- Scaffold a `MetadataUpdateHandler` appropriate for your UI framework
- Configure VS Code Hot Reload settings

```
hotreload-sentinel fix --project-dir ./src/MyApp
```

## MCP Server

The MCP server exposes 9 tools over stdio JSON-RPC, compatible with the protocol version `2025-06-18` used by GitHub Copilot CLI.

| Tool | Purpose |
|------|---------|
| `hr_watch_start` | Start the background session watcher |
| `hr_watch_stop` | Stop the background watcher |
| `hr_status` | Get current status (IDLE / ACTIVE / DEGRADED) |
| `hr_diagnose` | Run full environment and project diagnostics |
| `hr_report` | Summarize current session state |
| `hr_watch_follow` | Poll for events over a time window |
| `hr_pending_atoms` | Retrieve unconfirmed change atoms from recent applies |
| `hr_record_verdict` | Store per-atom developer verdicts (yes / no / partial) |
| `hr_draft_issue` | Generate a GitHub issue draft from collected verdicts |

The server runs as a subprocess managed by the Copilot CLI. You do not need to start it manually.

## How the Confirmation Flow Works

```
1. Developer saves a .cs file in VS Code
2. .NET Hot Reload applies the delta (logged in Session.log)
3. Sentinel detects the apply event and extracts change atoms
4. Copilot CLI calls hr_pending_atoms, gets a list like:
     [0] Label text changed: "Hello" -> "Hello World"  (FormsPage.cs:42)
     [1] Shadow added to Card border                    (FormsPage.cs:58)
5. Agent asks the developer about each atom individually
6. Developer responds: [0] yes, [1] no
7. Agent calls hr_record_verdict with the results
8. On request, hr_draft_issue produces a bug report that includes:
     - Atom [0]: PASSED (label text change)
     - Atom [1]: FAILED (shadow on Border)
     - Relevant Session.log excerpt
     - Code diff for the failed atom
     - Environment and framework details
```

This per-atom granularity prevents a common diagnostic trap: a save that contains both a working text change and a broken shadow change would otherwise be reported as simply "hot reload failed," losing the signal about what specifically broke.

## App-Side Diagnostics (Optional)

For deeper integration, add the diagnostics NuGet to your MAUI app:

```
dotnet add package HotReloadSentinel.Diagnostics
```

In `MauiProgram.cs`:

```csharp
var builder = MauiApp.CreateBuilder();

builder.UseHotReloadDiagnostics();  // Add this line

// ... rest of your setup
```

This provides:

- **Heartbeat endpoint** -- An HTTP listener the sentinel can ping to confirm the app is responsive after a reload. The sentinel uses this to distinguish "reload succeeded but UI didn't update" from "app crashed."
- **Update counter** -- Tracks how many metadata updates the app has received, exposed via the heartbeat response.
- **Automatic MetadataUpdateHandler** -- Registered via assembly attribute. No manual wiring required.
- **Port file** -- Writes the heartbeat port to a known location so the sentinel can discover it automatically.

The diagnostics package is `#if DEBUG` guarded. It compiles to a no-op in Release builds.

## Framework Support

The sentinel understands the Hot Reload requirements for each .NET MAUI UI approach:

| Framework | What Sentinel Checks |
|-----------|---------------------|
| **XAML** | Standard MetadataUpdateHandler, XAML compilation settings |
| **MauiReactor** | `RuntimeHostConfigurationOption` for `MauiReactor.HotReload`, custom handler with `Invalidate()` |
| **C# Markup** | `ICommunityToolkitHotReloadHandler` implementation, `UseMauiCommunityToolkitMarkup()` registration |
| **Blazor Hybrid** | BlazorWebView configuration, `_Imports.razor` presence, wwwroot structure |

Framework detection is automatic based on NuGet references and code analysis.

## Environment Requirements

- .NET 9.0 SDK or later
- macOS or Windows (Linux support is partial)
- VS Code with C# Dev Kit, or Visual Studio 2022+
- The `Microsoft_CodeAnalysis_EditAndContinue_LogDir` environment variable must be set (the `init` command checks this)

## Building from Source

```
git clone https://github.com/davidortinau/hotreload-sentinel.git
cd hotreload-sentinel
dotnet build
dotnet test
dotnet pack -c Release -o ./nupkg
```

Install locally as a global tool:

```
dotnet tool install -g --add-source ./nupkg HotReloadSentinel
```

## License

MIT. See [LICENSE](LICENSE) for details.
