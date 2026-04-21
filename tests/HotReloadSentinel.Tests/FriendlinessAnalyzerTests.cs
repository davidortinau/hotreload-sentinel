namespace HotReloadSentinel.Tests;

using HotReloadSentinel.Diagnostics;
using Xunit;

public class FriendlinessAnalyzerTests : IDisposable
{
    readonly string _tempDir;

    public FriendlinessAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hrs-friendliness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    string Write(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // Helper to find the 1-based line number of a marker comment.
    static int LineOf(string path, string marker)
    {
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(marker)) return i + 1;
        throw new InvalidOperationException($"Marker '{marker}' not found in {path}");
    }

    [Fact]
    public void Classifies_field_initializer_as_rude_edit()
    {
        var code = """
            namespace T;
            public class Popup
            {
                private object _entry = new
                {
                    Placeholder = "Ethiopian" /* MARKER */
                };
            }
            """;
        var path = Write("Popup.cs", code);
        var result = HotReloadFriendlinessAnalyzer.Classify(path, LineOf(path, "MARKER"));

        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.FieldOrPropertyInitializer, result.Classification);
        Assert.Equal("Popup", result.EnclosingTypeName);
        Assert.Contains(result.Advice, a => a.Contains("rude-edit"));
        Assert.Contains(result.Advice, a => a.Contains("MetadataUpdateHandler"));
    }

    [Fact]
    public void Classifies_constructor_body()
    {
        var code = """
            namespace T;
            public class Page
            {
                public Page()
                {
                    var x = 1; /* MARKER */
                }
            }
            """;
        var path = Write("Page.cs", code);
        var result = HotReloadFriendlinessAnalyzer.Classify(path, LineOf(path, "MARKER"));

        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.ConstructorBody, result.Classification);
        Assert.Equal("Page", result.EnclosingTypeName);
        Assert.Equal("Page", result.EnclosingMemberName);
    }

    [Fact]
    public void Classifies_lifecycle_hook()
    {
        var code = """
            namespace T;
            public class MyPage
            {
                protected override void OnAppearing()
                {
                    var n = 1; /* MARKER */
                }
            }
            """;
        var path = Write("MyPage.cs", code);
        var result = HotReloadFriendlinessAnalyzer.Classify(path, LineOf(path, "MARKER"));

        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.LifecycleHook, result.Classification);
        Assert.Equal("OnAppearing", result.EnclosingMemberName);
    }

    [Fact]
    public void Classifies_named_render_method()
    {
        var code = """
            namespace T;
            public class C
            {
                public override object Render()
                {
                    return new { Text = "Hi" }; /* MARKER */
                }
            }
            """;
        var path = Write("C.cs", code);
        var result = HotReloadFriendlinessAnalyzer.Classify(path, LineOf(path, "MARKER"));

        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.NamedRenderMethod, result.Classification);
        Assert.Equal("Render", result.EnclosingMemberName);
        Assert.Contains(result.Advice, a => a.Contains("re-render"));
    }

    [Fact]
    public void Classifies_other_method_body()
    {
        var code = """
            namespace T;
            public class S
            {
                public int DoWork()
                {
                    return 42; /* MARKER */
                }
            }
            """;
        var path = Write("S.cs", code);
        var result = HotReloadFriendlinessAnalyzer.Classify(path, LineOf(path, "MARKER"));

        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.OtherMethodBody, result.Classification);
        Assert.Equal("DoWork", result.EnclosingMemberName);
    }

    [Fact]
    public void Returns_unknown_for_missing_file()
    {
        var result = HotReloadFriendlinessAnalyzer.Classify("/nope/missing.cs", 1);
        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.Unknown, result.Classification);
    }

    [Fact]
    public void Returns_unknown_for_out_of_range_line()
    {
        var path = Write("tiny.cs", "namespace T;\n");
        var result = HotReloadFriendlinessAnalyzer.Classify(path, 99);
        Assert.Equal(HotReloadFriendlinessAnalyzer.Classification.Unknown, result.Classification);
    }

    [Fact]
    public void Detects_assembly_level_metadata_update_handler()
    {
        Write("Program.cs", """
            namespace T;
            public class Program {}
            """);
        Write("HotReloadHook.cs", """
            [assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(T.Hook))]
            namespace T;
            internal static class Hook
            {
                public static void UpdateApplication(System.Type[]? _) { }
            }
            """);

        Assert.True(HotReloadFriendlinessAnalyzer.ProjectHasMetadataUpdateHandler(_tempDir));
    }

    [Fact]
    public void Reports_no_handler_when_only_method_attribute_present()
    {
        // A non-assembly attribute on a method should not count.
        Write("Other.cs", """
            namespace T;
            public class C
            {
                [System.Reflection.Metadata.MetadataUpdateHandler(typeof(C))]
                public void M() { }
            }
            """);

        Assert.False(HotReloadFriendlinessAnalyzer.ProjectHasMetadataUpdateHandler(_tempDir));
    }
}
