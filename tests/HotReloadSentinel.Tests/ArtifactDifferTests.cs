namespace HotReloadSentinel.Tests;

using HotReloadSentinel.Parsing;
using Xunit;

public class ArtifactDifferTests
{
    [Fact]
    public void FindAllSince_FiltersAndSortsByMtime()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hr-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var old1 = Path.Combine(dir, "MainPage.xaml.1.1.old.cs");
            var new1 = Path.Combine(dir, "MainPage.xaml.1.1.new.cs");
            var old2 = Path.Combine(dir, "MainPage.xaml.cs.2.2.old.cs");
            var new2 = Path.Combine(dir, "MainPage.xaml.cs.2.2.new.cs");

            File.WriteAllText(old1, "old1");
            File.WriteAllText(new1, "new1");
            File.WriteAllText(old2, "old2");
            File.WriteAllText(new2, "new2");

            var t1 = DateTime.UtcNow.AddSeconds(-10);
            var t2 = DateTime.UtcNow.AddSeconds(-5);
            File.SetLastWriteTimeUtc(old1, t1);
            File.SetLastWriteTimeUtc(new1, t1);
            File.SetLastWriteTimeUtc(old2, t2);
            File.SetLastWriteTimeUtc(new2, t2);

            var all = ArtifactDiffer.FindAllSince(dir);
            Assert.Equal(2, all.Count);
            Assert.True(all[0].Mtime <= all[1].Mtime);

            var since = t1.Subtract(DateTime.UnixEpoch).TotalSeconds;
            var filtered = ArtifactDiffer.FindAllSince(dir, since);
            Assert.Single(filtered);
            Assert.Contains("MainPage.xaml.cs.2.2.old.cs", filtered[0].SourceFile);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
