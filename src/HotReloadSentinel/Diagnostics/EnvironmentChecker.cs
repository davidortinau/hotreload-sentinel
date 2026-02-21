namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Checks environment variables required for hot reload diagnostics.
/// </summary>
public static class EnvironmentChecker
{
    public static List<DiagnosticCheck> Run()
    {
        var checks = new List<DiagnosticCheck>();

        // ENC LogDir
        var encLogDir = GetConfiguredEnv("Microsoft_CodeAnalysis_EditAndContinue_LogDir");
        checks.Add(new DiagnosticCheck
        {
            Id = "enc_logdir",
            Name = "Edit and Continue Log Directory",
            Status = string.IsNullOrEmpty(encLogDir) ? CheckStatus.Fail : CheckStatus.Pass,
            Message = string.IsNullOrEmpty(encLogDir)
                ? "Microsoft_CodeAnalysis_EditAndContinue_LogDir not set. Session.log and artifact diffs will not be generated."
                : $"Set to: {encLogDir}",
            AutoFixable = true,
            FixCommand = OperatingSystem.IsWindows()
                ? "setx Microsoft_CodeAnalysis_EditAndContinue_LogDir \"%temp%\\HotReloadLog\""
                : "echo 'export Microsoft_CodeAnalysis_EditAndContinue_LogDir=/tmp/HotReloadLog' >> ~/.zshrc && source ~/.zshrc",
        });

        // XAML Hot Reload logging
        var xamlLog = GetConfiguredEnv("HOTRELOAD_XAML_LOG_MESSAGES");
        checks.Add(new DiagnosticCheck
        {
            Id = "xaml_logging",
            Name = "XAML Hot Reload Logging",
            Status = string.IsNullOrEmpty(xamlLog) ? CheckStatus.Warn : CheckStatus.Pass,
            Message = string.IsNullOrEmpty(xamlLog)
                ? "HOTRELOAD_XAML_LOG_MESSAGES not set. VS/VS Code XAML Hot Reload events may not appear in Session.log."
                : $"Set to: {xamlLog}",
            AutoFixable = true,
            FixCommand = OperatingSystem.IsWindows()
                ? "setx HOTRELOAD_XAML_LOG_MESSAGES 1"
                : "echo 'export HOTRELOAD_XAML_LOG_MESSAGES=1' >> ~/.zshrc && source ~/.zshrc",
        });

        // Session.log existence
        var tmpDir = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        var sessionLog = Path.Combine(tmpDir, "HotReloadLog", "Session.log");
        var sessionLogExists = File.Exists(sessionLog);
        var sessionLogRecent = false;
        if (sessionLogExists)
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(sessionLog);
            sessionLogRecent = age.TotalMinutes < 30;
        }

        checks.Add(new DiagnosticCheck
        {
            Id = "session_log",
            Name = "Session.log Exists and Recent",
            Status = sessionLogRecent ? CheckStatus.Pass : sessionLogExists ? CheckStatus.Warn : CheckStatus.Fail,
            Message = !sessionLogExists
                ? "Session.log not found. Ensure ENC LogDir is set and a debug session has been run."
                : sessionLogRecent
                    ? $"Session.log found and updated within last 30 minutes."
                    : $"Session.log found but stale (last modified: {File.GetLastWriteTimeUtc(sessionLog):u}).",
            AutoFixable = false,
        });

        return checks;
    }

    static string? GetConfiguredEnv(string name)
    {
        var process = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(process))
            return process;

        if (!OperatingSystem.IsWindows())
            return null;

        var user = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(user))
            return user;

        var machine = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        return string.IsNullOrEmpty(machine) ? null : machine;
    }
}
