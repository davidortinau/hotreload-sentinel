namespace HotReloadSentinel.Parsing;

using System.Text.RegularExpressions;

/// <summary>
/// Finds and diffs old.cs/new.cs artifact pairs from the hot reload log directory.
/// </summary>
public static class ArtifactDiffer
{
    static readonly Regex ArtifactNamePattern = new(@"\.(\d+)\.(\d+)\.old\.cs$", RegexOptions.Compiled);

    /// <summary>
    /// Find the most recent artifact pair, optionally newer than a given mtime.
    /// </summary>
    public static ArtifactPair? FindLatest(string hotReloadDir, double? afterMtime = null)
    {
        if (!Directory.Exists(hotReloadDir))
            return null;

        ArtifactPair? best = null;
        double bestMtime = afterMtime ?? 0;

        foreach (var oldFile in Directory.EnumerateFiles(hotReloadDir, "*.old.cs", SearchOption.AllDirectories))
        {
            var newFile = oldFile.Replace(".old.cs", ".new.cs");
            if (!File.Exists(newFile))
                continue;

            var mtime = new FileInfo(oldFile).LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
            if (mtime > bestMtime)
            {
                bestMtime = mtime;
                var name = ArtifactNamePattern.Replace(Path.GetFileName(oldFile), ".$1.$2");
                best = new ArtifactPair
                {
                    OldPath = oldFile,
                    NewPath = newFile,
                    Name = Regex.Replace(Path.GetFileName(oldFile), @"\.old\.cs$", ""),
                    SourceFile = Path.GetRelativePath(hotReloadDir, oldFile),
                    Mtime = mtime,
                };
            }
        }

        return best;
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
