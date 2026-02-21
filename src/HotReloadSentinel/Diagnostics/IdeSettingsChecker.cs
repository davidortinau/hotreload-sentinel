namespace HotReloadSentinel.Diagnostics;

using System.Text.Json;

/// <summary>
/// Checks IDE settings for hot reload configuration.
/// </summary>
public static class IdeSettingsChecker
{
    public static List<DiagnosticCheck> Run(string projectDir, IdeDetectionResult? ide = null)
    {
        ide ??= IdeDetector.Detect();
        var checks = new List<DiagnosticCheck>();
        var hasVsCode = ide.HasVsCode;
        var hasVisualStudio = ide.HasVisualStudio;

        if (!hasVsCode)
        {
            checks.Add(new DiagnosticCheck
            {
                Id = hasVisualStudio ? "ide_visual_studio" : "ide_supported",
                Name = "IDE Detection",
                Status = hasVisualStudio ? CheckStatus.Pass : CheckStatus.Warn,
                Message = hasVisualStudio
                    ? "Visual Studio detected. VS Code settings checks are skipped."
                    : "No supported IDE detected (VS Code or Visual Studio). Continuing without IDE-specific checks.",
                AutoFixable = false,
            });
            return checks;
        }

        if (hasVisualStudio)
        {
            checks.Add(new DiagnosticCheck
            {
                Id = "ide_visual_studio",
                Name = "Visual Studio Guidance",
                Status = CheckStatus.Pass,
                Message = "Visual Studio detected. Verify Hot Reload is enabled in Visual Studio debug settings.",
                AutoFixable = false,
            });
        }

        // Check VS Code settings
        var vscodeSettings = Path.Combine(projectDir, ".vscode", "settings.json");
        if (File.Exists(vscodeSettings))
        {
            try
            {
                var content = File.ReadAllText(vscodeSettings);
                var json = JsonDocument.Parse(content);

                // hotReloadVerbosity
                var hasVerbosity = json.RootElement.TryGetProperty("csharp.debug.hotReloadVerbosity", out var verbosity);
                var isDetailed = hasVerbosity && verbosity.GetString() is "detailed" or "diagnostic";
                checks.Add(new DiagnosticCheck
                {
                    Id = "vscode_verbosity",
                    Name = "VS Code Hot Reload Verbosity",
                    Status = isDetailed ? CheckStatus.Pass : CheckStatus.Warn,
                    Message = isDetailed
                        ? $"Hot reload verbosity: {verbosity.GetString()}"
                        : "csharp.debug.hotReloadVerbosity not set to 'detailed'. Set it for better diagnostics.",
                    AutoFixable = true,
                    FixCommand = "hotreload-sentinel fix --check vscode_verbosity",
                });

                // hotReloadOnSave
                var hasOnSave = json.RootElement.TryGetProperty("csharp.debug.hotReloadOnSave", out var onSave);
                var onSaveEnabled = hasOnSave && onSave.GetBoolean();
                checks.Add(new DiagnosticCheck
                {
                    Id = "vscode_on_save",
                    Name = "VS Code Hot Reload On Save",
                    Status = onSaveEnabled ? CheckStatus.Pass : CheckStatus.Warn,
                    Message = onSaveEnabled
                        ? "Hot reload on save: enabled"
                        : "csharp.debug.hotReloadOnSave not enabled. Changes require manual hot reload trigger.",
                    AutoFixable = true,
                    FixCommand = "hotreload-sentinel fix --check vscode_on_save",
                });

                // ENC logDir in launch config
                var hasLogDir = json.RootElement.TryGetProperty("csharp.debug.editAndContinue.logDir", out _);
                if (hasLogDir)
                {
                    checks.Add(new DiagnosticCheck
                    {
                        Id = "vscode_enc_logdir",
                        Name = "VS Code ENC LogDir Setting",
                        Status = CheckStatus.Pass,
                        Message = "editAndContinue.logDir configured in VS Code settings.",
                        AutoFixable = false,
                    });
                }
            }
            catch (JsonException)
            {
                checks.Add(new DiagnosticCheck
                {
                    Id = "vscode_settings",
                    Name = "VS Code Settings",
                    Status = CheckStatus.Warn,
                    Message = "Could not parse .vscode/settings.json",
                    AutoFixable = false,
                });
            }
        }
        else
        {
            checks.Add(new DiagnosticCheck
            {
                Id = "vscode_settings",
                Name = "VS Code Settings",
                Status = hasVisualStudio ? CheckStatus.Pass : CheckStatus.Warn,
                Message = hasVisualStudio
                    ? "No .vscode/settings.json found. This is informational because Visual Studio is also detected."
                    : "No .vscode/settings.json found. VS Code hot reload settings not configured.",
                AutoFixable = !hasVisualStudio,
                FixCommand = hasVisualStudio ? null : "hotreload-sentinel fix --check vscode_settings",
            });
        }

        return checks;
    }
}
