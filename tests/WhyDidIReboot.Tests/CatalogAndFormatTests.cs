using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class CatalogAndFormatTests
{
    [Theory]
    [InlineData("0x0000009f", 0x9Fu)]
    [InlineData("0X9F", 0x9Fu)]
    [InlineData("9f", 0x9Fu)]
    [InlineData("1cc", 0x1CCu)]
    [InlineData(" 133 ", 0x133u)]
    public void Hex_bugcheck_codes_parse(string text, uint expected) =>
        Assert.Equal(expected, BugcheckCatalog.ParseHex(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zz")]
    [InlineData(null)]
    public void Invalid_hex_returns_null(string? text) =>
        Assert.Null(BugcheckCatalog.ParseHex(text));

    [Fact]
    public void Kernel_power_41_codes_are_decimal()
    {
        Assert.Equal(159u, BugcheckCatalog.ParseDecimal("159"));
        Assert.Equal(0u, BugcheckCatalog.ParseDecimal("0"));
        Assert.Null(BugcheckCatalog.ParseDecimal("9f"));
    }

    [Fact]
    public void Known_codes_have_names_and_hints()
    {
        Assert.Equal("DRIVER_POWER_STATE_FAILURE", BugcheckCatalog.Name(0x9F));
        Assert.Equal("0x0000009F DRIVER_POWER_STATE_FAILURE", BugcheckCatalog.Format(0x9F));
        Assert.Contains("power change", BugcheckCatalog.Hint(0x9F));
        Assert.Equal("CRITICAL_PROCESS_DIED", BugcheckCatalog.Name(0xEF));
        Assert.Equal("STATUS_SYSTEM_PROCESS_TERMINATED", BugcheckCatalog.Name(0xC000021A));
    }

    [Fact]
    public void Unknown_codes_are_safe()
    {
        Assert.Equal("Unknown bugcheck", BugcheckCatalog.Name(0xBEEF));
        Assert.Null(BugcheckCatalog.Hint(0xBEEF));
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min 0 s")]
    [InlineData(322, "5 min 22 s")]
    [InlineData(3600, "1 h 0 min")]
    [InlineData(5000, "1 h 23 min")]
    [InlineData(86400, "1 d 0 h")]
    [InlineData(5356800, "62 d 0 h")]
    public void Durations_are_humanised(int seconds, string expected) =>
        Assert.Equal(expected, Format.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Negative_and_missing_durations_are_handled()
    {
        Assert.Equal("5 min 0 s", Format.Duration(TimeSpan.FromMinutes(-5)));
        Assert.Equal("unknown", Format.Duration(null));
    }

    [Fact]
    public void Every_category_has_a_label_and_a_colour_for_both_themes()
    {
        foreach (var c in Enum.GetValues<RebootCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(Format.Category(c)), $"{c} has no label");
            Assert.Matches("^#[0-9A-F]{6}$", CategoryPalette.Hex(c, dark: false));
            Assert.Matches("^#[0-9A-F]{6}$", CategoryPalette.Hex(c, dark: true));
            Assert.NotEqual(CategoryPalette.Hex(c, false), CategoryPalette.Hex(c, true));
        }
    }

    [Fact]
    public void Friendly_process_names_strip_paths_and_keep_case_for_unknown_exes()
    {
        Assert.Equal("Windows Update (Windows Modules Installer)", RebootAnalyzer.FriendlyProcess(@"C:\Windows\servicing\TrustedInstaller.exe (LUNCHBOX)"));
        Assert.Equal("the Start menu power button", RebootAnalyzer.FriendlyProcess(@"C:\Windows\Explorer.EXE (LUNCHBOX)"));
        Assert.Equal("Windows first-run setup (OOBE)", RebootAnalyzer.FriendlyProcess(@"C:\Windows\System32\oobe\CloudExperienceHostBroker.exe (LUNCHBOX)"));
        Assert.Equal("MyTool.exe", RebootAnalyzer.FriendlyProcess(@"D:\Apps\MyTool.exe (LUNCHBOX)"));
        Assert.Equal("an unknown process", RebootAnalyzer.FriendlyProcess(""));
    }

    [Fact]
    public void Raw_event_falls_back_to_ordered_values()
    {
        var e = new RawEvent { Time = DateTime.Now, Id = 6008, Provider = "EventLog", Log = "System", Ordered = new() { "3:30:17 PM", "9/13/2026" } };
        Assert.Equal("3:30:17 PM", e.Get("param1", 0));
        Assert.Equal("9/13/2026", e.Get("param2", 1));
        Assert.Equal("", e.Get("param3", 2));
        Assert.True(e.Is("eventlog", 6008));
        Assert.False(e.Is("EventLog", 6005));
    }
}
