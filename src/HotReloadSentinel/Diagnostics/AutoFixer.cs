namespace HotReloadSentinel.Diagnostics;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Applies auto-fixes for diagnosed issues.
/// </summary>
public static class AutoFixer
{
    public static (bool success, string message) Fix(string checkId, string projectDir, IdeDetectionResult? ide = null)
    {
        ide ??= IdeDetector.Detect();
        return checkId switch
        {
            "bom_encoding" => FixBomEncoding(projectDir),
            "metadata_handler" => FixMetadataHandler(projectDir),
            "vscode_verbosity" => FixVsCodeSetting(projectDir, "csharp.debug.hotReloadVerbosity", "detailed", ide),
            "vscode_on_save" => FixVsCodeSetting(projectDir, "csharp.debug.hotReloadOnSave", true, ide),
            "vscode_settings" => FixVsCodeSettings(projectDir, ide),
            _ => (false, $"No auto-fix available for check: {checkId}")
        };
    }

    static (bool, string) FixBomEncoding(string projectDir)
    {
        var count = EncodingChecker.FixBom(projectDir);
        return (true, $"Added UTF-8 BOM to {count} file(s).");
    }

    static (bool, string) FixMetadataHandler(string projectDir)
    {
        var framework = ProjectAnalyzer.DetectFramework(projectDir);
        var templateName = framework switch
        {
            "MauiReactor" => "HotReloadService.MauiReactor.cs",
            "CSharpMarkup" => "HotReloadService.CSharpMarkup.cs",
            _ => "HotReloadService.Xaml.cs",
        };

        var templateDir = Path.Combine(AppContext.BaseDirectory, "templates");
        var templatePath = Path.Combine(templateDir, templateName);
        var destPath = Path.Combine(projectDir, "HotReloadService.cs");

        if (File.Exists(destPath))
            return (false, $"HotReloadService.cs already exists at {destPath}. Not overwriting.");

        if (File.Exists(templatePath))
        {
            File.Copy(templatePath, destPath);
            return (true, $"Scaffolded {templateName} → {destPath}");
        }

        // Fallback: generate inline
        var content = framework switch
        {
            "MauiReactor" => """
                [assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadService))]

                internal static class HotReloadService
                {
                    public static void ClearCache(Type[]? updatedTypes) { }
                    public static void UpdateApplication(Type[]? updatedTypes)
                    {
                        HotReloadTriggered?.Invoke();
                    }
                    public static event Action? HotReloadTriggered;
                }
                """,
            _ => """
                [assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadService))]

                internal static class HotReloadService
                {
                    public static void ClearCache(Type[]? updatedTypes) { }
                    public static void UpdateApplication(Type[]? updatedTypes)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            // Trigger UI refresh
                        });
                    }
                }
                """,
        };

        File.WriteAllText(destPath, content);
        return (true, $"Generated HotReloadService.cs for {framework} at {destPath}");
    }

    static (bool, string) FixVsCodeSetting(string projectDir, string key, object value, IdeDetectionResult ide)
    {
        if (!ide.HasVsCode)
            return (false, "VS Code was not detected. Skipping VS Code settings auto-fix. If you use Visual Studio, configure Hot Reload in Visual Studio settings.");

        var settingsPath = Path.Combine(projectDir, ".vscode", "settings.json");
        var dir = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(dir);

        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            try
            {
                settings = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject() ?? new JsonObject();
            }
            catch
            {
                settings = new JsonObject();
            }
        }
        else
        {
            settings = new JsonObject();
        }

        settings[key] = value switch
        {
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            _ => JsonValue.Create(value.ToString())
        };

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return (true, $"Set {key} in {settingsPath}");
    }

    static (bool, string) FixVsCodeSettings(string projectDir, IdeDetectionResult ide)
    {
        if (!ide.HasVsCode)
            return (false, "VS Code was not detected. Skipping VS Code settings auto-fix. If you use Visual Studio, configure Hot Reload in Visual Studio settings.");

        var settingsPath = Path.Combine(projectDir, ".vscode", "settings.json");
        Directory.CreateDirectory(Path.Combine(projectDir, ".vscode"));

        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            try
            {
                settings = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject() ?? new JsonObject();
            }
            catch
            {
                settings = new JsonObject();
            }
        }
        else
        {
            settings = new JsonObject();
        }

        settings["csharp.debug.hotReloadOnSave"] = true;
        settings["csharp.debug.hotReloadVerbosity"] = "detailed";

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return (true, $"Created/updated {settingsPath} with hot reload settings.");
    }
}
