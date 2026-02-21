namespace HotReloadSentinel.Tests;

using HotReloadSentinel.Parsing;
using Xunit;

public class SessionLogParserTests
{
    [Fact]
    public void ParsesApplyCountFromSampleLog()
    {
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, """
            Found 1 potentially changed, 0 deleted document(s)
            Solution update 1.1 status: Ready
            Module update: capabilities=[Baseline]
            Found 1 potentially changed, 0 deleted document(s)
            Solution update 1.2 status: Ready
            """);

        var parser = new SessionLogParser();
        var markers = parser.Parse(tmpFile);

        Assert.Equal(2, markers.SaveCount);
        Assert.Equal(2, markers.ApplyCount);
        Assert.Equal(2, markers.ResultSuccessCount);
        Assert.True(markers.HasActivity);

        File.Delete(tmpFile);
    }

    [Fact]
    public void DetectsEnc1008()
    {
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, "Error ENC1008: rude edit detected\n");

        var parser = new SessionLogParser();
        var markers = parser.Parse(tmpFile);

        Assert.Equal(1, markers.Enc1008Count);
        File.Delete(tmpFile);
    }

    [Fact]
    public void IncrementalParsing()
    {
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, "Solution update 1.1 status: Ready\n");

        var parser = new SessionLogParser();
        var m1 = parser.Parse(tmpFile);
        Assert.Equal(1, m1.ApplyCount);

        // Append more
        File.AppendAllText(tmpFile, "Solution update 1.2 status: Ready\n");
        var m2 = parser.Parse(tmpFile);
        Assert.Equal(1, m2.ApplyCount); // Only new line

        File.Delete(tmpFile);
    }

    [Fact]
    public void DetectsXamlDocumentChange()
    {
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, "Document changed, added, or deleted: 'D:\\TestHotness\\MainPage.xaml'\n");

        var parser = new SessionLogParser();
        var markers = parser.Parse(tmpFile);

        Assert.Equal(1, markers.XamlChangeCount);
        Assert.Equal(0, markers.XamlCodeBehindChangeCount);
        Assert.Equal(0, markers.XamlApplyCount);
        File.Delete(tmpFile);
    }

    [Fact]
    public void DetectsXamlCodeBehindChangeSeparately()
    {
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, "Document changed, added, or deleted: 'D:\\TestHotness\\MainPage.xaml.cs'\n");

        var parser = new SessionLogParser();
        var markers = parser.Parse(tmpFile);

        Assert.Equal(1, markers.XamlCodeBehindChangeCount);
        Assert.Equal(0, markers.XamlChangeCount);
        File.Delete(tmpFile);
    }

    [Fact]
    public void DetectsXamlApplyLine()
    {
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, "XAML Hot Reload update applied to MainPage.xaml\n");

        var parser = new SessionLogParser();
        var markers = parser.Parse(tmpFile);

        Assert.Equal(1, markers.XamlApplyCount);
        File.Delete(tmpFile);
    }
}
