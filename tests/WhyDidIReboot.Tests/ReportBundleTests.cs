using System.IO;
using System.IO.Compression;
using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public sealed class ReportBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wdir-bundle-" + Guid.NewGuid().ToString("N"));
    private readonly string _cache;
    private static readonly DateTime When = new(2026, 10, 4, 14, 5, 54);

    public ReportBundleTests()
    {
        _cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static RebootEntry Crash(string? dumpPath, string title = "Crashed with a blue screen: MEMORY_MANAGEMENT") => new()
    {
        Timestamp = When, Category = RebootCategory.BlueScreen, Title = title, Summary = "Windows hit a fatal error.",
        DumpPath = dumpPath, DumpExists = dumpPath is not null,
        Details = new() { new("STOP code", "0x0000001A (MEMORY_MANAGEMENT)"), new(CsvReport.DumpPresentDetail, "Yes") },
    };

    private static string Present(RebootEntry e) => e.Details.Single(d => d.Key == CsvReport.DumpPresentDetail).Value;

    /// <summary>Creates a fake dump on "another machine": a file under the temp folder standing in for C:\Windows\Minidump.</summary>
    private string MakeDump(string recordedPath, string content, DateTime? written = null)
    {
        var local = Map(recordedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, content);
        File.SetLastWriteTime(local, written ?? new DateTime(2026, 10, 4, 14, 5, 40));
        return local;
    }

    private string Map(string recordedPath) => Path.Combine(_root, "source", recordedPath[3..]);

    [Fact]
    public void A_bundle_round_trips_the_cards_and_their_dumps()
    {
        const string mini = @"C:\Windows\Minidump\100426-10875-01.dmp";
        const string live = @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260712-0828.dmp";
        var miniLocal = MakeDump(mini, "MINIDUMP-ONE");
        MakeDump(live, "LIVE-KERNEL-DUMP");
        var entries = new[] { Crash(mini), Crash(live, "Kernel error"), Crash(null, "No dump here") };
        var zip = Path.Combine(_root, "report.zip");

        var plan = ReportBundle.PlanFor(entries, Map);
        Assert.Equal(2, plan.Dumps.Count);
        Assert.Empty(plan.Missing);
        var written = ReportBundle.Write(zip, plan);
        Assert.Equal((3, 2), (written.Entries, written.Dumps));
        Assert.Empty(written.Skipped);
        Assert.False(File.Exists(zip + ".partial"));

        using (var archive = ZipFile.OpenRead(zip))
            Assert.Equal(
                new[] { "dumps/C/Windows/LiveKernelReports/WATCHDOG/WATCHDOG-20260712-0828.dmp", "dumps/C/Windows/Minidump/100426-10875-01.dmp", "report.csv" },
                archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));

        // "Another PC": nothing exists at the recorded C:\ paths; everything comes from the bundle.
        var location = ReportBundle.Open(zip, _cache);
        Assert.True(location.IsCsv);
        Assert.Equal(zip, location.BundlePath);
        Assert.Equal("report.zip", location.Display);

        var result = RebootAnalyzer.Analyze(new TimeRange(null, null), location);
        Assert.Equal(3, result.Entries.Count);
        var crash = result.Entries.Single(e => e.DumpPath == mini);
        Assert.True(crash.DumpExists);
        var unpacked = location.MapPath(crash.DumpPath!);
        Assert.StartsWith(_cache, unpacked);
        Assert.Equal("MINIDUMP-ONE", File.ReadAllText(unpacked));
        Assert.Equal("LIVE-KERNEL-DUMP", File.ReadAllText(location.MapPath(live)));
        Assert.False(result.Entries.Single(e => e.Title == "No dump here").DumpExists);

        // Same size and write time as the original, so Ask AI's saved analysis is found again after re-opening.
        Assert.Equal(new FileInfo(miniLocal).Length, new FileInfo(unpacked).Length);
        Assert.Equal(File.GetLastWriteTime(miniLocal), File.GetLastWriteTime(unpacked), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Dumps_that_are_gone_are_reported_and_the_report_is_still_written()
    {
        const string here = @"C:\Windows\Minidump\here.dmp";
        const string gone = @"C:\Windows\Minidump\gone.dmp";
        MakeDump(here, "x");
        var zip = Path.Combine(_root, "partial.zip");

        var result = ReportBundle.Write(zip, ReportBundle.PlanFor(new[] { Crash(here), Crash(gone) }, Map));

        Assert.Equal(1, result.Dumps);
        Assert.Contains(result.Skipped, s => s.StartsWith(gone) && s.Contains("not found"));

        var location = ReportBundle.Open(zip, _cache);
        var entries = RebootAnalyzer.Analyze(new TimeRange(null, null), location).Entries;
        Assert.True(entries.Single(e => e.DumpPath == here).DumpExists);
        Assert.False(entries.Single(e => e.DumpPath == gone).DumpExists);
        // The card says what is true where it is opened, not what was true on the PC that exported it.
        Assert.Equal("Yes", Present(entries.Single(e => e.DumpPath == here)));
        Assert.Equal("No", Present(entries.Single(e => e.DumpPath == gone)));
    }

    [Fact]
    public void A_dump_that_exists_but_cannot_be_opened_is_listed_as_unreadable_not_missing()
    {
        const string locked = @"C:\Windows\Minidump\admin-only.dmp";
        var local = MakeDump(locked, "x");
        var zip = Path.Combine(_root, "locked.zip");

        // Stand-in for "access denied": deny read to everyone on the file, as Windows does for non-admins.
        var info = new FileInfo(local);
        var security = info.GetAccessControl();
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        var deny = new System.Security.AccessControl.FileSystemAccessRule(everyone, System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var plan = ReportBundle.PlanFor(new[] { Crash(locked) }, Map);
            Assert.Empty(plan.Dumps);
            Assert.Empty(plan.Missing);
            Assert.Equal(new[] { locked }, plan.Unreadable);

            var result = ReportBundle.Write(zip, plan);
            Assert.Equal(0, result.Dumps);
            Assert.Contains(result.Skipped, s => s.StartsWith(locked) && s.Contains("administrator"));
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void The_same_dump_named_by_two_cards_is_packed_once()
    {
        const string dump = @"C:\Windows\MEMORY.DMP";
        MakeDump(dump, "kernel");
        var plan = ReportBundle.PlanFor(new[] { Crash(dump), Crash(@"c:\windows\memory.dmp") }, Map);
        Assert.Single(plan.Dumps);
    }

    [Fact]
    public void Opening_again_reuses_the_unpacked_folder_and_a_cleared_cache_is_unpacked_again()
    {
        const string dump = @"C:\Windows\Minidump\a.dmp";
        MakeDump(dump, "A");
        var zip = Path.Combine(_root, "again.zip");
        ReportBundle.Write(zip, ReportBundle.PlanFor(new[] { Crash(dump) }, Map));

        var first = ReportBundle.Open(zip, _cache);
        var second = ReportBundle.Open(zip, _cache);
        Assert.Equal(first.SystemPath, second.SystemPath);

        // Settings → Clear cache removes the files; the next load brings them back at the same paths.
        AppCache.Clear(new[] { _cache });
        Assert.False(File.Exists(first.SystemPath));
        var entries = RebootAnalyzer.Analyze(new TimeRange(null, null), first).Entries;
        Assert.True(entries.Single().DumpExists);
        Assert.Equal("A", File.ReadAllText(first.MapPath(dump)));
    }

    [Fact]
    public void A_zip_that_is_not_a_report_is_refused_and_entries_cannot_escape_the_folder()
    {
        var notOurs = Path.Combine(_root, "holiday-photos.zip");
        using (var z = ZipFile.Open(notOurs, ZipArchiveMode.Create))
            Write(z, "photo.txt", "cheese");
        var ex = Assert.Throws<InvalidDataException>(() => ReportBundle.Open(notOurs, _cache));
        Assert.Contains("report.csv", ex.Message);

        var hostile = Path.Combine(_root, "hostile.zip");
        using (var z = ZipFile.Open(hostile, ZipArchiveMode.Create))
        {
            Write(z, "report.csv", CsvReport.ToCsv(new[] { Crash(null) }));
            Write(z, "dumps/../../../escaped.dmp", "outside");
            Write(z, "dumps/C/ok.dmp", "inside");
            Write(z, "other/ignored.txt", "not a dump");
        }
        var location = ReportBundle.Open(hostile, _cache);
        var dir = Path.GetDirectoryName(location.SystemPath)!;
        Assert.True(File.Exists(Path.Combine(dir, "dumps", "C", "ok.dmp")));
        Assert.False(File.Exists(Path.Combine(dir, "other", "ignored.txt")));
        Assert.Empty(Directory.GetFiles(_root, "escaped.dmp", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped.dmp")));
    }

    [Theory]
    [InlineData(@"C:\Windows\Minidump\100426-10875-01.dmp", "dumps/C/Windows/Minidump/100426-10875-01.dmp")]
    [InlineData(@"d:\Dumps\x.dmp", "dumps/D/Dumps/x.dmp")]
    [InlineData(@"C:\Windows\..\..\evil.dmp", "dumps/C/Windows/evil.dmp")]
    [InlineData(@"\\server\share\crash.dmp", "dumps/_/crash.dmp")]
    [InlineData("MEMORY.DMP", "dumps/_/MEMORY.DMP")]
    public void Dump_paths_become_safe_entry_names(string recorded, string expected)
    {
        Assert.Equal(expected, ReportBundle.EntryNameFor(recorded));
        Assert.Equal(Path.Combine(@"X:\b\dumps", expected["dumps/".Length..].Replace('/', '\\')), ReportBundle.LocalPathFor(@"X:\b\dumps", recorded));
    }

    [Fact]
    public void Resolve_opens_csv_and_zip_reports_and_plain_csv_paths_are_not_remapped()
    {
        Assert.Null(LogLocation.Resolve(Path.Combine(_root, "missing.zip")));
        Assert.True(ReportBundle.IsBundlePath(@"C:\x\Report.ZIP"));
        Assert.False(ReportBundle.IsBundlePath(@"C:\x\report.csv"));

        var csv = LogLocation.ForCsv(@"C:\x\report.csv");
        Assert.Null(csv.BundlePath);
        Assert.Equal(@"C:\Windows\Minidump\a.dmp", csv.MapPath(@"C:\Windows\Minidump\a.dmp"));
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open());
        w.Write(content);
    }
}
