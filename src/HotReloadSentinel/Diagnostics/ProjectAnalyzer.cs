namespace HotReloadSentinel.Diagnostics;

using System.Text.RegularExpressions;

/// <summary>
/// Detects UI framework, target framework, and MetadataUpdateHandler presence.
/// </summary>
public static class ProjectAnalyzer
{
    public static List<DiagnosticCheck> Run(string projectDir)
    {
        var checks = new List<DiagnosticCheck>();

        // Find csproj
        var csprojFiles = Directory.Exists(projectDir)
            ? Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly)
            : [];

        if (csprojFiles.Length == 0)
        {
            checks.Add(new DiagnosticCheck
            {
                Id = "project_found",
                Name = "Project File",
                Status = CheckStatus.Fail,
                Message = $"No .csproj found in {projectDir}",
                AutoFixable = false,
            });
            return checks;
        }

        var csprojContent = File.ReadAllText(csprojFiles[0]);

        // Detect UI framework
        var framework = DetectFramework(projectDir, csprojContent);
        checks.Add(new DiagnosticCheck
        {
            Id = "ui_framework",
            Name = "UI Framework",
            Status = CheckStatus.Pass,
            Message = $"Detected: {framework}",
            AutoFixable = false,
        });

        // Detect target framework
        var tfmMatch = Regex.Match(csprojContent, @"<TargetFramework[s]?>(.*?)</TargetFramework[s]?>");
        if (tfmMatch.Success)
        {
            checks.Add(new DiagnosticCheck
            {
                Id = "target_framework",
                Name = "Target Framework",
                Status = CheckStatus.Pass,
                Message = tfmMatch.Groups[1].Value,
                AutoFixable = false,
            });
        }

        // Check Debug configuration
        var debugConfig = Regex.IsMatch(csprojContent, @"Condition.*Debug", RegexOptions.IgnoreCase);
        checks.Add(new DiagnosticCheck
        {
            Id = "debug_config",
            Name = "Debug Configuration",
            Status = CheckStatus.Pass,
            Message = "Project has Debug configuration.",
            AutoFixable = false,
        });

        // Check MetadataUpdateHandler
        var handlerCheck = CheckMetadataUpdateHandler(projectDir, framework);
        checks.Add(handlerCheck);

        // For MauiReactor: check RuntimeHostConfigurationOption
        if (framework == "MauiReactor")
        {
            var hasRhco = csprojContent.Contains("MauiReactor.HotReload");
            checks.Add(new DiagnosticCheck
            {
                Id = "reactor_hot_reload_option",
                Name = "MauiReactor.HotReload RuntimeHostConfigurationOption",
                Status = hasRhco ? CheckStatus.Pass : CheckStatus.Warn,
                Message = hasRhco
                    ? "RuntimeHostConfigurationOption for MauiReactor.HotReload found in csproj."
                    : "MauiReactor.HotReload RuntimeHostConfigurationOption not found in csproj. Hot reload may not trigger UI refresh.",
                AutoFixable = false,
            });
        }

        return checks;
    }

    public static string DetectFramework(string projectDir, string? csprojContent = null)
    {
        csprojContent ??= "";

        // Check for MauiReactor
        if (csprojContent.Contains("Reactor.Maui") || csprojContent.Contains("ReactorUI.Maui"))
            return "MauiReactor";

        // Check for C# Markup
        if (csprojContent.Contains("CommunityToolkit.Maui.Markup"))
            return "CSharpMarkup";

        // Check for Blazor Hybrid
        if (csprojContent.Contains("BlazorWebView") || Directory.Exists(Path.Combine(projectDir, "wwwroot")))
            return "BlazorHybrid";

        // Check source files
        if (Directory.Exists(projectDir))
        {
            var csFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Take(50);

            foreach (var file in csFiles)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains("using MauiReactor"))
                        return "MauiReactor";
                    if (content.Contains("ICommunityToolkitHotReloadHandler"))
                        return "CSharpMarkup";
                }
                catch (IOException) { }
            }
        }

        return "XAML";
    }

    static DiagnosticCheck CheckMetadataUpdateHandler(string projectDir, string framework)
    {
        if (!Directory.Exists(projectDir))
        {
            return new DiagnosticCheck
            {
                Id = "metadata_handler",
                Name = "MetadataUpdateHandler",
                Status = CheckStatus.Warn,
                Message = "Could not check — project directory not found.",
                AutoFixable = false,
            };
        }

        var csFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        bool found = false;
        foreach (var file in csFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (content.Contains("MetadataUpdateHandler"))
                {
                    found = true;
                    break;
                }
            }
            catch (IOException) { }
        }

        // XAML apps usually don't need a custom handler
        var required = framework is "MauiReactor" or "CSharpMarkup";

        if (found)
        {
            return new DiagnosticCheck
            {
                Id = "metadata_handler",
                Name = "MetadataUpdateHandler",
                Status = CheckStatus.Pass,
                Message = "MetadataUpdateHandler found in project.",
                AutoFixable = false,
            };
        }

        return new DiagnosticCheck
        {
            Id = "metadata_handler",
            Name = "MetadataUpdateHandler",
            Status = required ? CheckStatus.Fail : CheckStatus.Pass,
            Message = required
                ? $"{framework} requires a MetadataUpdateHandler for Hot Reload UI refresh. None found."
                : "No custom MetadataUpdateHandler (not required for XAML apps).",
            AutoFixable = required,
            FixCommand = required ? $"hotreload-sentinel fix --check metadata_handler" : null,
        };
    }
}
