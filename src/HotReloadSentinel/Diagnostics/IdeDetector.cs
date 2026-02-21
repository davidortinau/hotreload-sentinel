namespace HotReloadSentinel.Diagnostics;

using System.Diagnostics;

/// <summary>
/// Detects supported IDEs for platform-specific diagnostics.
/// </summary>
public static class IdeDetector
{
    public static IdeDetectionResult Detect()
    {
        return new IdeDetectionResult(
            HasVsCode: IsVsCodeInstalled(),
            HasVisualStudio: IsVisualStudioInstalled());
    }

    static bool IsVsCodeInstalled()
    {
        if (FindOnPath("code", "code.cmd", "code.exe") is not null)
            return true;

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
                Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe"),
            };

            return candidates.Any(File.Exists);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Directory.Exists("/Applications/Visual Studio Code.app");
        }

        return false;
    }

    static bool IsVisualStudioInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vsWhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vsWhere))
        {
            try
            {
                var psi = new ProcessStartInfo(vsWhere, "-latest -products * -property installationPath")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd()?.Trim();
                if (proc is not null)
                    proc.WaitForExit(2000);
                if (!string.IsNullOrWhiteSpace(output))
                    return true;
            }
            catch
            {
                // Fall through to path-based checks.
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var roots = new[]
        {
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2026"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", "2026"),
        };

        return roots.Any(Directory.Exists);
    }

    static string? FindOnPath(params string[] commands)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            foreach (var cmd in commands)
            {
                var full = Path.Combine(p.Trim(), cmd);
                if (File.Exists(full))
                    return full;
            }
        }

        return null;
    }
}

public sealed record IdeDetectionResult(bool HasVsCode, bool HasVisualStudio);
