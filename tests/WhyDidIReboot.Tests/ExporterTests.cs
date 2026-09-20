using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class ExporterTests
{
    private static readonly RebootEntry Crash = new()
    {
        Timestamp = new DateTime(2026, 7, 12, 20, 28, 55),
        Category = RebootCategory.BlueScreen,
        Title = "Crashed with a blue screen: DRIVER_POWER_STATE_FAILURE",
        Summary = "Windows hit a fatal error <STOP 0x0000009F> & restarted itself.",
        ShutdownTime = new DateTime(2026, 7, 12, 20, 28, 55),
        BootTime = new DateTime(2026, 7, 12, 20, 34, 17),
        Downtime = TimeSpan.FromSeconds(322),
        PreviousUptime = TimeSpan.FromMinutes(73),
        DumpPath = @"C:\Windows\Minidump\071226-7000-01.dmp",
        Details = new() { new("STOP code", "0x0000009F (DRIVER_POWER_STATE_FAILURE)"), new("Crash dump", @"C:\Windows\Minidump\071226-7000-01.dmp") },
        Evidence = new() { new(new DateTime(2026, 7, 12, 20, 34, 19), "System", 41, "Microsoft-Windows-Kernel-Power", "The system has rebooted without cleanly shutting down first.") },
    };

    private static readonly RebootEntry Update = new()
    {
        Timestamp = new DateTime(2026, 9, 13, 15, 30, 22),
        Category = RebootCategory.WindowsUpdate,
        Title = "Restarted to finish installing Windows updates",
        Summary = "Windows Update restarted the PC, \"quoted\" text included.",
        ShutdownTime = new DateTime(2026, 9, 13, 15, 30, 22),
        BootTime = new DateTime(2026, 9, 13, 15, 30, 50),
        Downtime = TimeSpan.FromSeconds(28),
    };

    private static readonly AnalysisResult Result = new()
    {
        Entries = new() { Update, Crash },
        RecordsRead = 1234,
        Elapsed = TimeSpan.FromMilliseconds(150),
        CurrentBootTime = new DateTime(2026, 9, 13, 15, 30, 53),
        Warnings = new() { "Application log: access denied (test)." },
    };

    [Fact]
    public void Text_report_contains_header_entries_details_and_evidence()
    {
        var text = TextExporter.ToText(Result, Result.Entries, "last 90 days");

        Assert.Contains($"Why Did I Reboot {AppInfo.VersionTag} — report for " + Environment.MachineName, text);
        Assert.Matches(@"^v\d+\.\d+\.\d+$", AppInfo.VersionTag);
        Assert.Contains("Range: last 90 days", text);
        Assert.Contains("Log records read: 1234", text);
        Assert.Contains("Warning: Application log: access denied (test).", text);
        Assert.Contains("[2026-09-13 15:30:22]  RESTARTED TO FINISH INSTALLING WINDOWS UPDATES", text);
        Assert.Contains("[2026-07-12 20:28:55]  CRASHED WITH A BLUE SCREEN: DRIVER_POWER_STATE_FAILURE", text);
        Assert.Contains("Category: Blue screen", text);
        Assert.Contains("STOP code: 0x0000009F (DRIVER_POWER_STATE_FAILURE)", text);
        Assert.Contains("System #41 Microsoft-Windows-Kernel-Power: The system has rebooted", text);
    }

    [Fact]
    public void Csv_has_a_header_row_and_escapes_quotes()
    {
        var csv = TextExporter.ToCsv(Result.Entries);
        var rows = CsvReport.ReadRows(csv);

        Assert.Equal(CsvReport.Columns, rows[0]);
        Assert.Equal(3, rows.Count);
        Assert.Contains("\"Windows Update restarted the PC, \"\"quoted\"\" text included.\"", csv);
        Assert.Equal("28", rows[1][6]);
        Assert.Equal("", rows[1][7]);
        Assert.Equal("322", rows[2][6]);
        Assert.Equal("4380", rows[2][7]);
        Assert.Equal(@"C:\Windows\Minidump\071226-7000-01.dmp", rows[2][8]);
        Assert.Contains("STOP code\t0x0000009F", rows[2][9]);   // details packed as key<tab>value lines
    }

    [Fact]
    public void Html_report_is_a_complete_document_with_escaped_content()
    {
        var html = TextExporter.ToHtml(Result, Result.Entries, "everything");

        Assert.StartsWith("<!doctype html>", html);
        Assert.EndsWith("</html>\n", html);
        Assert.Contains("<title>Why Did I Reboot — " + Environment.MachineName + "</title>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("range: everything", html);

        // Content is escaped, never injected raw.
        Assert.Contains("&lt;STOP 0x0000009F&gt; &amp; restarted", html);
        Assert.DoesNotContain("<STOP 0x0000009F>", html);

        // One card per entry with its category colour, badge and day heading.
        Assert.Equal(2, CountOf(html, "<div class=\"card\""));
        Assert.Contains($"--c:{CategoryPalette.Hex(RebootCategory.BlueScreen, false)};--cd:{CategoryPalette.Hex(RebootCategory.BlueScreen, true)}", html);
        Assert.Contains("<span class=\"badge\">Blue screen</span>", html);
        Assert.Contains("<h2 class=\"day\">Sunday, 12 July 2026</h2>", html);
        Assert.Contains("<td>STOP code</td><td>0x0000009F (DRIVER_POWER_STATE_FAILURE)</td>", html);
        Assert.Contains("Event 41 · Microsoft-Windows-Kernel-Power", html);
        Assert.Contains("class=\"warn\">Application log: access denied (test).", html);
    }

    [Fact]
    public void Html_report_is_interactive_and_collapses_updates()
    {
        var withUpdates = new RebootEntry
        {
            Timestamp = new DateTime(2026, 9, 13, 15, 30, 22),
            Category = RebootCategory.WindowsUpdate,
            Title = "Restarted to finish installing Windows updates",
            Summary = "s",
            Updates = new() { new("2026-09 Cumulative Update (KB5099999)", "KB5099999", null, null, null, false, UpdateClassifier.Windows) },
        };
        var result = new AnalysisResult { Entries = new() { withUpdates, Crash }, CurrentBootTime = new DateTime(2026, 9, 13) };

        var html = TextExporter.ToHtml(result, result.Entries, "everything");

        // Category chips are buttons the script toggles; cards carry the category for filtering.
        Assert.Contains("<button type=\"button\" class=\"chip\" data-cat=\"WindowsUpdate\"", html);
        Assert.Contains("<button type=\"button\" class=\"chip\" data-cat=\"BlueScreen\"", html);
        Assert.Contains("<div class=\"card\" data-cat=\"BlueScreen\" data-reboot=\"1\"", html);

        // Search box, clear button, presets and the shown counter.
        Assert.Contains("<input id=\"q\" type=\"search\"", html);
        Assert.Contains("<button id=\"clear\"", html);
        foreach (var preset in new[] { "default", "everything", "reboots", "problems", "none" })
            Assert.Contains($"data-preset=\"{preset}\"", html);
        Assert.Contains("<span id=\"shown\"></span>", html);

        // Updates sit behind a collapsed block like the app, and the script is inline.
        Assert.Contains("<details class=\"updates-block\"><summary>Installed updates</summary>", html);
        Assert.Contains("<script>", html);
        Assert.Contains("URLSearchParams(location.search).get('q')", html);
        Assert.Contains($"Generated by Why Did I Reboot {AppInfo.VersionTag}.", html);
        Assert.Contains("mark.hl{background:var(--hl)", html);
    }

    [Fact]
    public void Html_report_with_no_entries_says_so()
    {
        var html = TextExporter.ToHtml(Result, Array.Empty<RebootEntry>(), "last 7 days");

        Assert.Contains("No events in this range.", html);
        Assert.DoesNotContain("<div class=\"card\"", html);
    }

    [Fact]
    public void Single_entry_text_lists_details_and_records()
    {
        var text = TextExporter.EntryToText(Crash);

        Assert.StartsWith("[2026-07-12 20:28:55]  CRASHED WITH A BLUE SCREEN", text);
        Assert.Contains("    Crash dump: C:\\Windows\\Minidump\\071226-7000-01.dmp", text);
        Assert.Contains("    Log records:", text);
        Assert.DoesNotContain("\n\n", text.TrimEnd());
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
