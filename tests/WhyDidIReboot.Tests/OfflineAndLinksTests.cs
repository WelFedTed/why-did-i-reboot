using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class OfflineAndLinksTests
{
    // ------------------------------------------------------------ LogLocation

    private static bool Exists(string p, params string[] present) =>
        present.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Drive_root_resolves_to_the_winevt_logs_and_remembers_the_root()
    {
        var sys = @"D:\Windows\System32\winevt\Logs\System.evtx";
        var app = @"D:\Windows\System32\winevt\Logs\Application.evtx";

        var loc = LogLocation.Resolve(@"D:\", p => Exists(p, sys, app));

        Assert.NotNull(loc);
        Assert.True(loc!.IsOffline);
        Assert.Equal(sys, loc.SystemPath);
        Assert.Equal(app, loc.ApplicationPath);
        Assert.Equal(@"D:\", loc.Root);
        Assert.Equal(@"D:\Windows\Minidump\x.dmp", loc.MapPath(@"C:\Windows\Minidump\x.dmp"));
    }

    [Theory]
    [InlineData(@"D:")]
    [InlineData(@"D:\Windows")]
    [InlineData(@"D:\Windows\")]
    [InlineData(@"D:\Windows\System32\winevt\Logs")]
    [InlineData(@"D:\Windows\System32\winevt\Logs\System.evtx")]
    public void Windows_folder_logs_folder_and_evtx_file_all_resolve(string input)
    {
        var sys = @"D:\Windows\System32\winevt\Logs\System.evtx";

        var loc = LogLocation.Resolve(input, p => Exists(p, sys));

        Assert.NotNull(loc);
        Assert.Equal(sys, loc!.SystemPath);
        Assert.Null(loc.ApplicationPath);
        Assert.Equal(@"D:\", loc.Root);
    }

    [Fact]
    public void Copied_logs_folder_outside_a_windows_tree_has_no_root_and_leaves_paths_alone()
    {
        var sys = @"E:\backup\logs\System.evtx";

        var loc = LogLocation.Resolve(@"E:\backup\logs", p => Exists(p, sys));

        Assert.NotNull(loc);
        Assert.Null(loc!.Root);
        Assert.Equal(@"E:\backup\logs", loc.Display);
        Assert.Equal(@"C:\Windows\Minidump\x.dmp", loc.MapPath(@"C:\Windows\Minidump\x.dmp"));
    }

    [Fact]
    public void Folder_without_logs_returns_null()
    {
        Assert.Null(LogLocation.Resolve(@"D:\Photos", _ => false));
        Assert.Null(LogLocation.Resolve("", _ => true));
    }

    [Fact]
    public void Local_location_never_remaps()
    {
        Assert.False(LogLocation.Local.IsOffline);
        Assert.Equal(@"C:\x.dmp", LogLocation.Local.MapPath(@"C:\x.dmp"));
    }

    // ------------------------------------------------------------ links

    [Theory]
    [InlineData(0x9Fu, "https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x9f--driver-power-state-failure")]
    [InlineData(0x133u, "https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x133--dpc-watchdog-violation")]
    [InlineData(0x1Au, "https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x1a--memory-management")]
    [InlineData(0xC000021Au, "https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0xc000021a--status-system-process-terminated")]
    public void Known_bugchecks_link_to_their_learn_page(uint code, string expected) =>
        Assert.Equal(expected, BugcheckCatalog.Url(code));

    [Fact]
    public void Unknown_bugchecks_link_to_the_reference_index() =>
        Assert.Equal(BugcheckCatalog.ReferenceIndexUrl, BugcheckCatalog.Url(0xBEEF));

    [Theory]
    [InlineData("2026-06 Security Update (KB5094126) (26200.8655)", "KB5094126")]
    [InlineData("Update for Microsoft Defender Antivirus antimalware platform - KB4052623 (Version 4.18)", "KB4052623")]
    [InlineData("Windows Malicious Software Removal Tool x64 - v5.142 (KB890830)", "KB890830")]
    [InlineData("kb 5001234 lowercase with space", "KB5001234")]
    [InlineData("9NRZT3Q9R3DL-Microsoft.WindowsAppRuntime.2", null)]
    [InlineData("", null)]
    public void Kb_numbers_are_extracted_from_titles(string title, string? expected) =>
        Assert.Equal(expected, KnowledgeBase.KbNumber(title));

    [Fact]
    public void Kb_support_url_is_built_from_the_number()
    {
        Assert.Equal("https://support.microsoft.com/help/5094126", KnowledgeBase.Url("KB5094126"));
        Assert.Equal("https://support.microsoft.com/help/5094126", KnowledgeBase.Url("Something (KB5094126)"));
        Assert.Null(KnowledgeBase.Url("no kb here"));
    }

    [Fact]
    public void Long_descriptions_are_cut_at_a_sentence_boundary()
    {
        var one = "Install this update to resolve issues in Windows.";
        Assert.Equal(one, RebootAnalyzer.Shorten(one));
        Assert.Null(RebootAnalyzer.Shorten("   "));

        var para = "After the download, this tool runs one time to check your computer for infection by specific, prevalent malicious software and helps remove any infection that is found. " +
                   "If an infection is found, the tool will display a status report the next time that you start your computer. A new version of the tool will be offered every month.";
        var shortened = RebootAnalyzer.Shorten(para)!;
        Assert.True(shortened.Length <= 221, shortened.Length.ToString());
        Assert.EndsWith(".", shortened);
        Assert.StartsWith("After the download", shortened);

        var noSentences = new string('x', 300);
        Assert.EndsWith("…", RebootAnalyzer.Shorten(noSentences)!);
    }

    [Theory]
    [InlineData("http://support.microsoft.com", null)]
    [InlineData("https://support.microsoft.com/", null)]
    [InlineData("https://go.microsoft.com/fwlink/?LinkId=52661", "https://go.microsoft.com/fwlink/?LinkId=52661")]
    [InlineData("https://support.microsoft.com/help/5094126", "https://support.microsoft.com/help/5094126")]
    [InlineData("ftp://example.com/x", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void Only_specific_support_urls_are_kept(string? input, string? expected) =>
        Assert.Equal(expected, RebootAnalyzer.SpecificUrl(input));

    // ------------------------------------------------------------ analyzer integration

    [Fact]
    public void Blue_screen_entry_carries_a_learn_link_and_dump_paths_are_remapped()
    {
        var boot = T0.AddHours(1);
        var dump = @"C:\Windows\Minidump\071226-7000-01.dmp";
        var events = new List<RawEvent>
        {
            Boot(T0), Boot(boot),
            KernelPower41(boot.AddSeconds(2), 159),
            Bugcheck1001(boot.AddSeconds(7), "0x0000009f", dump, "r"),
        };
        string? checkedPath = null;
        var options = new AnalysisOptions { MapPath = p => { checkedPath = @"D:\" + p[3..]; return checkedPath; } };

        var e = RebootAnalyzer.Build(events, options).Single(x => x.Category == RebootCategory.BlueScreen);

        var link = Assert.Single(e.Links);
        Assert.Equal("STOP 0x9F on Microsoft Learn", link.Label);
        Assert.Equal(BugcheckCatalog.Url(0x9F), link.Url);
        Assert.Equal(@"D:\Windows\Minidump\071226-7000-01.dmp", checkedPath);
        Assert.Equal(dump, e.DumpPath);   // the recorded path is still shown as written
    }

    [Fact]
    public void Update_cards_list_installed_updates_with_history_descriptions_and_kb_links()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent>
        {
            Boot(T0),
            UpdateInstalled(shutdown.AddMinutes(-40), "2026-09 Cumulative Update for Windows 11 (KB5099999)"),
            UpdateInstalled(shutdown.AddMinutes(-39), "2026-09 Cumulative Update for Windows 11 (KB5099999)"),   // duplicate record
            UpdateInstalled(shutdown.AddMinutes(-30), "Windows Malicious Software Removal Tool x64 - v5.142 (KB890830)"),
        };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), @"C:\Windows\servicing\TrustedInstaller.exe (LUNCHBOX)", @"NT AUTHORITY\SYSTEM", "Operating System: Upgrade (Planned)")));

        var history = new Dictionary<string, UpdateDetails>(StringComparer.OrdinalIgnoreCase)
        {
            ["KB5099999"] = new("2026-09 Cumulative Update for Windows 11 (KB5099999)", "Install this update to resolve issues in Windows.", "Security Updates", "https://support.microsoft.com/help/5099999", null),
        };
        var options = new AnalysisOptions { UpdateDetails = history };

        var e = RebootAnalyzer.Build(events.OrderBy(x => x.Time).ToList(), options).Single(x => x.Category == RebootCategory.WindowsUpdate);

        Assert.Equal(2, e.Updates.Count);
        var cu = e.Updates.Single(u => u.Kb == "KB5099999");
        Assert.Equal("Install this update to resolve issues in Windows.", cu.Description);
        Assert.Equal("Security Updates", cu.Category);
        Assert.Equal("https://support.microsoft.com/help/5099999", cu.Url);
        Assert.False(cu.Failed);

        var msrt = e.Updates.Single(u => u.Kb == "KB890830");
        Assert.Null(msrt.Description);                                            // not in history
        Assert.Equal("https://support.microsoft.com/help/890830", msrt.Url);     // fallback link
    }

    [Fact]
    public void Failed_update_is_flagged_on_the_card()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Ev(shutdown.AddMinutes(-20), EventLogSource.WindowsUpdateClient, 20, new() { ["updateTitle"] = "Broken Update (KB5000001)", ["errorCode"] = "0x80070002" }),
        };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), @"C:\Windows\Explorer.EXE (LUNCHBOX)", @"LUNCHBOX\bucky", "Other (Unplanned)")));

        var e = RebootAnalyzer.Build(events.OrderBy(x => x.Time).ToList()).Single(x => x.Category == RebootCategory.UserInitiated);

        var u = Assert.Single(e.Updates);
        Assert.True(u.Failed);
        Assert.Equal("KB5000001", u.Kb);
    }

    [Fact]
    public void Exports_include_links_and_update_descriptions()
    {
        var entry = new RebootEntry
        {
            Timestamp = T0,
            Category = RebootCategory.WindowsUpdate,
            Title = "Restarted to finish installing Windows updates",
            Summary = "s",
            Updates = new() { new("Cumulative Update (KB5099999)", "KB5099999", "Fixes <things> & stuff.", "Security Updates", "https://support.microsoft.com/help/5099999", false) },
            Links = new() { new("STOP 0x9F on Microsoft Learn", "https://learn.microsoft.com/x") },
        };
        var result = new AnalysisResult { Entries = new() { entry }, CurrentBootTime = T0 };

        var text = TextExporter.ToText(result, result.Entries, "all");
        Assert.Contains("- Cumulative Update (KB5099999)", text);
        Assert.Contains("Fixes <things> & stuff.", text);
        Assert.Contains("https://support.microsoft.com/help/5099999", text);
        Assert.Contains("STOP 0x9F on Microsoft Learn: https://learn.microsoft.com/x", text);

        var html = TextExporter.ToHtml(result, result.Entries, "all");
        Assert.Contains("Fixes &lt;things&gt; &amp; stuff.", html);
        Assert.Contains("<a href=\"https://support.microsoft.com/help/5099999\" target=\"_blank\" rel=\"noopener\">KB5099999 ↗</a>", html);
        Assert.Contains("<a href=\"https://learn.microsoft.com/x\" target=\"_blank\" rel=\"noopener\">STOP 0x9F on Microsoft Learn ↗</a>", html);
    }

    [Fact]
    public void Offline_report_header_says_where_the_logs_came_from()
    {
        var loc = new LogLocation(@"D:\Windows\System32\winevt\Logs\System.evtx", null, @"D:\", @"D:\", true);
        var result = new AnalysisResult { Source = loc, CurrentBootTime = T0 };

        var text = TextExporter.ToText(result, Array.Empty<RebootEntry>(), "all");
        Assert.Contains(@"report for logs at D:\", text);
        Assert.Contains("Offline logs from another Windows installation", text);

        var html = TextExporter.ToHtml(result, Array.Empty<RebootEntry>(), "all");
        Assert.Contains("Offline logs from another Windows installation", html);
    }
}
