namespace HotReloadSentinel.Parsing;

using System.Text.RegularExpressions;

/// <summary>
/// Extracts discrete change atoms from old/new artifact file pairs.
/// </summary>
public static class ChangeAtomExtractor
{
    static readonly string[] MauiControls =
    [
        "Border", "VStack", "HStack", "Label", "Button", "Card",
        "Grid", "StackLayout", "ScrollView", "Frame", "Image",
        "Entry", "Editor", "Picker", "Switch", "Slider", "CheckBox",
        "ContentView", "ContentPage", "CollectionView", "ListView",
        "ActivityIndicator", "ProgressBar", "BoxView", "AbsoluteLayout",
        "WebView", "SearchBar", "RadioButton", "RefreshView",
    ];

    static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

    public static List<ChangeAtom> Extract(string oldPath, string newPath)
    {
        var atoms = new List<ChangeAtom>();
        if (!File.Exists(oldPath) || !File.Exists(newPath))
            return atoms;

        var oldLines = File.ReadAllLines(oldPath);
        var newLines = File.ReadAllLines(newPath);
        var fileName = Regex.Replace(Path.GetFileName(oldPath), @"\.\d+\.\d+\.old\.cs$", ".cs");

        var diff = UnifiedDiff.Generate(oldLines, newLines);
        var currentHunk = new List<string>();
        int currentOldStart = 0, currentNewStart = 0;

        foreach (var line in diff)
        {
            if (line.StartsWith("@@"))
            {
                EmitAtom(atoms, currentHunk, currentOldStart, currentNewStart, oldLines, fileName);
                currentHunk.Clear();
                var m = HunkHeader.Match(line);
                if (m.Success)
                {
                    currentOldStart = int.Parse(m.Groups[1].Value);
                    currentNewStart = int.Parse(m.Groups[2].Value);
                }
            }
            else if (line.StartsWith("---") || line.StartsWith("+++"))
            {
                continue;
            }
            else
            {
                currentHunk.Add(line);
            }
        }

        EmitAtom(atoms, currentHunk, currentOldStart, currentNewStart, oldLines, fileName);
        return atoms;
    }

    static void EmitAtom(List<ChangeAtom> atoms, List<string> hunk, int oldStart, int newStart,
                          string[] oldLines, string fileName)
    {
        var minusLines = hunk.Where(l => l.StartsWith('-')).ToList();
        var plusLines = hunk.Where(l => l.StartsWith('+')).ToList();
        if (minusLines.Count == 0 && plusLines.Count == 0)
            return;

        var kind = (minusLines.Count > 0, plusLines.Count > 0) switch
        {
            (true, true) => ChangeKind.Modify,
            (false, true) => ChangeKind.Add,
            _ => ChangeKind.Remove
        };

        int lineHint = kind is ChangeKind.Remove or ChangeKind.Modify ? oldStart : newStart;

        // Detect control from surrounding context
        int ctxLo = Math.Max(0, oldStart - 10);
        int ctxHi = Math.Min(oldLines.Length, oldStart + minusLines.Count + 10);
        var surrounding = oldLines[ctxLo..ctxHi]
            .Concat(plusLines.Select(l => l[1..]))
            .ToArray();

        string controlHint = "unknown";
        foreach (var ctrl in MauiControls)
        {
            if (surrounding.Any(s => s.Contains(ctrl)))
            {
                controlHint = ctrl;
                break;
            }
        }

        // Build human-readable summary
        string summary;
        if (kind == ChangeKind.Modify && minusLines.Count == 1 && plusLines.Count == 1)
        {
            summary = $"Changed `{minusLines[0][1..].Trim()}` → `{plusLines[0][1..].Trim()}`";
        }
        else if (kind == ChangeKind.Add)
        {
            var added = string.Join("; ", plusLines.Take(3).Select(l => l[1..].Trim()));
            summary = $"Added {added}";
        }
        else if (kind == ChangeKind.Remove)
        {
            var removed = string.Join("; ", minusLines.Take(3).Select(l => l[1..].Trim()));
            summary = $"Removed {removed}";
        }
        else
        {
            var oldText = string.Join("; ", minusLines.Take(2).Select(l => l[1..].Trim()));
            var newText = string.Join("; ", plusLines.Take(2).Select(l => l[1..].Trim()));
            summary = $"Changed `{oldText}` → `{newText}`";
        }

        atoms.Add(new ChangeAtom
        {
            Kind = kind,
            ControlHint = controlHint,
            ChangeSummary = summary,
            File = fileName,
            LineHint = lineHint
        });
    }
}

public sealed class ChangeAtom
{
    public ChangeKind Kind { get; set; }
    public string ControlHint { get; set; } = "unknown";
    public string ChangeSummary { get; set; } = "";
    public string File { get; set; } = "";
    public int LineHint { get; set; }
}

public enum ChangeKind { Add, Remove, Modify }
