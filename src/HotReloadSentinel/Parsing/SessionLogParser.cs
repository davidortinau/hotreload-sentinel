namespace HotReloadSentinel.Parsing;

using System.Text.RegularExpressions;

/// <summary>
/// Incrementally parses Session.log to extract hot reload event counts.
/// </summary>
public sealed class SessionLogParser
{
    // Patterns from Session.log
    static readonly Regex SavePattern = new(@"Found \d+ potentially changed", RegexOptions.Compiled);
    static readonly Regex ApplyPattern = new(@"Solution update \d+\.\d+ status:\s*(Ready|ManagedModuleUpdate)", RegexOptions.Compiled);
    static readonly Regex XamlCodeBehindChangePattern = new(@"Document changed, added, or deleted:\s*'.*\.xaml\.cs'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex XamlChangePattern = new(@"Document changed, added, or deleted:\s*'.*\.xaml'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex XamlApplyPattern = new(@"(XAML.*Hot Reload|Hot Reload.*XAML).*(applied|apply|updated|update)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Enc1008Pattern = new(@"ENC1008", RegexOptions.Compiled);
    static readonly Regex NotAppliedPattern = new(@"Changes not applied.*project not built", RegexOptions.Compiled);
    static readonly Regex NotAppliedOtherTfmPattern = new(@"Changes not applied.*not built", RegexOptions.Compiled);
    static readonly Regex ResultSuccessPattern = new(@"Solution update \d+\.\d+ status:\s*Ready", RegexOptions.Compiled);
    static readonly Regex ResultBlockedPattern = new(@"Solution update \d+\.\d+ status:\s*Blocked", RegexOptions.Compiled);
    static readonly Regex ConnectionLostPattern = new(@"connection has been closed|Connection lost", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex LastSolutionUpdatePattern = new(@"Solution update \d+\.\d+ status:\s*\w+", RegexOptions.Compiled);

    public long Offset { get; set; }

    public LogMarkers Parse(string logPath)
    {
        var markers = new LogMarkers();
        if (!File.Exists(logPath))
            return markers;

        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (Offset > 0)
            fs.Seek(Offset, SeekOrigin.Begin);

        using var reader = new StreamReader(fs);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (SavePattern.IsMatch(line))
                markers.SaveCount++;

            if (ApplyPattern.IsMatch(line))
                markers.ApplyCount++;

            if (XamlCodeBehindChangePattern.IsMatch(line))
                markers.XamlCodeBehindChangeCount++;

            if (XamlChangePattern.IsMatch(line))
                markers.XamlChangeCount++;

            if (XamlApplyPattern.IsMatch(line))
                markers.XamlApplyCount++;

            if (Enc1008Pattern.IsMatch(line))
                markers.Enc1008Count++;

            if (ResultSuccessPattern.IsMatch(line))
                markers.ResultSuccessCount++;

            if (ResultBlockedPattern.IsMatch(line))
                markers.ResultFailureCount++;

            if (ConnectionLostPattern.IsMatch(line))
                markers.ConnectionLostCount++;

            // Distinguish not-applied on active TFM vs other TFMs
            if (NotAppliedPattern.IsMatch(line))
            {
                if (NotAppliedOtherTfmPattern.IsMatch(line))
                    markers.NotAppliedOtherTfmCount++;
                else
                    markers.NotAppliedCount++;
            }

            var lastMatch = LastSolutionUpdatePattern.Match(line);
            if (lastMatch.Success)
                markers.LastSolutionUpdate = lastMatch.Value;

            markers.HasActivity = true;
        }

        Offset = fs.Position;
        return markers;
    }
}

public sealed class LogMarkers
{
    public int SaveCount { get; set; }
    public int ApplyCount { get; set; }
    public int XamlCodeBehindChangeCount { get; set; }
    public int XamlChangeCount { get; set; }
    public int XamlApplyCount { get; set; }
    public int Enc1008Count { get; set; }
    public int ResultSuccessCount { get; set; }
    public int ResultFailureCount { get; set; }
    public int NotAppliedCount { get; set; }
    public int NotAppliedOtherTfmCount { get; set; }
    public int ConnectionLostCount { get; set; }
    public string? LastSolutionUpdate { get; set; }
    public bool HasActivity { get; set; }
}
