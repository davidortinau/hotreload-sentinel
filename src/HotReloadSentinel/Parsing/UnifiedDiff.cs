namespace HotReloadSentinel.Parsing;

/// <summary>
/// Minimal unified diff generator (no external dependencies).
/// </summary>
public static class UnifiedDiff
{
    public static List<string> Generate(string[] oldLines, string[] newLines, int context = 3)
    {
        var result = new List<string>();
        var lcs = ComputeLcs(oldLines, newLines);

        // Build edit script
        var edits = new List<(char op, int oldIdx, int newIdx)>();
        int i = oldLines.Length, j = newLines.Length;
        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && lcs[i, j] == lcs[i - 1, j - 1] + 1 && oldLines[i - 1] == newLines[j - 1])
            {
                edits.Add((' ', i - 1, j - 1));
                i--; j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                edits.Add(('+', -1, j - 1));
                j--;
            }
            else
            {
                edits.Add(('-', i - 1, -1));
                i--;
            }
        }

        edits.Reverse();

        // Group into hunks with context
        var hunks = new List<(int start, int end)>();
        int hunkStart = -1;
        for (int e = 0; e < edits.Count; e++)
        {
            if (edits[e].op != ' ')
            {
                int lo = Math.Max(0, e - context);
                int hi = Math.Min(edits.Count - 1, e + context);
                if (hunkStart < 0)
                    hunkStart = lo;

                // Extend current hunk
                if (hunks.Count > 0 && lo <= hunks[^1].end + 1)
                    hunks[^1] = (hunks[^1].start, hi);
                else
                {
                    if (hunkStart >= 0 && hunks.Count > 0)
                        hunks[^1] = (hunks[^1].start, Math.Max(hunks[^1].end, hunkStart - 1));
                    hunks.Add((lo, hi));
                }
                hunkStart = -1;
            }
        }

        foreach (var (start, end) in hunks)
        {
            int oldStart = 0, newStart = 0, oldCount = 0, newCount = 0;
            bool first = true;
            var lines = new List<string>();

            for (int e = start; e <= end && e < edits.Count; e++)
            {
                var (op, oi, ni) = edits[e];
                if (first)
                {
                    oldStart = op == '+' ? 0 : oi + 1;
                    newStart = op == '-' ? 0 : ni + 1;
                    first = false;
                }

                switch (op)
                {
                    case ' ':
                        lines.Add($" {oldLines[oi]}");
                        oldCount++; newCount++;
                        break;
                    case '-':
                        lines.Add($"-{oldLines[oi]}");
                        oldCount++;
                        break;
                    case '+':
                        lines.Add($"+{newLines[ni]}");
                        newCount++;
                        break;
                }
            }

            result.Add($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");
            result.AddRange(lines);
        }

        return result;
    }

    static int[,] ComputeLcs(string[] a, string[] b)
    {
        int m = a.Length, n = b.Length;
        var dp = new int[m + 1, n + 1];
        for (int x = 1; x <= m; x++)
            for (int y = 1; y <= n; y++)
                dp[x, y] = a[x - 1] == b[y - 1] ? dp[x - 1, y - 1] + 1 : Math.Max(dp[x - 1, y], dp[x, y - 1]);
        return dp;
    }
}
