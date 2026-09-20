using System.IO;
using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class CsvAndRangeTests
{
    private static readonly RebootEntry Rich = new()
    {
        Timestamp = new DateTime(2026, 7, 12, 20, 28, 55),
        Category = RebootCategory.BlueScreen,
        Title = "Crashed with a blue screen: DRIVER_POWER_STATE_FAILURE",
        Summary = "Windows hit a fatal error, \"quoted\", with a comma, and\na line break.",
        ShutdownTime = new DateTime(2026, 7, 12, 20, 28, 55),
        BootTime = new DateTime(2026, 7, 12, 20, 34, 17),
        Downtime = TimeSpan.FromSeconds(322),
        PreviousUptime = TimeSpan.FromMinutes(73),
        DumpPath = @"C:\Windows\Minidump\does-not-exist.dmp",
        Details = new() { new("STOP code", "0x0000009F (DRIVER_POWER_STATE_FAILURE)"), new("Boot type", "Full (cold) boot") },
        Links = new() { new("STOP 0x9F on Microsoft Learn", "https://learn.microsoft.com/x") },
        Updates = new()
        {
            new("2026-06 Security Update (KB5094126)", "KB5094126", "Install this update.", "Security Updates", "https://support.microsoft.com/help/5094126", false, UpdateClassifier.Windows),
            new("Razer Inc - HIDClass - 6.2.9200.16547", null, null, null, "https://www.catalog.update.microsoft.com/Search.aspx?q=Razer", true, UpdateClassifier.Drivers),
        },
        Evidence = new()
        {
            new(new DateTime(2026, 7, 12, 20, 34, 19), "System", 41, "Microsoft-Windows-Kernel-Power", "The system has rebooted without cleanly shutting down first."),
            new(new DateTime(2026, 7, 12, 20, 34, 24), "System", 1001, "Microsoft-Windows-WER-SystemErrorReporting", "The bugcheck was: 0x0000009f, with a comma"),
        },
    };

    private static readonly RebootEntry Plain = new()
    {
        Timestamp = new DateTime(2026, 9, 13, 15, 30, 22),
        Category = RebootCategory.WindowsUpdate,
        Title = "Restarted to finish installing Windows updates",
        Summary = "s",
    };

    // ------------------------------------------------------------ CSV round trip

    [Fact]
    public void Csv_round_trips_every_card_field()
    {
        var csv = CsvReport.ToCsv(new[] { Rich, Plain });
        var back = CsvReport.Parse(csv);

        Assert.Equal(2, back.Count);
        var r = back[0];
        Assert.Equal(Rich.Timestamp, r.Timestamp);
        Assert.Equal(RebootCategory.BlueScreen, r.Category);
        Assert.Equal(Rich.Title, r.Title);
        Assert.Equal(Rich.Summary, r.Summary);
        Assert.Equal(Rich.ShutdownTime, r.ShutdownTime);
        Assert.Equal(Rich.BootTime, r.BootTime);
        Assert.Equal(Rich.Downtime, r.Downtime);
        Assert.Equal(Rich.PreviousUptime, r.PreviousUptime);
        Assert.Equal(Rich.DumpPath, r.DumpPath);
        Assert.False(r.DumpExists);
        Assert.Equal(Rich.Details, r.Details);
        Assert.Equal(Rich.Links, r.Links);
        Assert.Equal(Rich.Updates, r.Updates);
        Assert.Equal(Rich.Evidence, r.Evidence);
        Assert.Equal(new[] { UpdateClassifier.Windows, UpdateClassifier.Drivers }, r.UpdateGroups.Select(g => g.Name));

        var p = back[1];
        Assert.Equal(RebootCategory.WindowsUpdate, p.Category);
        Assert.Null(p.ShutdownTime);
        Assert.Null(p.DumpPath);
        Assert.Empty(p.Details);
        Assert.Empty(p.Updates);
        Assert.Empty(p.Evidence);
    }

    [Fact]
    public void Csv_header_lists_the_summary_columns_first()
    {
        var csv = CsvReport.ToCsv(Array.Empty<RebootEntry>());
        Assert.StartsWith("Timestamp,Category,Title,Summary,ShutdownTime,BootTime,DowntimeSeconds,PreviousUptimeSeconds,DumpPath,Details,Links,Updates,LogRecords", csv);
    }

    [Fact]
    public void Old_nine_column_csv_still_opens()
    {
        var old = "Timestamp,Category,Title,Summary,ShutdownTime,BootTime,DowntimeSeconds,PreviousUptimeSeconds,DumpPath\n" +
                  "\"2026-09-13 15:30:22\",\"Windows Update\",\"Restarted\",\"Sum\",\"2026-09-13 15:30:22\",\"2026-09-13 15:30:50\",28,,\"\"\n";

        var e = Assert.Single(CsvReport.Parse(old));
        Assert.Equal(RebootCategory.WindowsUpdate, e.Category);
        Assert.Equal(TimeSpan.FromSeconds(28), e.Downtime);
        Assert.Null(e.PreviousUptime);
        Assert.Empty(e.Details);
    }

    [Fact]
    public void Foreign_csv_is_rejected_and_bad_rows_are_skipped_with_a_warning()
    {
        Assert.Throws<InvalidDataException>(() => CsvReport.Parse("a,b,c\n1,2,3\n"));
        Assert.Throws<InvalidDataException>(() => CsvReport.Parse(""));

        var warnings = new List<string>();
        var text = CsvReport.ToCsv(new[] { Plain }) + "\"not a date\",\"User\",\"t\",\"s\"\n";
        var entries = CsvReport.Parse(text, warnings);
        Assert.Single(entries);
        Assert.Single(warnings);
        Assert.Contains("row 3", warnings[0]);
    }

    [Fact]
    public void Rfc4180_reader_handles_quotes_commas_and_newlines()
    {
        var rows = CsvReport.ReadRows("a,\"b,c\",\"d\"\"e\"\n\"multi\nline\",x\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b,c", "d\"e" }, rows[0]);
        Assert.Equal(new[] { "multi\nline", "x" }, rows[1]);
    }

    [Fact]
    public void Csv_location_resolves_and_analyzer_reads_it_within_the_range()
    {
        var path = Path.Combine(Path.GetTempPath(), "wdir-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(path, CsvReport.ToCsv(new[] { Rich, Plain }));

            var loc = LogLocation.Resolve(path);
            Assert.NotNull(loc);
            Assert.True(loc!.IsCsv);
            Assert.True(loc.IsOffline);
            Assert.Equal(Path.GetFileName(path), loc.Display);

            var all = RebootAnalyzer.Analyze(TimeRange.All, loc);
            Assert.Equal(2, all.Entries.Count);
            Assert.Equal(Plain.Timestamp, all.Entries[0].Timestamp);   // newest first
            Assert.Equal(Rich.BootTime, all.CurrentBootTime);           // last recorded boot among the imported cards

            var july = RebootAnalyzer.Analyze(TimeRange.Days(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)), loc);
            Assert.Single(july.Entries);
            Assert.Equal(RebootCategory.BlueScreen, july.Entries[0].Category);
            Assert.True(july.Source.IsCsv);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_csv_does_not_resolve() =>
        Assert.Null(LogLocation.Resolve(@"C:\nowhere\report.csv", _ => false));

    // ------------------------------------------------------------ time ranges

    [Fact]
    public void Day_ranges_cover_whole_days_and_accept_reversed_input()
    {
        var r = TimeRange.Days(new DateTime(2026, 3, 31), new DateTime(2026, 3, 1));
        Assert.Equal(new DateTime(2026, 3, 1), r.From);
        Assert.Equal(new DateTime(2026, 3, 31, 23, 59, 59, 999).AddTicks(9999), r.To);
        Assert.True(r.Contains(new DateTime(2026, 3, 31, 23, 59, 59)));
        Assert.False(r.Contains(new DateTime(2026, 4, 1)));
        Assert.Equal("1 Mar 2026 – 31 Mar 2026", r.Label);
    }

    [Fact]
    public void Open_ended_ranges_contain_everything_on_that_side()
    {
        Assert.True(TimeRange.All.Contains(DateTime.MinValue));
        Assert.Equal("everything", TimeRange.All.Label);
        var since = new TimeRange(new DateTime(2026, 1, 1), null);
        Assert.True(since.Contains(DateTime.MaxValue));
        Assert.False(since.Contains(new DateTime(2025, 12, 31)));
        Assert.Equal("since 1 Jan 2026", since.Label);
        Assert.NotNull(TimeRange.LastDays(7).From);
        Assert.Null(TimeRange.LastDays(null).From);
    }

    [Fact]
    public void Time_clause_is_written_in_utc_for_the_event_log()
    {
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local);
        var clause = EventLogSource.TimeClause(new TimeRange(from, null));
        Assert.StartsWith(" and TimeCreated[@SystemTime>='", clause);
        Assert.Contains(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss"), clause);
        Assert.Equal("", EventLogSource.TimeClause(TimeRange.All));
        Assert.Contains(" and @SystemTime<='", EventLogSource.TimeClause(TimeRange.Days(from, from)));
    }

    // ------------------------------------------------------------ links and WinDbg

    [Theory]
    [InlineData("http://support.microsoft.com/select/?target=hub", null)]
    [InlineData("https://support.microsoft.com/select/", null)]
    [InlineData("https://support.microsoft.com/", null)]
    [InlineData("https://support.microsoft.com/help/5094126", "https://support.microsoft.com/help/5094126")]
    public void Generic_support_hub_links_are_rejected(string input, string? expected) =>
        Assert.Equal(expected, RebootAnalyzer.SpecificUrl(input));

    [Fact]
    public void Driver_updates_without_a_kb_link_to_the_update_catalog()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent> { Boot(T0), UpdateInstalled(shutdown.AddMinutes(-30), "Razer Inc - HIDClass - 6.2.9200.16547") };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), @"C:\Windows\Explorer.EXE (LUNCHBOX)", @"LUNCHBOX\bucky", "Other (Unplanned)")));
        var history = new Dictionary<string, UpdateDetails>(StringComparer.OrdinalIgnoreCase)
        {
            ["Razer Inc - HIDClass - 6.2.9200.16547"] = new("Razer Inc - HIDClass - 6.2.9200.16547", "Razer driver", "Drivers", "http://support.microsoft.com/select/?target=hub", null),
        };

        var e = RebootAnalyzer.Build(events.OrderBy(x => x.Time).ToList(), new AnalysisOptions { UpdateDetails = history }).Single(x => x.Category == RebootCategory.UserInitiated);

        var u = Assert.Single(e.Updates);
        Assert.Equal("https://www.catalog.update.microsoft.com/Search.aspx?q=Razer%20Inc%20-%20HIDClass%20-%206.2.9200.16547", u.Url);
        Assert.Equal("Microsoft Update Catalog", u.LinkLabel);
    }

    [Fact]
    public void Windbg_is_found_in_the_store_alias_then_the_sdk_then_path()
    {
        string? Env(string k) => k switch
        {
            "LOCALAPPDATA" => @"C:\Users\me\AppData\Local",
            "ProgramFiles(x86)" => @"C:\Program Files (x86)",
            "ProgramFiles" => @"C:\Program Files",
            "PATH" => @"C:\tools;C:\other",
            _ => null,
        };
        var alias = @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\WinDbgX.exe";
        var sdk = @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\windbg.exe";
        var onPath = @"C:\other\windbg.exe";

        Assert.Equal(alias, WinDbgLocator.Find(p => p == alias || p == sdk || p == onPath, Env));
        Assert.Equal(sdk, WinDbgLocator.Find(p => p == sdk || p == onPath, Env));
        Assert.Equal(onPath, WinDbgLocator.Find(p => p == onPath, Env));
        Assert.Null(WinDbgLocator.Find(_ => false, Env));

        Assert.Equal(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\winget.exe", WinDbgLocator.FindWinget(p => p.EndsWith("winget.exe"), Env));
        Assert.Null(WinDbgLocator.FindWinget(_ => false, Env));
    }

    [Fact]
    public void Windbg_arguments_open_the_dump_and_analyse_it()
    {
        Assert.Equal("-z \"C:\\Windows\\Minidump\\x.dmp\" -c \"!analyze -v\"", WinDbgLocator.Arguments(@"C:\Windows\Minidump\x.dmp"));
        Assert.Contains("--id Microsoft.WinDbg", WinDbgLocator.WingetInstallArguments);
        Assert.Contains("--accept-package-agreements", WinDbgLocator.WingetInstallArguments);
    }
}
