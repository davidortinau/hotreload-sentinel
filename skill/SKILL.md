---
name: hotreload-sentinel
description: AI-assisted .NET MAUI Hot Reload diagnostics. Monitors hot reload sessions, validates environment setup, tracks per-change developer confirmations, and generates bug reports. Use when hot reload isn't working, UI doesn't update after code changes, or for monitoring hot reload sessions in real-time.
---

# Hot Reload Sentinel — Copilot Skill

## Monitoring Workflow

When a developer asks to "watch hot reload", "monitor hot reload", or any hot-reload related task:

### Step 1: Prerequisites Check (ALWAYS do this first)

1. Call `hr_diagnose` to validate the environment
2. Review results for any `fail` or `warn` status checks
3. If prerequisites are unmet, report them clearly to the developer:
   - Missing ENC log directory → explain it must be set before launching the IDE
   - Missing BOM on .cs files → offer to auto-fix with `hr_diagnose --fix`
   - Missing MetadataUpdateHandler → offer to scaffold one for their framework
   - VS Code settings not configured → offer to auto-fix
4. Ask the developer if they'd like you to fix any auto-fixable issues before proceeding
5. If critical prerequisites are unmet (no ENC log dir, no Session.log), stop and explain — watching without these will produce no useful data

### Step 2: Start Monitoring

1. Call `hr_watch_start` to start the background watcher
2. Call `hr_status` to confirm watcher is alive and check heartbeat connectivity
3. If heartbeat is not detected, warn the developer that app-side diagnostics (HotReloadSentinel.Diagnostics NuGet) may not be installed

### Step 3: Follow and Confirm

1. Call `hr_watch_follow` (with seconds=60) to stream events
2. When output contains `follow_pending_confirmation=true`:
   - Call `hr_pending_atoms` to get the change atoms
   - For each atom, use `ask_user` to confirm with the developer individually
   - Record all answers with `hr_record_verdict`
3. Repeat `hr_watch_follow` to catch more events — do NOT wait for the developer to prompt you
4. When done, call `hr_watch_stop`
5. If failures were recorded, offer to call `hr_draft_issue` to generate a bug report

## Environment Setup Requirements

### CRITICAL: ENC Log Directory
```bash
# Must be set BEFORE launching VS Code
export Microsoft_CodeAnalysis_EditAndContinue_LogDir=/tmp/HotReloadLog  # macOS/Linux
set Microsoft_CodeAnalysis_EditAndContinue_LogDir=%temp%\HotReloadLog   # Windows
```

### File Encoding
All `.cs` files MUST be UTF-8 with BOM. Files without BOM will silently fail hot reload.

### VS Code Settings
```json
{
  "csharp.experimental.debug.hotReload": true,
  "csharp.debug.hotReloadOnSave": true,
  "csharp.debug.hotReloadVerbosity": "detailed"
}
```

## MetadataUpdateHandler (Required for MauiReactor / C# Markup)

### MauiReactor
```csharp
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadService))]

internal static class HotReloadService
{
    public static void ClearCache(Type[]? updatedTypes) { }
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        HotReloadTriggered?.Invoke();
    }
    public static event Action? HotReloadTriggered;
}
```
Base pages subscribe: `HotReloadService.HotReloadTriggered += () => Invalidate();`

### C# Markup (CommunityToolkit)
Implement `ICommunityToolkitHotReloadHandler` on pages.

### Standard XAML
Usually works without custom handler. If not, check linker settings.

## Diagnostic Reasoning

Use these rules when interpreting sentinel output:

| Scenario | Meaning | Action |
|---|---|---|
| ENC applied + heartbeat advanced + visual OK | Working correctly | Record as passed |
| ENC applied + heartbeat advanced + visual FAILED | UI framework rendering bug | File bug against MAUI/MauiReactor with diff + heartbeat proof |
| ENC applied + heartbeat NOT advanced | Delta didn't reach app or handler didn't fire | Check MetadataUpdateHandler registration |
| ENC applied + no heartbeat endpoint | Can't determine if app received delta | Recommend installing HotReloadSentinel.Diagnostics NuGet |
| ENC blocked | Compilation error | Show error message from Session.log |
| `not_applied` on active TFM | Stale build | Recommend rebuild |
| `not_applied_other_tfm` | Normal multi-TFM noise | Ignore |
| `ENC1008` | Rude edit | Restart debug session required |
| `connection_lost` | Debug session disconnected | Restart debug session |

## Common Failure Patterns

1. **"Nothing happens when I save"** → Check Debug config, F5 attached, file saved
2. **"Unsupported edit" / "Rude edit"** → Adding methods/fields/properties requires restart
3. **Changes apply but UI doesn't update** → Re-trigger code path, check MetadataUpdateHandler
4. **Shadow/style not rendering on specific controls** → Likely MAUI handler bug, file issue

## References

- [Diagnosing Hot Reload (MAUI Wiki)](https://github.com/dotnet/maui/wiki/Diagnosing-Hot-Reload)
- [Supported code changes (C#)](https://learn.microsoft.com/visualstudio/debugger/supported-code-changes-csharp)
- [XAML Hot Reload for .NET MAUI](https://learn.microsoft.com/dotnet/maui/xaml/hot-reload)
