using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class CrashAnalysisTests
{
    private const string SampleLog = """
        Opened log file 'C:\Temp\x.analyze.txt'
        *******************************************************************************
        *                        Bugcheck Analysis                                    *
        *******************************************************************************

        DRIVER_POWER_STATE_FAILURE (9f)
        A driver has failed to complete a power IRP within a specific time.
        Arguments:
        Arg1: 0000000000000004, The power transition timed out waiting to synchronize with the Pnp
        Arg2: 000000000000012c, Timeout in seconds.

        KEY_VALUES_STRING: 1

            Key  : Analysis.CPU.mSec
            Value: 2109

        BUGCHECK_CODE:  9f

        BUGCHECK_P1: 4

        BUGCHECK_P2: 12c

        BUGCHECK_P3: ffffc30df3df2040

        BUGCHECK_P4: ffffd40cf206f5d0

        FAULTING_THREAD:  ffffc30df3df2040

        PROCESS_NAME:  System

        STACK_TEXT:
        ffffd40c`f206f5a8 fffff804`8358b800     : 0000000000000004 : nt!KeBugCheckEx
        ffffd40c`f206f5b0 fffff804`83400000     : ffffc30df3df2040 : nt!PopIrpWatchdogBugcheck+0x9e
        ffffd40c`f206f610 fffff804`83410000     : 0000000000000000 : nt!PopIrpWatchdog+0x3b
        ffffd40c`f206f660 fffff804`83420000     : 0000000000000000 : nt!KiProcessExpiredTimerList+0x1ac


        SYMBOL_NAME:  Netwtw10!unknown_function+0

        MODULE_NAME: Netwtw10

        IMAGE_NAME:  Netwtw10.sys

        STACK_COMMAND:  .process /r /p 0xffffc30df3df2040; .thread 0xffffc30df3df2040 ; kb

        FAILURE_BUCKET_ID:  0x9F_4_POWER_DOWN_Netwtw10!unknown_function

        OSPLATFORM_TYPE:  x64

        FAILURE_ID_HASH:  {2e4c6a2d-0000-0000-0000-000000000000}

        Followup:     MachineOwner
        ---------
        Probably caused by : Netwtw10.sys ( Netwtw10!unknown_function+0 )
        Closing open log file C:\Temp\x.analyze.txt
        """;

    [Fact]
    public void Summary_keeps_the_cause_key_fields_and_stack_and_drops_the_noise()
    {
        var s = CrashAnalysis.Summarize(SampleLog);

        Assert.StartsWith("Probably caused by : Netwtw10.sys", s);
        Assert.Contains("BUGCHECK_CODE: 9f", s);
        Assert.Contains("BUGCHECK_P1: 4", s);
        Assert.Contains("MODULE_NAME: Netwtw10", s);
        Assert.Contains("IMAGE_NAME: Netwtw10.sys", s);
        Assert.Contains("FAILURE_BUCKET_ID: 0x9F_4_POWER_DOWN_Netwtw10!unknown_function", s);
        Assert.Contains("STACK_TEXT:", s);
        Assert.Contains("nt!PopIrpWatchdogBugcheck+0x9e", s);
        Assert.DoesNotContain("Analysis.CPU.mSec", s);
        Assert.DoesNotContain("Opened log file", s);
        Assert.DoesNotContain("Followup", s);
        Assert.DoesNotContain("FAULTING_THREAD", s);   // not in the key list
    }

    [Fact]
    public void Summary_limits_stack_lines_and_total_length()
    {
        var s = CrashAnalysis.Summarize(SampleLog, stackLines: 2);
        Assert.Contains("nt!KeBugCheckEx", s);
        Assert.Contains("nt!PopIrpWatchdogBugcheck", s);
        Assert.DoesNotContain("nt!PopIrpWatchdog+0x3b", s);

        var tiny = CrashAnalysis.Summarize(SampleLog, maxChars: 80);
        Assert.True(tiny.Length <= 80 + "\n…(trimmed)".Length);
        Assert.EndsWith("…(trimmed)", tiny);
    }

    [Fact]
    public void Unrecognised_output_falls_back_to_its_tail_and_empty_input_is_empty()
    {
        var junk = string.Concat(Enumerable.Repeat("some unrelated debugger chatter\n", 300)) + "THE END";
        var s = CrashAnalysis.Summarize(junk, maxChars: 100);
        Assert.StartsWith("…", s);
        Assert.EndsWith("THE END", s);
        Assert.Equal("", CrashAnalysis.Summarize(""));
    }

    [Fact]
    public void Prompt_contains_instructions_the_card_and_the_analysis()
    {
        var p = CrashAnalysis.BuildPrompt("[2026-07-12 20:28:55]  CRASHED WITH A BLUE SCREEN", "Probably caused by : Netwtw10.sys");
        Assert.Contains("what most likely caused the crash", p);
        Assert.Contains("=== Crash summary (Why Did I Reboot) ===", p);
        Assert.Contains("CRASHED WITH A BLUE SCREEN", p);
        Assert.Contains("=== WinDbg !analyze -v (key sections) ===", p);
        Assert.Contains("Netwtw10.sys", p);
    }

    [Fact]
    public void Services_build_prefilled_urls_and_default_to_chatgpt()
    {
        var chatgpt = CrashAnalysis.ServiceByName("ChatGPT");
        Assert.Equal("https://chatgpt.com/?q=hello%20world%20%26%20co", chatgpt.Url("hello world & co"));
        Assert.False(chatgpt.NeedsSignIn);
        Assert.Equal("ChatGPT", CrashAnalysis.ServiceByName(null).Name);
        Assert.Equal("ChatGPT", CrashAnalysis.ServiceByName("nonsense").Name);
        Assert.Equal("Claude", CrashAnalysis.ServiceByName("claude").Name);
        Assert.True(CrashAnalysis.ServiceByName("Claude").NeedsSignIn);
        Assert.Equal(4, CrashAnalysis.Services.Count);
    }

    [Fact]
    public void Url_is_shrunk_to_fit_and_says_so()
    {
        var service = CrashAnalysis.ServiceByName("ChatGPT");
        var analysis = string.Concat(Enumerable.Repeat("STACK_TEXT line with some symbols nt!KeBugCheckEx+0x1234\n", 400));   // ~24 KB

        var (url, prompt, trimmed) = CrashAnalysis.BuildUrl(service, "card text", analysis);

        Assert.True(trimmed);
        Assert.True(url.Length <= CrashAnalysis.MaxUrlLength, url.Length.ToString());
        Assert.Contains("(trimmed; the full text is on the clipboard)", prompt);
        Assert.StartsWith("https://chatgpt.com/?q=", url);

        var (shortUrl, _, shortTrimmed) = CrashAnalysis.BuildUrl(service, "card", "short analysis");
        Assert.False(shortTrimmed);
        Assert.Contains("short%20analysis", shortUrl);
    }

    [Fact]
    public void Analyze_to_log_arguments_open_log_analyse_close_and_quit_without_inner_quotes()
    {
        var a = WinDbgLocator.AnalyzeToLogArguments(@"C:\Windows\Minidump\x.dmp", @"C:\Users\Public\WhyDidIReboot\analysis\x.analyze.txt");
        Assert.Equal("-z \"C:\\Windows\\Minidump\\x.dmp\" -c \".logopen C:\\Users\\Public\\WhyDidIReboot\\analysis\\x.analyze.txt; !analyze -v; .logclose; qq\"", a);
        Assert.DoesNotContain("\\\"", a);   // escaped quotes broke WinDbg's -c parsing

        Assert.Throws<ArgumentException>(() => WinDbgLocator.AnalyzeToLogArguments(@"C:\x.dmp", @"C:\Users\John Smith\x.txt"));
        Assert.Throws<ArgumentException>(() => WinDbgLocator.AnalyzeToLogArguments(@"C:\x.dmp", "C:\\x\"y.txt"));
    }

    [Fact]
    public void Analysis_folder_avoids_spaces_and_prefers_the_public_profile()
    {
        string? Env(string k) => k switch
        {
            "PUBLIC" => @"C:\Users\Public",
            "ProgramData" => @"C:\ProgramData",
            "TEMP" => @"C:\Users\John Smith\AppData\Local\Temp",
            _ => null,
        };
        Assert.Equal(@"C:\Users\Public\WhyDidIReboot\analysis", WinDbgLocator.AnalysisWorkDir(_ => true, Env));
        Assert.Equal(@"C:\ProgramData\WhyDidIReboot\analysis", WinDbgLocator.AnalysisWorkDir(d => !d.StartsWith(@"C:\Users\Public"), Env));
        // The temp folder has a space, so it is skipped even when writable; the last resort is the drive root.
        Assert.Equal(@"C:\WhyDidIReboot\analysis", WinDbgLocator.AnalysisWorkDir(d => d.StartsWith(@"C:\Why") || d.Contains("Temp"), Env));
        Assert.Null(WinDbgLocator.AnalysisWorkDir(_ => false, Env));
    }

    [Fact]
    public void Log_file_name_comes_from_the_dump_and_is_safe_for_the_command_line()
    {
        Assert.Equal("071226-7000-01.analyze.txt", WinDbgLocator.LogFileName(@"C:\Windows\Minidump\071226-7000-01.dmp"));
        Assert.Equal("my_dump_file.analyze.txt", WinDbgLocator.LogFileName(@"D:\Old PC\my dump;file.dmp"));
        Assert.Equal("dump.analyze.txt", WinDbgLocator.LogFileName(@"C:\.dmp"));
    }

    [Fact]
    public async Task Waiting_for_a_log_returns_its_text_once_it_is_closed_and_stable()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wdir-log-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var wait = WinDbgLocator.WaitForLogAsync(path, TimeSpan.FromSeconds(20));
            await Task.Delay(1200);
            await System.IO.File.WriteAllTextAsync(path, "analysis output");
            var text = await wait;
            Assert.Equal("analysis output", text);

            Assert.Null(await WinDbgLocator.WaitForLogAsync(path + ".missing", TimeSpan.FromSeconds(2)));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
