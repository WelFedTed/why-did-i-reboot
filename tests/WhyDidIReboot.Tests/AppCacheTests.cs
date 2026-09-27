using System.IO;
using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class AppCacheTests
{
    [Fact]
    public void Measure_and_clear_remove_every_cached_file_but_keep_the_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "wdir-cache-" + Guid.NewGuid().ToString("N"));
        var analysis = Path.Combine(root, "analysis");
        var updates = Path.Combine(root, "update");
        try
        {
            Directory.CreateDirectory(Path.Combine(updates, "staged"));
            File.WriteAllText(Path.Combine(analysis + "-missing-is-fine.txt"), "");   // not in a cache folder: untouched
            Directory.CreateDirectory(analysis);
            File.WriteAllText(Path.Combine(analysis, "a.1234abcd.analyze.txt"), new string('x', 1000));
            File.WriteAllText(Path.Combine(updates, "WhyDidIReboot.exe"), new string('x', 2048));
            File.WriteAllText(Path.Combine(updates, "staged", "WhyDidIReboot.dll"), "dll");
            var folders = new[] { analysis, updates, Path.Combine(root, "does-not-exist") };

            Assert.Equal(new AppCache.Summary(3, 3051), AppCache.Measure(folders));

            var (removed, skipped) = AppCache.Clear(folders);
            Assert.Equal(new AppCache.Summary(3, 3051), removed);
            Assert.Equal(0, skipped);
            Assert.Equal(AppCache.Summary.Empty, AppCache.Measure(folders));
            Assert.True(Directory.Exists(analysis));
            Assert.True(Directory.Exists(updates));
            Assert.False(Directory.Exists(Path.Combine(updates, "staged")));
            Assert.True(File.Exists(analysis + "-missing-is-fine.txt"));
        }
        finally
        {
            File.Delete(analysis + "-missing-is-fine.txt");
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_file_held_open_is_kept_and_counted_as_skipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wdir-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var busy = Path.Combine(dir, "busy.analyze.txt");
            File.WriteAllText(busy, "open in WinDbg");
            File.WriteAllText(Path.Combine(dir, "done.analyze.txt"), "done");
            using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var (removed, skipped) = AppCache.Clear(new[] { dir });
                Assert.Equal(1, removed.Files);
                Assert.Equal(1, skipped);
            }
            Assert.True(File.Exists(busy));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Cache_folders_cover_every_analysis_fallback_and_the_update_downloads()
    {
        var folders = AppCache.Folders().ToList();
        Assert.Contains(AppCache.UpdateDownloadDir, folders);
        Assert.All(WinDbgLocator.AnalysisDirCandidates(), d => Assert.Contains(d, folders));
        var work = WinDbgLocator.AnalysisWorkDir();
        if (work is not null) Assert.Contains(work, folders, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 0, "nothing")]
    [InlineData(1, 500, "1 file, 500 bytes")]
    [InlineData(3, 30720, "3 files, 30 KB")]
    [InlineData(2, 5 * 1048576L + 524288, "2 files, 5.5 MB")]
    public void Describe_reads_naturally(int files, long bytes, string expected)
    {
        Assert.Equal(expected, AppCache.Describe(new AppCache.Summary(files, bytes)));
    }
}
