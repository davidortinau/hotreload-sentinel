namespace HotReloadSentinel.Parsing;

using System.Text.RegularExpressions;

/// <summary>
/// Finds and diffs old.cs/new.cs artifact pairs from the hot reload log directory.
/// </summary>
public static class ArtifactDiffer
{
    /// <summary>
    /// Find all artifact pairs newer than a given mtime (or all when null), sorted by mtime ascending.
    /// </summary>
    public static List<ArtifactPair> FindAllSince(string hotReloadDir, double? afterMtime = null)
    {
        var results = new List<ArtifactPair>();
        if (!Directory.Exists(hotReloadDir))
            return results;

        var threshold = afterMtime ?? 0;
        foreach (var oldFile in Directory.EnumerateFiles(hotReloadDir, "*.old.cs", SearchOption.AllDirectories))
        {
            var newFile = oldFile.Replace(".old.cs", ".new.cs");
            if (!File.Exists(newFile))
                continue;

            var mtime = new FileInfo(oldFile).LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
            if (mtime <= threshold)
                continue;

            results.Add(new ArtifactPair
            {
                OldPath = oldFile,
                NewPath = newFile,
                Name = Regex.Replace(Path.GetFileName(oldFile), @"\.old\.cs$", ""),
                SourceFile = Path.GetRelativePath(hotReloadDir, oldFile),
                Mtime = mtime,
            });
        }

        results.Sort((a, b) => a.Mtime.CompareTo(b.Mtime));
        return results;
    }

    /// <summary>
    /// Find the most recent artifact pair, optionally newer than a given mtime.
    /// </summary>
    public static ArtifactPair? FindLatest(string hotReloadDir, double? afterMtime = null)
    {
        return FindAllSince(hotReloadDir, afterMtime).LastOrDefault();
    }

    /// <summary>
    /// Generate a unified diff preview (max N lines) from an artifact pair.
    /// </summary>
    public static string GenerateDiffPreview(string oldPath, string newPath, int maxLines = 10)
    {
        if (!File.Exists(oldPath) || !File.Exists(newPath))
            return "";

        var oldLines = File.ReadAllLines(oldPath);
        var newLines = File.ReadAllLines(newPath);
        var diff = UnifiedDiff.Generate(oldLines, newLines);

        var changed = diff.Where(l => l.StartsWith('+') || l.StartsWith('-')).Take(maxLines);
        return string.Join(" | ", changed);
    }
}

public sealed class ArtifactPair
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public double Mtime { get; set; }
}
