using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class GroupingAndDriversTests
{
    // ------------------------------------------------------------ update grouping

    [Theory]
    [InlineData("Intel - System - 2406.5.5.0", null, UpdateClassifier.Drivers)]
    [InlineData("Razer Inc - HIDClass - 6.2.9200.16547", null, UpdateClassifier.Drivers)]
    [InlineData("NVIDIA Display Driver Update (32.0.15.9186)", null, UpdateClassifier.Drivers)]
    [InlineData("Some Vendor thing", "Drivers", UpdateClassifier.Drivers)]
    [InlineData("2026-06 Security Update (KB5094126) (26200.8655)", "Security Updates", UpdateClassifier.Windows)]
    [InlineData("2026-06 Security Update (KB5094126) (26200.8655)", null, UpdateClassifier.Windows)]   // history may lack the classification
    [InlineData("2026-09 Cumulative Update Preview for .NET Framework 3.5 and 4.8.1 (KB5050000)", null, UpdateClassifier.Windows)]
    [InlineData("2026-09 Cumulative Update for Windows 11 (KB5099999)", null, UpdateClassifier.Windows)]
    [InlineData("Feature Update to Windows 11 24H2", null, UpdateClassifier.Windows)]
    [InlineData("Security Intelligence Update for Microsoft Defender Antivirus - KB2267602", "Definition Updates", UpdateClassifier.Security)]
    [InlineData("Windows Malicious Software Removal Tool x64 - v5.142 (KB890830)", "Update Rollups", UpdateClassifier.Security)]
    [InlineData("Update for Windows Security platform - KB5007651", null, UpdateClassifier.Security)]
    [InlineData("Microsoft .NET 8.0.10 Security Update for x64 (KB5045000)", null, UpdateClassifier.Apps)]
    [InlineData("Microsoft Edge 130.0.2849.46", null, UpdateClassifier.Apps)]
    [InlineData("Something unrecognised", null, UpdateClassifier.Other)]
    public void Updates_are_grouped_by_title_and_classification(string title, string? category, string expected) =>
        Assert.Equal(expected, UpdateClassifier.Group(title, category));

    [Fact]
    public void Card_groups_follow_display_order_and_skip_empty_groups()
    {
        var entry = new RebootEntry
        {
            Timestamp = T0, Category = RebootCategory.WindowsUpdate, Title = "t", Summary = "s",
            Updates = new()
            {
                new("Intel - System - 1.0.0.0", null, null, null, null, false, UpdateClassifier.Drivers),
                new("Cumulative Update (KB1)", "KB1", null, null, null, false, UpdateClassifier.Windows),
                new("Razer Inc - HIDClass - 2.0.0.0", null, null, null, null, true, UpdateClassifier.Drivers),
            },
        };

        var groups = entry.UpdateGroups;

        Assert.Equal(new[] { UpdateClassifier.Windows, UpdateClassifier.Drivers }, groups.Select(g => g.Name));
        Assert.Equal(2, groups[1].Items.Count);
        Assert.True(groups[1].Items[1].Failed);
    }

    [Fact]
    public void Analyzer_assigns_groups_and_finds_driver_history_by_title()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent>
        {
            Boot(T0),
            UpdateInstalled(shutdown.AddMinutes(-30), "Intel - System - 2406.5.5.0"),
            UpdateInstalled(shutdown.AddMinutes(-25), "2026-09 Cumulative Update for Windows 11 (KB5099999)"),
        };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), @"C:\Windows\servicing\TrustedInstaller.exe (LUNCHBOX)", @"NT AUTHORITY\SYSTEM", "Operating System: Upgrade (Planned)")));
        var history = new Dictionary<string, UpdateDetails>(StringComparer.OrdinalIgnoreCase)
        {
            ["Intel - System - 2406.5.5.0"] = new("Intel - System - 2406.5.5.0", "Intel System driver update released in June 2024", "Drivers", null, null),
        };

        var e = RebootAnalyzer.Build(events.OrderBy(x => x.Time).ToList(), new AnalysisOptions { UpdateDetails = history })
            .Single(x => x.Category == RebootCategory.WindowsUpdate);

        var driver = e.Updates.Single(u => u.Title.StartsWith("Intel"));
        Assert.Equal(UpdateClassifier.Drivers, driver.Group);
        Assert.Equal("Intel System driver update released in June 2024", driver.Description);
        Assert.Equal(UpdateClassifier.Windows, e.Updates.Single(u => u.Kb == "KB5099999").Group);
        Assert.Equal(new[] { UpdateClassifier.Windows, UpdateClassifier.Drivers }, e.UpdateGroups.Select(g => g.Name));
    }

    [Fact]
    public void Exports_show_the_heading_and_group_names()
    {
        var entry = new RebootEntry
        {
            Timestamp = T0, Category = RebootCategory.WindowsUpdate, Title = "t", Summary = "s",
            Updates = new() { new("Intel - System - 1.0.0.0", null, "desc", "Drivers", null, true, UpdateClassifier.Drivers) },
        };
        var result = new AnalysisResult { Entries = new() { entry }, CurrentBootTime = T0 };

        var text = TextExporter.ToText(result, result.Entries, "all");
        Assert.Contains("Installed updates:", text);
        Assert.Contains("      Drivers:", text);
        Assert.Contains("Intel - System - 1.0.0.0 (FAILED)", text);

        var html = TextExporter.ToHtml(result, result.Entries, "all");
        Assert.Contains("<details class=\"updates-block\"><summary>Installed updates</summary>", html);
        Assert.Contains("<div class=\"ugroup\">Drivers</div>", html);
        Assert.Contains("<span class=\"fail\">failed</span>", html);
    }

    // ------------------------------------------------------------ driver labels

    [Theory]
    [InlineData("nvlddmkm.sys", "NVIDIA kernel-mode display driver")]
    [InlineData("NVLDDMKM.SYS", "NVIDIA kernel-mode display driver")]
    [InlineData(@"C:\Windows\System32\drivers\nvlddmkm.sys", "NVIDIA kernel-mode display driver")]
    [InlineData("Netwtw04.sys", "Intel wireless adapter driver")]
    [InlineData("Netwtw14.sys", "Intel wireless adapter driver")]
    [InlineData("ntoskrnl.exe", "Windows kernel")]
    [InlineData("MpKsl1a2b3c.sys", "Microsoft Defender kernel driver")]
    [InlineData("nvsomethingnew.sys", "NVIDIA driver")]
    [InlineData("totallyunknown.sys", null)]
    [InlineData("", null)]
    public void Driver_names_map_to_labels(string file, string? expected) =>
        Assert.Equal(expected, DriverCatalog.Label(file));

    [Fact]
    public void Annotate_appends_labels_once_and_leaves_unknown_names_alone()
    {
        Assert.Equal(
            "Probably caused by nvlddmkm.sys (NVIDIA kernel-mode display driver) and mystery.sys",
            DriverCatalog.Annotate("Probably caused by nvlddmkm.sys and mystery.sys"));

        // Already labelled text is not labelled twice.
        var once = DriverCatalog.Annotate("Netwtw08.sys (Intel wireless adapter driver) failed");
        Assert.Equal("Netwtw08.sys (Intel wireless adapter driver) failed", once);

        Assert.Equal("", DriverCatalog.Annotate(null));
        Assert.Equal("no drivers here", DriverCatalog.Annotate("no drivers here"));
    }

    [Fact]
    public void Analyzer_labels_drivers_in_summaries_details_and_log_messages()
    {
        var boot = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Boot(boot),
            KernelPower41(boot.AddSeconds(2), 0x116),
            Ev(boot.AddSeconds(7), EventLogSource.BugCheck, 1001, new()
            {
                ["param1"] = "0x00000116 (0xffff, 0xfffff8, 0x0, 0xd)",
                ["param2"] = @"C:\Windows\Minidump\x.dmp",
                ["param3"] = "r",
            }, message: "The computer has rebooted from a bugcheck. Faulting module nvlddmkm.sys."),
            Requested(boot.AddHours(1), @"C:\Windows\Explorer.EXE (LUNCHBOX)", @"LUNCHBOX\bucky", "Other (Unplanned)", comment: "Netwtw10.sys kept crashing"),
            LogStopped(boot.AddHours(1).AddSeconds(2)), ShuttingDown(boot.AddHours(1).AddSeconds(3)),
            Boot(boot.AddHours(1).AddSeconds(30)),
        };

        var entries = RebootAnalyzer.Build(events.OrderBy(x => x.Time).ToList());

        var bsod = entries.Single(x => x.Category == RebootCategory.BlueScreen);
        Assert.Contains(bsod.Evidence, v => v.Message.Contains("nvlddmkm.sys (NVIDIA kernel-mode display driver)"));

        var user = entries.Single(x => x.Category == RebootCategory.UserInitiated);
        Assert.Contains("Netwtw10.sys (Intel wireless adapter driver)", user.Summary);
        Assert.Contains(user.Details, d => d.Key == "Comment" && d.Value.Contains("(Intel wireless adapter driver)"));
    }
}
