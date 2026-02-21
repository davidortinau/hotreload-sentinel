namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Represents a single diagnostic check result.
/// </summary>
public sealed class DiagnosticCheck
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public CheckStatus Status { get; set; }
    public string? Message { get; set; }
    public bool AutoFixable { get; set; }
    public string? FixCommand { get; set; }
    public List<string>? AffectedFiles { get; set; }
}

public enum CheckStatus { Pass, Warn, Fail }
