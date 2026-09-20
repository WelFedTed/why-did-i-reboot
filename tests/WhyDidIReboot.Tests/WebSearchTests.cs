using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class WebSearchTests
{
    [Fact]
    public void Crash_query_has_the_bugcheck_name_short_code_and_driver_names()
    {
        var e = new RebootEntry
        {
            Timestamp = T0, Category = RebootCategory.BlueScreen, Title = "t",
            Summary = "A driver did not respond. iaStorAC.sys (Intel Rapid Storage (RST) driver) was involved.",
            Details = new() { new("STOP code", "0x0000009F (DRIVER_POWER_STATE_FAILURE)"), new("Crash dump", @"C:\Windows\Minidump\x.dmp") },
            Evidence = new() { new(T0, "System", 1001, "BugCheck", "Probably caused by pci.sys and iaStorAC.sys again"), new(T0, "System", 41, "Kernel-Power", "ntoskrnl.sys should be ignored") },
        };

        Assert.Equal("DRIVER_POWER_STATE_FAILURE 0x9F iaStorAC.sys pci.sys", e.SearchQuery);
        Assert.True(e.HasSearchQuery);
    }

    [Fact]
    public void Live_kernel_event_query_uses_its_code_and_drivers_are_capped_at_three()
    {
        var e = new RebootEntry
        {
            Timestamp = T0, Category = RebootCategory.LiveKernelEvent, Title = "t", Summary = "a.sys b.sys c.sys d.sys",
            Details = new() { new("Live kernel event code", "0x1CC (EXRESOURCE_TIMEOUT_LIVEDUMP)") },
        };
        Assert.Equal("EXRESOURCE_TIMEOUT_LIVEDUMP 0x1CC a.sys b.sys c.sys", e.SearchQuery);
    }

    [Fact]
    public void Cards_without_a_code_have_no_query_and_unknown_codes_search_for_bugcheck()
    {
        var user = new RebootEntry { Timestamp = T0, Category = RebootCategory.UserInitiated, Title = "t", Summary = "bucky restarted" };
        Assert.Null(user.SearchQuery);
        Assert.False(user.HasSearchQuery);

        var unknown = new RebootEntry
        {
            Timestamp = T0, Category = RebootCategory.BlueScreen, Title = "t", Summary = "s",
            Details = new() { new("STOP code", "0x0000BEEF (Unknown bugcheck)") },
        };
        Assert.Null(unknown.SearchQuery);   // "(Unknown bugcheck)" has a space, so it is not a symbolic name; nothing useful to search
    }

    [Fact]
    public void Analyzer_output_for_the_july_style_crash_produces_a_query()
    {
        var boot = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0), Boot(boot),
            KernelPower41(boot.AddSeconds(2), 159),
            Bugcheck1001(boot.AddSeconds(7), "0x0000009f", @"C:\Windows\Minidump\" + Guid.NewGuid().ToString("N") + ".dmp", "r"),
        };
        var e = RebootAnalyzer.Build(events).Single(x => x.Category == RebootCategory.BlueScreen);
        Assert.Equal("DRIVER_POWER_STATE_FAILURE 0x9F", e.SearchQuery);
    }

    [Fact]
    public void Engines_build_urls_and_labels_and_default_to_google()
    {
        Assert.Equal("https://www.google.com/search?q=DRIVER_POWER_STATE_FAILURE%200x9F", WebSearch.EngineByName("Google").Url("DRIVER_POWER_STATE_FAILURE 0x9F"));
        Assert.Equal("https://duckduckgo.com/?q=a%20b", WebSearch.EngineByName("duckduckgo").Url("a b"));
        Assert.Equal("Search Bing", WebSearch.EngineByName("Bing").ButtonLabel);
        Assert.Equal("Google", WebSearch.EngineByName(null).Name);
        Assert.Equal("Google", WebSearch.EngineByName("nope").Name);
        Assert.Equal(5, WebSearch.Engines.Count);
    }
}
