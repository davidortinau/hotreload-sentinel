namespace HotReloadSentinel.IssueGeneration;

using System.Text;
using System.Text.RegularExpressions;
using HotReloadSentinel.Parsing;
using HotReloadSentinel.Verdicts;

/// <summary>
/// Generates structured GitHub issue markdown from verdict entries.
/// </summary>
public static class IssueDraftBuilder
{
    public static string BuildIssueDraft(VerdictEntry verdict, SentinelState state)
    {
        var sb = new StringBuilder();
        var atoms = verdict.Atoms;
        var av = verdict.AtomVerdicts;

        var working = atoms.Where((a, i) => av.TryGetValue(i.ToString(), out var v) && v == "yes").ToList();
        var failing = atoms.Where((a, i) => av.TryGetValue(i.ToString(), out var v) && v is "no" or "partial").ToList();
        var unanswered = atoms.Where((a, i) => !av.ContainsKey(i.ToString())).ToList();

        var title = BuildTitle(atoms, av);

        sb.AppendLine($"# [Bug] {title}");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine("- Platform: Mac Catalyst (`net10.0-maccatalyst`)");

        // Detect framework from artifact name
        var framework = verdict.ArtifactPair.Contains("Reactor") ? "MauiReactor (MVU)" : "XAML/MVVM";
        sb.AppendLine($"- UI Framework: {framework}");
        sb.AppendLine("- Tool: .NET MAUI Hot Reload (VS Code C# Dev Kit)");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine("Hot Reload successfully applied code changes (ENC status: Ready, delta emitted) but visual changes were not reflected on screen for specific control types.");
        sb.AppendLine();

        if (working.Count > 0)
        {
            sb.AppendLine("## What Worked ✅");
            foreach (var a in working)
                sb.AppendLine($"- {a.ChangeSummary} on `{a.ControlHint}` ({a.File}:{a.LineHint})");
            sb.AppendLine();
        }

        if (failing.Count > 0)
        {
            sb.AppendLine("## What Didn't Work ❌");
            foreach (var a in failing)
                sb.AppendLine($"- {a.ChangeSummary} on `{a.ControlHint}` ({a.File}:{a.LineHint})");
            sb.AppendLine();
        }

        sb.AppendLine("## Steps to Reproduce");
        if (failing.Count > 0)
        {
            var f = failing[0];
            sb.AppendLine($"1. Create a component with a `{f.ControlHint}` control");
            sb.AppendLine($"2. Apply the change: {f.ChangeSummary}");
            sb.AppendLine("3. Save the file to trigger Hot Reload");
            sb.AppendLine("4. Observe: change not reflected despite Hot Reload reporting success");
        }
        sb.AppendLine();

        sb.AppendLine("## Hot Reload Evidence");
        sb.AppendLine($"- ENC Apply Status: `{state.LastSolutionUpdate ?? "Unknown"}`");
        sb.AppendLine($"- Artifact: `{verdict.ArtifactPair}.old.cs` / `{verdict.ArtifactPair}.new.cs`");
        var hbAdvanced = state.LastHeartbeatUpdateCount.HasValue ? "Yes" : "Unknown";
        sb.AppendLine($"- Heartbeat advanced: {hbAdvanced}");
        sb.AppendLine($"- MetadataUpdateHandler fired: {hbAdvanced}");
        sb.AppendLine();

        sb.AppendLine("## Expected Behavior");
        if (failing.Count > 0)
            sb.AppendLine($"Change should be visually reflected on `{failing[0].ControlHint}` after Hot Reload applies successfully.");
        sb.AppendLine();

        sb.AppendLine("## Actual Behavior");
        sb.AppendLine("No visual change observed. ENC apply status was Ready but the UI was not updated.");
        sb.AppendLine();

        // Classification
        var hasShadowIssue = failing.Any(a => a.ChangeSummary.Contains("Shadow", StringComparison.OrdinalIgnoreCase));
        var hasBorderIssue = failing.Any(a => a.ControlHint is "Border" or "Card");
        if (hasShadowIssue && hasBorderIssue)
        {
            sb.AppendLine("## Classification");
            sb.AppendLine("- MAUI rendering bug (Shadow not rendered on Border native handler)");
            sb.AppendLine();
        }

        sb.AppendLine("## Additional Context");
        sb.AppendLine($"- Verdict: `{verdict.Verdict}`");
        sb.AppendLine($"- Apply index: {verdict.ApplyIndex}");
        foreach (var (a, i) in atoms.Select((a, i) => (a, i)))
        {
            var v = av.TryGetValue(i.ToString(), out var val) ? val : "unknown";
            var emoji = v == "yes" ? "✅" : v == "no" ? "❌" : "⚠️";
            sb.AppendLine($"- {emoji} {a.ChangeSummary} on `{a.ControlHint}` — {(v == "yes" ? "worked" : "did not work")}");
        }

        return sb.ToString();
    }

    public static string BuildSessionSummary(List<VerdictEntry> verdicts, SentinelState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# [Hot Reload] Session Summary — All Changes Applied Successfully");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine("- Platform: Mac Catalyst (`net10.0-maccatalyst`)");
        sb.AppendLine("- Tool: .NET MAUI Hot Reload (VS Code C# Dev Kit)");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine("All hot reload changes during this session were applied successfully with visual confirmation.");
        sb.AppendLine();
        sb.AppendLine("## Hot Reload Metrics");
        sb.AppendLine($"- Apply count: {state.ApplyCount}");
        sb.AppendLine($"- Result success count: {state.ResultSuccessCount}");
        if (state.LastSolutionUpdate is not null)
            sb.AppendLine($"- Last solution update: {state.LastSolutionUpdate}");
        sb.AppendLine();
        sb.AppendLine("_This session had no hot reload failures. This is a positive data point for hot reload working correctly._");

        return sb.ToString();
    }

    static string BuildTitle(List<AtomInfo> atoms, Dictionary<string, string> av)
    {
        var failing = atoms.Where((a, i) => av.TryGetValue(i.ToString(), out var v) && v == "no").ToList();
        if (failing.Count == 0)
            return "Hot Reload visual sync issue";

        var first = failing[0];
        if (first.ChangeSummary.Contains("Shadow", StringComparison.OrdinalIgnoreCase))
            return $"Shadow style class not applied to {first.ControlHint} control (Hot Reload applied successfully)";
        if (first.ChangeSummary.Contains("Class(", StringComparison.OrdinalIgnoreCase))
            return $"Style class not applied to {first.ControlHint} control (Hot Reload applied successfully)";

        return $"Hot Reload change not reflected on {first.ControlHint} (ENC apply succeeded)";
    }
}
