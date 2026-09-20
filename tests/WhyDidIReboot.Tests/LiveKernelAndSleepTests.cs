using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class LiveKernelAndSleepTests
{
    private static string Detail(RebootEntry e, string key) =>
        e.Details.FirstOrDefault(d => d.Key == key).Value ?? "";

    [Fact]
    public void Repeated_wer_reports_for_one_fault_collapse_to_one_entry()
    {
        const string dump = @"C:\Windows\LiveKernelReports\ResourceTimeout\ResourceTimeout-20260712-1948.dmp";
        var events = new List<RawEvent>
        {
            Boot(T0),
            LiveKernelEvent(T0.AddHours(1), "1cc", dump, "r1"),
            LiveKernelEvent(T0.AddDays(1), "1cc", dump, "r1"),
            LiveKernelEvent(T0.AddDays(30), "1cc", dump, "r1"),
        };

        var lke = RebootAnalyzer.Build(events).Where(e => e.Category == RebootCategory.LiveKernelEvent).ToList();

        var e = Assert.Single(lke);
        Assert.Equal("Kernel problem recorded without a reboot (EXRESOURCE_TIMEOUT_LIVEDUMP)", e.Title);
        Assert.Equal(new DateTime(2026, 7, 12, 19, 48, 0), e.Timestamp);   // from the dump file name, not the report time
        Assert.Equal(dump, e.DumpPath);
        Assert.False(e.IsReboot);
        Assert.Contains("2 more time(s)", Detail(e, "Report re-filed by Windows Error Reporting"));
    }

    [Fact]
    public void Different_faults_stay_separate_and_unknown_codes_are_shown_as_hex()
    {
        var events = new List<RawEvent>
        {
            LiveKernelEvent(T0, "141", @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260801-1010.dmp", "a"),
            LiveKernelEvent(T0.AddMinutes(1), "abc", @"C:\Windows\LiveKernelReports\X-20260801-1011.dmp", "b"),
        };

        var lke = RebootAnalyzer.Build(events).Where(e => e.Category == RebootCategory.LiveKernelEvent).OrderBy(e => e.Timestamp).ToList();

        Assert.Equal(2, lke.Count);
        Assert.Contains("VIDEO_ENGINE_TIMEOUT_DETECTED", lke[0].Title);
        Assert.Contains("code 0xABC", lke[1].Title);
    }

    [Fact]
    public void Sleep_followed_by_wake_becomes_one_cycle()
    {
        var slept = T0.AddHours(1);
        var woke = slept.AddHours(2);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Sleep(slept, reason: "4"),
            Wake(woke.AddSeconds(1), slept, woke, "Power Button"),
        };

        var e = Assert.Single(RebootAnalyzer.Build(events), x => x.Category == RebootCategory.Sleep);
        Assert.Equal("Slept for 2 h 0 min", e.Title);
        Assert.Equal(slept, e.Timestamp);
        Assert.Contains("the idle timeout", e.Summary);
        Assert.Contains("Power Button", e.Summary);
        Assert.Equal("2 h 0 min", Detail(e, "Asleep for"));
        Assert.Equal(2, e.Evidence.Count);
    }

    [Fact]
    public void Sleep_without_a_wake_before_the_next_boot_is_reported()
    {
        var slept = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Sleep(slept, reason: "0"),
            LogStopped(slept.AddHours(5)), ShuttingDown(slept.AddHours(5)),
            Boot(slept.AddHours(6)),
            Wake(slept.AddHours(7), slept.AddHours(6).AddMinutes(30), slept.AddHours(7), "Unknown"),   // belongs to the next session
        };

        var sleeps = RebootAnalyzer.Build(events).Where(x => x.Category == RebootCategory.Sleep).ToList();

        var e = Assert.Single(sleeps);
        Assert.Equal("Went to sleep and did not wake before the next boot", e.Title);
        Assert.Contains("power button, sleep key or lid", e.Summary);
    }

    [Fact]
    public void Wake_source_falls_back_to_the_type_code_when_text_is_empty()
    {
        var slept = T0.AddHours(1);
        var woke = slept.AddMinutes(45);
        var events = new List<RawEvent> { Sleep(slept), Wake(woke, slept, woke, "") };

        var e = Assert.Single(RebootAnalyzer.Build(events), x => x.Category == RebootCategory.Sleep);
        Assert.Equal("Power button", Detail(e, "Wake source"));
    }
}
