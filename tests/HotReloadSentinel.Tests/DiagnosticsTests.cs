namespace HotReloadSentinel.Tests;

using HotReloadSentinel.Diagnostics;
using Xunit;

public class DiagnosticsTests
{
    [Fact]
    public void EnvironmentChecker_ReturnsChecks()
    {
        var checks = EnvironmentChecker.Run();
        Assert.True(checks.Count >= 3);
        Assert.Contains(checks, c => c.Id == "enc_logdir");
        Assert.Contains(checks, c => c.Id == "xaml_logging");
        Assert.Contains(checks, c => c.Id == "session_log");
    }

    [Fact]
    public void EncodingChecker_DetectsMissingBom()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"hr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // File without BOM
            File.WriteAllText(Path.Combine(tmpDir, "NoBom.cs"), "using System;\n");

            // File with BOM
            var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF };
            var content = System.Text.Encoding.UTF8.GetBytes("using System;\n");
            var withBom = new byte[bomBytes.Length + content.Length];
            bomBytes.CopyTo(withBom, 0);
            content.CopyTo(withBom, bomBytes.Length);
            File.WriteAllBytes(Path.Combine(tmpDir, "WithBom.cs"), withBom);

            var check = EncodingChecker.Run(tmpDir);
            Assert.Equal("bom_encoding", check.Id);
            Assert.Equal(CheckStatus.Warn, check.Status);
            Assert.Contains("1 of 2", check.Message);
            Assert.NotNull(check.AffectedFiles);
            Assert.Contains("NoBom.cs", check.AffectedFiles!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void EncodingChecker_FixBom_AddsToMissingFiles()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"hr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "Test.cs"), "class Foo {}\n");
            var fixed_count = EncodingChecker.FixBom(tmpDir);
            Assert.Equal(1, fixed_count);

            // Verify BOM was added
            var bytes = File.ReadAllBytes(Path.Combine(tmpDir, "Test.cs"));
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ProjectAnalyzer_DetectsMauiReactor()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"hr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "Test.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Reactor.Maui" Version="4.0.0" />
                  </ItemGroup>
                </Project>
                """);

            var framework = ProjectAnalyzer.DetectFramework(tmpDir, File.ReadAllText(Path.Combine(tmpDir, "Test.csproj")));
            Assert.Equal("MauiReactor", framework);

            var checks = ProjectAnalyzer.Run(tmpDir);
            Assert.Contains(checks, c => c.Id == "ui_framework" && c.Message!.Contains("MauiReactor"));
            Assert.Contains(checks, c => c.Id == "metadata_handler" && c.Status == CheckStatus.Fail);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void IdeSettingsChecker_DetectsVsCodeSettings()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"hr-test-{Guid.NewGuid():N}");
        var vscodeDir = Path.Combine(tmpDir, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        try
        {
            File.WriteAllText(Path.Combine(vscodeDir, "settings.json"), """
                {
                  "csharp.debug.hotReloadVerbosity": "detailed",
                  "csharp.debug.hotReloadOnSave": true
                }
                """);

            var checks = IdeSettingsChecker.Run(tmpDir);
            Assert.Contains(checks, c => c.Id == "vscode_verbosity" && c.Status == CheckStatus.Pass);
            Assert.Contains(checks, c => c.Id == "vscode_on_save" && c.Status == CheckStatus.Pass);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void AutoFixer_FixesVsCodeSettings()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"hr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var (success, _) = AutoFixer.Fix("vscode_settings", tmpDir);
            Assert.True(success);
            Assert.True(File.Exists(Path.Combine(tmpDir, ".vscode", "settings.json")));

            var content = File.ReadAllText(Path.Combine(tmpDir, ".vscode", "settings.json"));
            Assert.Contains("hotReloadVerbosity", content);
            Assert.Contains("hotReloadOnSave", content);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
