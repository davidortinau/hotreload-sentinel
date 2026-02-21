---
name: hotreload-sentinel
description: AI-assisted .NET MAUI Hot Reload diagnostics. Monitors hot reload sessions, validates environment setup, tracks per-change developer confirmations, and generates bug reports. Use when hot reload isn't working, UI doesn't update after code changes, or for monitoring hot reload sessions in real-time.
---

# Hot Reload Sentinel — Copilot Skill

## Monitoring Workflow

When a developer asks to "watch hot reload" or "monitor hot reload":

1. Call `hr_diagnose` first to validate the environment
2. If any issues found, explain and offer to fix
3. Call `hr_watch_start` to start the background watcher
4. Call `hr_watch_follow` (with seconds=60) to stream events
5. When output contains `follow_pending_confirmation=true`:
   - Call `hr_pending_atoms` to get the change atoms
   - Use `ask_user` to confirm each atom with the developer
   - Record answers with `hr_record_verdict`
6. Repeat `hr_watch_follow` to catch more events
7. When done, call `hr_watch_stop`
8. If failures were recorded, call `hr_draft_issue` to generate bug reports

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

| Scenario | Meaning |
|---|---|
| ENC applied + heartbeat advanced + visual OK | ✅ Working correctly |
| ENC applied + heartbeat advanced + visual FAILED | 🐛 MAUI rendering bug |
| ENC applied + heartbeat NOT advanced | ⚠️ MetadataUpdateHandler missing/broken |
| ENC blocked | ❌ Compilation error — check error message |
| `not_applied` on active TFM | ⚠️ Stale build, need rebuild |
| `not_applied_other_tfm` | ✅ Normal, ignore (multi-TFM project) |
| `ENC1008` | ❌ Rude edit — restart required |
| `connection_lost` | ⚠️ Debug session disconnected |

## Common Failure Patterns

1. **"Nothing happens when I save"** → Check Debug config, F5 attached, file saved
2. **"Unsupported edit" / "Rude edit"** → Adding methods/fields/properties requires restart
3. **Changes apply but UI doesn't update** → Re-trigger code path, check MetadataUpdateHandler
4. **Shadow/style not rendering on specific controls** → Likely MAUI handler bug, file issue

## References

- [Diagnosing Hot Reload (MAUI Wiki)](https://github.com/dotnet/maui/wiki/Diagnosing-Hot-Reload)
- [Supported code changes (C#)](https://learn.microsoft.com/visualstudio/debugger/supported-code-changes-csharp)
- [XAML Hot Reload for .NET MAUI](https://learn.microsoft.com/dotnet/maui/xaml/hot-reload)
