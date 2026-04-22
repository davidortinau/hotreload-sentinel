namespace HotReloadSentinel.Mcp;

using System.Text.Json;

/// <summary>
/// MCP tool definitions for the hot reload sentinel.
/// </summary>
public static class Tools
{
    public static readonly JsonElement ToolList = JsonSerializer.Deserialize<JsonElement>("""
    [
        {
            "name": "hr_watch_start",
            "description": "Start hotreload sentinel background watcher.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": false}
        },
        {
            "name": "hr_status",
            "description": "Get current hotreload sentinel status.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": false}
        },
        {
            "name": "hr_diagnose",
            "description": "Run hotreload sentinel diagnose summary.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": false}
        },
        {
            "name": "hr_watch_stop",
            "description": "Stop hotreload sentinel background watcher.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": false}
        },
        {
            "name": "hr_report",
            "description": "Summarize sentinel state file with key hints.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": false}
        },
        {
            "name": "hr_watch_follow",
            "description": "Foreground follow stream for status and alerts. When output contains 'follow_pending_confirmation=true', call hr_pending_atoms to get atoms, then use ask_user to confirm each atom with the developer, then call hr_record_verdict with results.",
            "inputSchema": {
                "type": "object",
                "properties": {"seconds": {"type": "integer", "minimum": 0, "default": 60}},
                "additionalProperties": false
            }
        },
        {
            "name": "hr_draft_issue",
            "description": "Generate a GitHub issue draft from hot reload verdicts collected during this session. Returns the draft markdown and file path.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "include_successful": {
                        "type": "boolean",
                        "description": "When true, include successful verdicts in the summary draft.",
                        "default": false
                    }
                },
                "additionalProperties": false
            }
        },
        {
            "name": "hr_pending_atoms",
            "description": "Return unconfirmed change atoms from recent hot reload apply events. Each atom includes a 'friendliness' classification (e.g. field_or_property_initializer, lifecycle_hook, named_render_method, other_method_body) so the agent can advise whether the change should auto-render or requires re-triggering the code path. Call this after detecting a new apply event to get atoms the developer should confirm.",
            "inputSchema": {"type": "object", "properties": {}, "additionalProperties": false}
        },
        {
            "name": "hr_friendliness_advice",
            "description": "Get hot-reload friendliness analysis for atoms in a recent apply event. Use this when an atom comes back 'no' or 'partial' to surface concrete code-level advice (rude-edit detection, MetadataUpdateHandler suggestion, MauiReactor refactor suggestion). Defaults to the most recent pending apply.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "apply_index": {
                        "type": "integer",
                        "description": "Optional. The apply_index from hr_pending_atoms. Defaults to the most recent unconfirmed apply if omitted."
                    },
                    "project_dir": {
                        "type": "string",
                        "description": "Optional. Project root used to resolve atom file basenames to full paths. Defaults to the current working directory."
                    }
                },
                "additionalProperties": false
            }
        },
        {
            "name": "hr_record_verdict",
            "description": "Record per-atom developer verdicts for a specific apply event. Call after asking the developer about each atom.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "apply_index": {
                        "type": "integer",
                        "description": "The apply_index from pending-atoms to record verdicts for."
                    },
                    "verdicts": {
                        "type": "object",
                        "description": "Map of atom index (string) to verdict: 'yes', 'no', or 'partial'. E.g. {\"0\": \"yes\", \"1\": \"no\"}",
                        "additionalProperties": {"type": "string", "enum": ["yes", "no", "partial"]}
                    }
                },
                "required": ["apply_index", "verdicts"],
                "additionalProperties": false
            }
        }
    ]
    """);
}
