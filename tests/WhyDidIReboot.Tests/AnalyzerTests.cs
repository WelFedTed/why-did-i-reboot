using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class AnalyzerTests
{
    private const string TrustedInstaller = @"C:\Windows\servicing\TrustedInstaller.exe (LUNCHBOX)";
    private const string Explorer = @"C:\Windows\Explorer.EXE (LUNCHBOX)";
    private const string Winlogon = @"C:\Windows\system32\winlogon.exe (LUNCHBOX)";
    private const string System = @"NT AUTHORITY\SYSTEM";
    private const string Bucky = @"LUNCHBOX\bucky";

    private static List<RebootEntry> Reboots(IEnumerable<RawEvent> events) =>
        RebootAnalyzer.Build(events.OrderBy(e => e.Time).ThenBy(e => e.RecordId).ToList())
            .Where(e => e.IsReboot).OrderBy(e => e.Timestamp).ToList();

    private static string Detail(RebootEntry e, string key) =>
        e.Details.FirstOrDefault(d => d.Key == key).Value ?? "";

    [Fact]
    public void Oldest_boot_with_nothing_before_it_is_unknown()
    {
        var entries = Reboots(new[] { Boot(T0), BootType(T0.AddSeconds(1), "0") });

        var e = Assert.Single(entries);
        Assert.Equal(RebootCategory.Unknown, e.Category);
        Assert.Equal(T0, e.BootTime);
        Assert.Null(e.ShutdownTime);
        Assert.Contains("oldest boot", e.Summary);
    }

    [Fact]
    public void Windows_update_restart_is_recognised_from_trusted_installer()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), TrustedInstaller, System, "Operating System: Upgrade (Planned)", code: "0x80020003")));

        var e = Assert.Single(Reboots(events), x => x.BootTime > T0);
        Assert.Equal(RebootCategory.WindowsUpdate, e.Category);
        Assert.Equal("Restarted to finish installing Windows updates", e.Title);
        Assert.Equal(shutdown, e.ShutdownTime);
        Assert.Equal(shutdown, e.Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(30), e.Downtime);
        Assert.Equal(TimeSpan.FromHours(2), e.PreviousUptime);
        Assert.Equal(TrustedInstaller, Detail(e, "Requested by"));
        Assert.Contains(e.Evidence, v => v.Id == 1074);
    }

    [Fact]
    public void Windows_update_restart_names_updates_installed_shortly_before()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent>
        {
            Boot(T0),
            UpdateInstalled(shutdown.AddMinutes(-40), "2026-09 Cumulative Update for Windows 11 (KB5099999)"),
            UpdateInstalled(shutdown.AddMinutes(-35), "9NRZT3Q9R3DL-Microsoft.WindowsAppRuntime.2"),   // Store app, must be ignored
            UpdateInstalled(shutdown.AddHours(-5), "Old update (KB5000000)"),                         // too long ago
        };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), TrustedInstaller, System, "Operating System: Upgrade (Planned)")));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.WindowsUpdate, e.Category);
        Assert.Contains("KB5099999", e.Summary);
        Assert.DoesNotContain("WindowsAppRuntime", e.Summary);
        Assert.DoesNotContain("KB5000000", e.Summary);
        var u = Assert.Single(e.Updates);
        Assert.Equal("KB5099999", u.Kb);
        Assert.Equal("https://support.microsoft.com/help/5099999", u.Url);
    }

    [Fact]
    public void User_restart_from_explorer_names_the_user()
    {
        var shutdown = T0.AddMinutes(90);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-10), Explorer, Bucky, "Other (Unplanned)", comment: "testing")));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.UserInitiated, e.Category);
        Assert.Equal("bucky restarted the PC", e.Title);
        Assert.Contains("Start menu power button", e.Summary);
        Assert.Contains("\"testing\"", e.Summary);
        Assert.Equal(Bucky, Detail(e, "User"));
    }

    [Fact]
    public void User_shutdown_uses_the_shutdown_verb()
    {
        var shutdown = T0.AddMinutes(90);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-10), Explorer, Bucky, "Other (Unplanned)", action: "power off"), TimeSpan.FromHours(8)));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.UserInitiated, e.Category);
        Assert.Equal("bucky powered off the PC", e.Title);
        Assert.Equal(TimeSpan.FromHours(8), e.Downtime);
    }

    [Fact]
    public void Sign_in_screen_restart_by_system_is_not_attributed_to_a_person()
    {
        var shutdown = T0.AddMinutes(4);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-3), Winlogon, System, "No title for this reason could be found", code: "0x500ff")));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.Application, e.Category);
        Assert.Equal("Windows restarted itself from the sign-in screen", e.Title);
        Assert.DoesNotContain("someone", e.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Other_process_is_reported_as_application_with_its_name()
    {
        var shutdown = T0.AddMinutes(30);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-3), @"C:\Program Files\Vendor\Installer.exe (LUNCHBOX)", Bucky, "Application: Installation (Planned)", code: "0x80040002")));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.Application, e.Category);
        Assert.Equal("Installer.exe restarted the PC", e.Title);
        Assert.Contains("on behalf of bucky", e.Summary);
        Assert.Contains("Application: Installation (Planned)", e.Summary);
    }

    [Fact]
    public void Clean_shutdown_without_a_request_is_clean_no_reason()
    {
        var shutdown = T0.AddHours(1);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, request: null));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.CleanNoReason, e.Category);
        Assert.Equal("Shut down cleanly, reason not recorded", e.Title);
        Assert.Equal(shutdown, e.ShutdownTime);
    }

    [Fact]
    public void Fast_startup_hybrid_shutdown_is_called_out()
    {
        var shutdown = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0),
            LogStopped(shutdown.AddSeconds(-1)), ShuttingDown(shutdown),
            Boot(shutdown.AddMinutes(1)), BootType(shutdown.AddMinutes(1).AddSeconds(1), "1"),
        };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.CleanNoReason, e.Category);
        Assert.Equal("Shut down cleanly (fast startup)", e.Title);
        Assert.StartsWith("Fast startup", Detail(e, "Boot type"));
    }

    [Fact]
    public void Request_logged_long_before_the_shutdown_is_ignored()
    {
        var shutdown = T0.AddHours(3);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddHours(-2), Explorer, Bucky, "Other (Unplanned)")));

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.CleanNoReason, e.Category);
    }

    [Fact]
    public void Blue_screen_is_decoded_with_stop_code_and_dump()
    {
        var boot = T0.AddHours(1);
        // A path that cannot exist on the machine running the tests, so "Dump file present" is deterministic.
        var dump = @"C:\Windows\Minidump\" + Guid.NewGuid().ToString("N") + ".dmp";
        var events = new List<RawEvent>
        {
            Boot(T0),
            Boot(boot), BootType(boot.AddMilliseconds(300), "0"),
            KernelPower41(boot.AddSeconds(2), 159),
            Bugcheck1001(boot.AddSeconds(7), "0x0000009f", dump, "d1386807-5984-4a9d-8956-dac2a3f565c1"),
            WerBlueScreen(boot.AddDays(3), "d1386807-5984-4a9d-8956-dac2a3f565c1"),
        };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.BlueScreen, e.Category);
        Assert.Equal("Crashed with a blue screen: DRIVER_POWER_STATE_FAILURE", e.Title);
        Assert.Contains("STOP 0x0000009F", e.Summary);
        Assert.Equal("0x0000009F (DRIVER_POWER_STATE_FAILURE)", Detail(e, "STOP code"));
        Assert.Equal(dump, e.DumpPath);
        Assert.Equal("No", Detail(e, "Dump file present"));
        Assert.Null(e.ShutdownTime);
        Assert.Equal(boot, e.Timestamp);
        Assert.Contains(e.Evidence, v => v.Log == "Application" && v.Id == 1001);   // linked WER report
    }

    [Fact]
    public void Blue_screen_during_a_requested_restart_says_so()
    {
        var shutdown = T0.AddHours(1);
        var boot = shutdown.AddMinutes(5);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Requested(shutdown.AddSeconds(-50), Explorer, Bucky, "Other (Unplanned)"),
            LogStopped(shutdown),
            Boot(boot), KernelPower41(boot.AddSeconds(2), 159),
        };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.BlueScreen, e.Category);
        Assert.Contains("while a restart requested by the Start menu power button was in progress", e.Summary);
        Assert.Equal(shutdown, e.ShutdownTime);
        Assert.Equal(TimeSpan.FromMinutes(5), e.Downtime);
    }

    [Fact]
    public void Bugcheck_record_alone_is_enough_for_a_blue_screen()
    {
        var boot = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Boot(boot), KernelPower41(boot.AddSeconds(2), 0),
            Bugcheck1001(boot.AddSeconds(7), "0x00000133", @"C:\Windows\Minidump\x.dmp", "r"),
        };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.BlueScreen, e.Category);
        Assert.Contains("DPC_WATCHDOG_VIOLATION", e.Title);
    }

    [Fact]
    public void Dirty_shutdown_without_bugcheck_is_power_loss()
    {
        var boot = T0.AddHours(1);
        var events = new List<RawEvent> { Boot(T0), Boot(boot), KernelPower41(boot.AddSeconds(2), 0) };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.Unexpected, e.Category);
        Assert.Equal("Lost power, froze, or was reset", e.Title);
        Assert.Equal("No", Detail(e, "Power button held"));
        Assert.Equal("Not recorded (unexpected stop)", Detail(e, "Shut down at"));
    }

    [Fact]
    public void Held_power_button_is_detected()
    {
        var boot = T0.AddHours(1);
        var events = new List<RawEvent> { Boot(T0), Boot(boot), KernelPower41(boot.AddSeconds(2), 0, powerButton: true) };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.Unexpected, e.Category);
        Assert.Equal("Forced off with the power button", e.Title);
        Assert.Equal("Yes", Detail(e, "Power button held"));
    }

    [Fact]
    public void Power_lost_while_asleep_is_detected_from_the_last_sleep_event()
    {
        var boot = T0.AddHours(3);
        var events = new List<RawEvent>
        {
            Boot(T0),
            Sleep(T0.AddHours(1)),                 // slept and never woke
            Boot(boot), KernelPower41(boot.AddSeconds(2), 0),
        };

        var e = Reboots(events).Last();
        Assert.Equal(RebootCategory.Unexpected, e.Category);
        Assert.Equal("Lost power while asleep or going to sleep", e.Title);
    }

    [Fact]
    public void Resume_from_hibernation_is_not_a_reboot()
    {
        var resume = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0),
            LogStopped(resume.AddMinutes(-30)), ShuttingDown(resume.AddMinutes(-30)),
            Boot(resume), BootType(resume.AddSeconds(1), "2"),
        };

        var all = RebootAnalyzer.Build(events);
        var e = all.Single(x => x.BootTime == resume);
        Assert.Equal(RebootCategory.Sleep, e.Category);
        Assert.Equal("Resumed from hibernation", e.Title);
        Assert.False(e.IsReboot);
    }

    [Fact]
    public void Event_log_service_start_is_used_as_a_boot_when_kernel_general_is_missing()
    {
        var shutdown = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            LogStarted(T0),
            LogStopped(shutdown),
            LogStarted(shutdown.AddSeconds(40)),
        };

        var entries = Reboots(events);
        Assert.Equal(2, entries.Count);
        Assert.Equal(RebootCategory.CleanNoReason, entries[1].Category);
        Assert.Equal(shutdown, entries[1].ShutdownTime);
        Assert.Equal(TimeSpan.FromSeconds(40), entries[1].Downtime);
    }

    [Fact]
    public void Event_log_service_start_near_a_kernel_boot_does_not_create_a_second_boot()
    {
        var events = new List<RawEvent> { Boot(T0), LogStarted(T0.AddSeconds(10)) };

        Assert.Single(Reboots(events));
    }

    [Fact]
    public void Evidence_is_chronological_and_unique()
    {
        var shutdown = T0.AddHours(2);
        var events = new List<RawEvent> { Boot(T0) };
        events.AddRange(CleanRestart(shutdown, Requested(shutdown.AddSeconds(-6), TrustedInstaller, System, "Operating System: Upgrade (Planned)")));

        var e = Reboots(events).Last();
        var times = e.Evidence.Select(v => v.Time).ToList();
        Assert.Equal(times.OrderBy(t => t), times);
        Assert.Equal(e.Evidence.Count, e.Evidence.Distinct().Count());
    }
}
