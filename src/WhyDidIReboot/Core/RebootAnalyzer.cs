using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace WhyDidIReboot.Core;

/// <summary>Turns raw event log records into one plain-English entry per reboot.</summary>
public static class RebootAnalyzer
{
    private static readonly TimeSpan PostBootWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestWindow = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan UpdateWindow = TimeSpan.FromHours(3);

    private static readonly Regex StoreAppUpdate = new(@"^9[A-Z0-9]{11}-", RegexOptions.Compiled);
    private static readonly Regex DumpDate = new(@"(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})\.dmp$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] UpdateProcesses =
    {
        "trustedinstaller.exe", "tiworker.exe", "mousocoreworker.exe", "usoclient.exe", "musnotification.exe",
        "musnotifyicon.exe", "wuauclt.exe", "setuphost.exe", "setupprep.exe", "waasmedicagent.exe", "sihclient.exe",
        "updateassistant.exe", "windows10upgraderapp.exe", "windowsupdatebox.exe", "usocoreworker.exe",
    };

    private static readonly string[] UserProcesses =
    {
        "explorer.exe", "winlogon.exe", "systemsettings.exe", "logonui.exe", "shellexperiencehost.exe",
        "startmenuexperiencehost.exe", "consent.exe", "securityhealthsystray.exe",
    };

    public static AnalysisResult Analyze(int? days) => Analyze(days, LogLocation.Local);

    /// <summary>Reads and analyses either this PC's logs or the .evtx files of another installation.</summary>
    public static AnalysisResult Analyze(int? days, LogLocation location)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var events = EventLogSource.Read(location, days, warnings, out var read);

        // Windows Update history describes updates installed on *this* PC; it says nothing about another drive.
        var options = new AnalysisOptions
        {
            MapPath = location.MapPath,
            UpdateDetails = location.IsOffline ? AnalysisOptions.Default.UpdateDetails : WindowsUpdateHistory.Load(warnings),
        };
        var entries = Build(events, options);

        var bootTime = location.IsOffline
            ? entries.Where(e => e.IsReboot && e.BootTime is not null).Select(e => e.BootTime!.Value).DefaultIfEmpty(DateTime.MinValue).Max()
            : DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new AnalysisResult
        {
            Entries = entries.OrderByDescending(e => e.Timestamp).ToList(),
            RecordsRead = read,
            Elapsed = sw.Elapsed,
            CurrentBootTime = bootTime,
            Source = location,
            Warnings = warnings,
        };
    }

    /// <summary>Pure function over an ascending list of events, so it can be exercised without a live log.</summary>
    public static List<RebootEntry> Build(List<RawEvent> events) => Build(events, AnalysisOptions.Default);

    public static List<RebootEntry> Build(List<RawEvent> events, AnalysisOptions options)
    {
        var entries = new List<RebootEntry>();

        // A boot is a Kernel-General 12. Fall back to EventLog 6005 when 12 is missing (older logs).
        var boots = events.Where(e => e.Is(EventLogSource.KernelGeneral, 12)).ToList();
        foreach (var e in events.Where(e => e.Is(EventLogSource.EventLogSvc, 6005)))
        {
            if (!boots.Any(b => (b.Time - e.Time).Duration() < TimeSpan.FromMinutes(10)))
                boots.Add(e);
        }
        boots.Sort((a, b) => a.Time.CompareTo(b.Time));

        for (var i = 0; i < boots.Count; i++)
        {
            var boot = boots[i];
            var prevBoot = i > 0 ? boots[i - 1] : null;
            var nextBootTime = i + 1 < boots.Count ? boots[i + 1].Time : DateTime.MaxValue;

            var before = events.Where(e => e.Time < boot.Time && (prevBoot is null || e.Time >= prevBoot.Time)).ToList();
            var after = events.Where(e => e.Time >= boot.Time && e.Time < nextBootTime).ToList();

            entries.Add(DescribeBoot(boot, prevBoot, before, after, events, options));
        }

        entries.AddRange(LiveKernelEvents(events, options));
        entries.AddRange(SleepCycles(events, boots));
        return entries;
    }

    // ---------------------------------------------------------------- reboots

    private static RebootEntry DescribeBoot(RawEvent boot, RawEvent? prevBoot, List<RawEvent> before, List<RawEvent> after, List<RawEvent> all, AnalysisOptions options)
    {
        var links = new List<LinkItem>();
        var postLimit = boot.Time + PostBootWindow;
        var kp41 = after.FirstOrDefault(e => e.Is(EventLogSource.KernelPower, 41) && e.Time <= postLimit);
        var bugcheck = after.FirstOrDefault(e => (e.Is(EventLogSource.BugCheck, 1001) || e.Is(EventLogSource.BugCheckLegacy, 1001)) && e.Time <= postLimit);
        var e6008 = after.FirstOrDefault(e => e.Is(EventLogSource.EventLogSvc, 6008) && e.Time <= postLimit);
        var kb27 = after.FirstOrDefault(e => e.Is(EventLogSource.KernelBoot, 27) && e.Time <= postLimit);
        var e1076 = after.FirstOrDefault(e => e.Is(EventLogSource.User32, 1076));

        var kg13 = before.LastOrDefault(e => e.Is(EventLogSource.KernelGeneral, 13));
        var e6006 = before.LastOrDefault(e => e.Is(EventLogSource.EventLogSvc, 6006));
        var kp109 = before.LastOrDefault(e => e.Is(EventLogSource.KernelPower, 109));
        var e1074 = before.LastOrDefault(e => e.Is(EventLogSource.User32, 1074));
        var lastSleep = before.LastOrDefault(e => e.Is(EventLogSource.KernelPower, 42));
        var lastWake = before.LastOrDefault(e => e.Is(EventLogSource.PowerTroubleshooter, 1));

        DateTime? shutdownTime = kg13?.Time ?? e6006?.Time ?? kp109?.Time;
        if (shutdownTime is null && e6008 is not null) shutdownTime = ParseUnexpectedShutdownTime(e6008);
        if (e1074 is not null && shutdownTime is not null && shutdownTime.Value - e1074.Time > RequestWindow) e1074 = null;
        if (e1074 is not null && shutdownTime is null && boot.Time - e1074.Time > RequestWindow) e1074 = null;

        var details = new List<KeyValuePair<string, string>>();
        var evidence = new List<EvidenceEvent>();
        void Detail(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) details.Add(new(k, v)); }
        void Cite(RawEvent? e) { if (e is not null) evidence.Add(e.ToEvidence()); }

        var bootType = kb27?.Get("BootType", 0);
        var bootTypeText = bootType switch
        {
            "0" => "Full (cold) boot",
            "1" => "Fast startup (hybrid boot: the previous shutdown was really a hibernation of the kernel)",
            "2" => "Resumed from hibernation",
            _ => null,
        };

        TimeSpan? previousUptime = prevBoot is not null && shutdownTime is not null ? shutdownTime - prevBoot.Time : null;
        TimeSpan? downtime = shutdownTime is not null ? boot.Time - shutdownTime : null;

        var updates = before
            .Where(e => e.Is(EventLogSource.WindowsUpdateClient, 19) || e.Is(EventLogSource.WindowsUpdateClient, 20))
            .Where(e => (shutdownTime ?? boot.Time) - e.Time <= UpdateWindow)
            .Select(e => (Event: e, Title: e.Get("updateTitle", 0), Failed: e.Id == 20))
            .Where(u => !StoreAppUpdate.IsMatch(u.Title))
            .ToList();

        // One card line per distinct update, described from Windows Update history when we have it.
        var updateItems = updates
            .GroupBy(u => u.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var u = g.First();
                var kb = KnowledgeBase.KbNumber(u.Title);
                options.UpdateDetails.TryGetValue(kb ?? "", out var info);
                // The KB article is the most specific page. History's SupportUrl is often just a host or a generic fwlink.
                var url = KnowledgeBase.Url(kb) ?? SpecificUrl(info?.SupportUrl);
                return new UpdateItem(u.Title, kb, Shorten(info?.Description), info?.Category, url, g.Any(x => x.Failed));
            })
            .ToList();

        RebootCategory category;
        string title;
        string summary;
        string? dumpPath = null;

        var unexpected = kp41 is not null || e6008 is not null;
        var isResume = bootType == "2";

        if (unexpected)
        {
            var code = BugcheckCatalog.ParseDecimal(kp41?.Get("BugcheckCode", 0)) ?? 0;
            if (code == 0 && bugcheck is not null)
            {
                var m = Regex.Match(bugcheck.Get("param1", 0), @"0x[0-9a-fA-F]+");
                code = m.Success ? BugcheckCatalog.ParseHex(m.Value) ?? 0 : 0;
            }

            if (code != 0 || bugcheck is not null)
            {
                category = RebootCategory.BlueScreen;
                var name = BugcheckCatalog.Name(code);
                var hint = BugcheckCatalog.Hint(code);
                title = $"Crashed with a blue screen: {name}";
                dumpPath = bugcheck?.Get("param2", 1);
                var dumpExists = !string.IsNullOrWhiteSpace(dumpPath) && File.Exists(options.MapPath(dumpPath));
                links.Add(new LinkItem($"STOP 0x{code:X} on Microsoft Learn", BugcheckCatalog.Url(code)));
                summary = $"Windows hit a fatal error (STOP 0x{code:X8}) and restarted itself. " +
                          (hint is not null ? hint + " " : "") +
                          (string.IsNullOrWhiteSpace(dumpPath)
                              ? "No crash dump file was recorded."
                              : dumpExists
                                  ? $"A crash dump was saved to {dumpPath}."
                                  : $"A crash dump was written to {dumpPath} but the file is no longer there.");
                if (e1074 is not null)
                    summary += $" It happened while a {ActionVerbNoun(e1074.Get("param5", 4))} requested by {FriendlyProcess(e1074.Get("param1", 0))} was in progress, so a driver most likely failed during shutdown.";
                else if (lastSleep is not null && (lastWake is null || lastWake.Time < lastSleep.Time))
                    summary += " The PC was asleep or going to sleep at the time, so a driver most likely failed during the sleep or wake transition.";
                Detail("STOP code", $"0x{code:X8} ({name})");
                if (kp41 is not null)
                    Detail("Parameters", string.Join(", ", Enumerable.Range(1, 4).Select(n => kp41.Get($"BugcheckParameter{n}", n))));
                else if (bugcheck is not null)
                    Detail("Parameters", bugcheck.Get("param1", 0));
                Detail("Crash dump", dumpPath);
                Detail("Dump file present", string.IsNullOrWhiteSpace(dumpPath) ? null : dumpExists ? "Yes" : "No");
                var report = bugcheck?.Get("param3", 2);
                Detail("Report ID", report);
                var wer = string.IsNullOrEmpty(report) ? null
                    : all.FirstOrDefault(e => e.Is(EventLogSource.Wer, 1001) && string.Equals(e.Get("ReportId"), report, StringComparison.OrdinalIgnoreCase));
                Cite(wer);
            }
            else
            {
                category = RebootCategory.Unexpected;
                var powerButton = kp41 is not null &&
                                  (kp41.Get("PowerButtonTimestamp") is { Length: > 0 } ts && ts != "0" ||
                                   string.Equals(kp41.Get("LongPowerButtonPressDetected"), "true", StringComparison.OrdinalIgnoreCase));
                var sleeping = kp41 is not null && kp41.Get("SleepInProgress") is { Length: > 0 } s && s != "0";
                var standby = kp41 is not null && string.Equals(kp41.Get("ConnectedStandbyInProgress"), "true", StringComparison.OrdinalIgnoreCase);
                var whea = kp41 is not null && int.TryParse(kp41.Get("WHEABootErrorCount"), out var wheaCount) && wheaCount > 0;
                var sleptBefore = lastSleep is not null && (lastWake is null || lastWake.Time < lastSleep.Time);

                if (powerButton)
                {
                    title = "Forced off with the power button";
                    summary = "The power button was held down until the PC switched off, so Windows never got to shut down cleanly. No crash was recorded.";
                }
                else if (sleeping || standby || sleptBefore)
                {
                    title = "Lost power while asleep or going to sleep";
                    summary = "The PC was asleep (or on its way to sleep) and then lost power or was reset before it woke. This is commonly a battery running flat, a power cut, or a sleep/wake driver problem. No crash dump was written.";
                }
                else if (whea)
                {
                    title = "Hardware error, then a hard reset";
                    summary = "The machine stopped without shutting down and the firmware reported a hardware (WHEA) error during the next boot. Check CPU/RAM stability, temperatures and the power supply.";
                }
                else
                {
                    title = "Lost power, froze, or was reset";
                    summary = "The PC stopped without shutting down cleanly and no crash dump was written. This usually means the power was cut, the reset button was pressed, or the system hung so hard that Windows could not record anything.";
                }

                if (e1074 is not null)
                    summary += $" Note: {FriendlyProcess(e1074.Get("param1", 0))} had requested a {ActionVerbNoun(e1074.Get("param5", 4))} {Format.Duration(boot.Time - e1074.Time)} earlier, so the machine may have hung during that shutdown.";

                if (kp41 is not null)
                {
                    Detail("Power button held", powerButton ? "Yes" : "No");
                    Detail("Sleep in progress", sleeping ? "Yes" : "No");
                    Detail("Boot app status", kp41.Get("BootAppStatus") is { Length: > 0 } and not "0" ? kp41.Get("BootAppStatus") : null);
                    Detail("Firmware hardware errors at boot", whea ? kp41.Get("WHEABootErrorCount") : null);
                }
            }

            if (e1076 is not null)
            {
                Detail("Reason entered afterwards", $"{e1076.Get("param3", 2)} — {e1076.Get("param6", 5)}".Trim(' ', '—'));
            }
            Cite(kp41); Cite(e6008); Cite(bugcheck); Cite(e1076);
        }
        else if (isResume)
        {
            category = RebootCategory.Sleep;
            title = "Resumed from hibernation";
            summary = "The PC came back from hibernation. Nothing restarted.";
        }
        else if (e1074 is not null)
        {
            var process = e1074.Get("param1", 0);
            var reason = e1074.Get("param3", 2);
            var reasonCode = e1074.Get("param4", 3);
            var action = e1074.Get("param5", 4);
            var comment = e1074.Get("param6", 5);
            var user = e1074.Get("param7", 6);
            var exe = ExeName(process);
            var friendly = FriendlyProcess(process);
            var human = IsHumanUser(user);
            var verb = ActionVerbPast(action);

            var isUpdate = UpdateProcesses.Contains(exe) ||
                           (exe == "svchost.exe" && reason.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)) ||
                           reason.Contains("Service pack", StringComparison.OrdinalIgnoreCase) ||
                           reason.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                           reason.Contains("Security fix", StringComparison.OrdinalIgnoreCase);
            var isSignInScreen = exe is "winlogon.exe" or "logonui.exe" && !human;
            var isUser = !isUpdate && !isSignInScreen &&
                         (UserProcesses.Contains(exe) || (exe == "shutdown.exe" && human) ||
                          (human && reason.StartsWith("Other", StringComparison.OrdinalIgnoreCase)));

            if (isSignInScreen)
            {
                category = RebootCategory.Application;
                title = verb == "restarted" ? "Windows restarted itself from the sign-in screen" : "Windows shut down from the sign-in screen";
                summary = $"The PC {verb} from the sign-in screen with nobody signed in. " +
                          "Either someone used the power menu on the lock screen, or Windows finished a pending update install and rebooted a second time to complete it.";
                var named = updates.Where(u => !u.Failed).Select(u => u.Title).Distinct().Take(3).ToList();
                if (named.Count > 0) summary += " Updates installed shortly before: " + string.Join("; ", named) + ".";
            }
            else if (isUpdate)
            {
                category = RebootCategory.WindowsUpdate;
                title = verb == "restarted" ? "Restarted to finish installing Windows updates" : $"Windows Update {verb} the PC";
                summary = $"{friendly} {verb} the PC to finish installing updates.";
                var named = updates.Where(u => !u.Failed).Select(u => u.Title).Distinct().Take(4).ToList();
                if (named.Count > 0) summary += " Installed shortly before: " + string.Join("; ", named) + ".";
                else if (exe is "setuphost.exe" or "setupprep.exe") summary += " This was part of a Windows feature update (version upgrade), which usually reboots several times.";
                else summary += " The updates were staged earlier and applied during the restart.";
            }
            else if (isUser)
            {
                category = RebootCategory.UserInitiated;
                var who = human ? ShortUser(user) : "someone";
                title = verb == "restarted" ? $"{who} restarted the PC" : verb == "powered off" ? $"{who} powered off the PC" : $"{who} shut down the PC";
                summary = $"{who} {verb} the PC from {friendly}.";
                if (!string.IsNullOrWhiteSpace(reason) && !reason.StartsWith("No title", StringComparison.OrdinalIgnoreCase))
                    summary += $" Reason recorded: {reason}.";
                if (!string.IsNullOrWhiteSpace(comment)) summary += $" Comment: \"{comment}\".";
            }
            else
            {
                category = RebootCategory.Application;
                title = $"{friendly} {verb} the PC";
                summary = $"{friendly} asked Windows to {ActionVerbNoun(action)}" +
                          (human ? $" on behalf of {ShortUser(user)}" : "") + ".";
                if (!string.IsNullOrWhiteSpace(reason) && !reason.StartsWith("No title", StringComparison.OrdinalIgnoreCase))
                    summary += $" Reason recorded: {reason}.";
                if (!string.IsNullOrWhiteSpace(comment)) summary += $" Comment: \"{comment}\".";
                var named = updates.Where(u => !u.Failed).Select(u => u.Title).Distinct().Take(3).ToList();
                if (named.Count > 0) summary += " Updates installed shortly before: " + string.Join("; ", named) + ".";
            }

            Detail("Requested by", process);
            Detail("User", user);
            Detail("Action", action);
            Detail("Reason", reason);
            Detail("Reason code", reasonCode);
            Detail("Comment", comment);
            Cite(e1074);
        }
        else if (kg13 is not null || e6006 is not null || kp109 is not null)
        {
            category = RebootCategory.CleanNoReason;
            title = bootType == "1" ? "Shut down cleanly (fast startup)" : "Shut down cleanly, reason not recorded";
            summary = "Windows shut down normally but nothing recorded who asked for it. " +
                      (bootType == "1"
                          ? "Because fast startup is on, this was a hybrid shutdown: the kernel hibernated and resumed rather than fully restarting."
                          : "This is typical of a shutdown from the sign-in screen, a scheduled task, a remote command, or Windows shutting down without a logged reason.");
            var named = updates.Where(u => !u.Failed).Select(u => u.Title).Distinct().Take(3).ToList();
            if (named.Count > 0) summary += " Updates installed shortly before: " + string.Join("; ", named) + ".";
        }
        else
        {
            category = RebootCategory.Unknown;
            title = prevBoot is null ? "Booted (no earlier records)" : "Booted, but the previous shutdown left no trace";
            summary = prevBoot is null
                ? "This is the oldest boot in the selected range, so there is nothing before it to explain."
                : "No shutdown, crash or power event was logged before this boot. The log may have been cleared or the machine may have stopped so abruptly that nothing was written.";
        }

        Detail("Shut down at", shutdownTime is null ? (unexpected ? "Not recorded (unexpected stop)" : null) : Format.When(shutdownTime));
        Detail("Booted at", Format.When(boot.Time));
        Detail("Down for", downtime is null ? null : Format.Duration(downtime));
        Detail("Previous session uptime", previousUptime is null ? null : Format.Duration(previousUptime));
        Detail("Boot type", bootTypeText);

        Cite(e1074 is null ? null : (category is RebootCategory.BlueScreen or RebootCategory.Unexpected ? e1074 : null));
        Cite(kg13); Cite(e6006); Cite(kp109); Cite(boot); Cite(kb27);
        foreach (var u in updates.Take(6)) Cite(u.Event);
        evidence.Sort((a, b) => a.Time.CompareTo(b.Time));

        return new RebootEntry
        {
            Timestamp = shutdownTime ?? boot.Time,
            Category = category,
            Title = title,
            Summary = summary,
            ShutdownTime = shutdownTime,
            BootTime = boot.Time,
            Downtime = downtime,
            PreviousUptime = previousUptime,
            DumpPath = dumpPath,
            Details = details.DistinctBy(d => d.Key + "\u0001" + d.Value).ToList(),
            Evidence = evidence.Distinct().ToList(),
            Links = links,
            Updates = updateItems,
        };
    }

    // ---------------------------------------------------------------- live kernel events

    private static IEnumerable<RebootEntry> LiveKernelEvents(List<RawEvent> events, AnalysisOptions options)
    {
        // WER re-files the same report every time it retries the upload, so group by the
        // underlying fault (dump file, else report id, else code + parameters) and keep the first.
        var reports = events
            .Where(e => e.Is(EventLogSource.Wer, 1001) && e.Get("EventName") == "LiveKernelEvent")
            .GroupBy(e =>
            {
                var dump = FirstDump(e.Get("AttachedFiles"));
                if (dump is not null) return "dump:" + dump.ToLowerInvariant();
                var report = e.Get("ReportId");
                if (report.Length > 0) return "report:" + report.ToLowerInvariant();
                return "code:" + string.Join("|", e.Get("P1"), e.Get("P2"), e.Get("P3"), e.Get("P4"), e.Get("P5"));
            });

        foreach (var group in reports)
        {
            var e = group.First();
            var retries = group.Count() - 1;
            var codeText = e.Get("P1");
            var code = BugcheckCatalog.ParseHex(codeText) ?? 0;
            var known = BugcheckCatalog.Hint(code) is not null;
            var name = known ? BugcheckCatalog.Name(code) : $"code 0x{code:X}";
            var hint = BugcheckCatalog.Hint(code) ?? "Windows captured diagnostic data about a kernel or driver problem.";
            var dump = FirstDump(e.Get("AttachedFiles"));

            // WER often files the report long after the fault; the dump name carries the real time.
            var when = e.Time;
            if (dump is not null)
            {
                var m = DumpDate.Match(dump);
                if (m.Success && DateTime.TryParseExact($"{m.Groups[1]}{m.Groups[2]}{m.Groups[3]}{m.Groups[4]}{m.Groups[5]}",
                        "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                    when = parsed;
            }

            var details = new List<KeyValuePair<string, string>>
            {
                new("Live kernel event code", $"0x{code:X} ({name})"),
                new("Parameters", string.Join(", ", new[] { e.Get("P2"), e.Get("P3"), e.Get("P4"), e.Get("P5") }.Where(s => s.Length > 0))),
            };
            if (dump is not null) details.Add(new("Dump file", dump + (File.Exists(options.MapPath(dump)) ? "" : " (no longer present)")));
            if (when != e.Time) details.Add(new("First reported to Windows Error Reporting", Format.When(e.Time)));
            if (retries > 0) details.Add(new("Report re-filed by Windows Error Reporting", $"{retries} more time(s), last on {Format.WhenShort(group.Last().Time)}"));

            yield return new RebootEntry
            {
                Timestamp = when,
                Category = RebootCategory.LiveKernelEvent,
                Title = $"Kernel problem recorded without a reboot ({name})",
                Summary = $"Windows kept running, but captured a live kernel report (code 0x{code:X}). {hint} These often precede a real crash, so repeated ones are worth investigating.",
                DumpPath = dump,
                Details = details,
                Evidence = new List<EvidenceEvent> { e.ToEvidence() },
                Links = new List<LinkItem> { new($"Code 0x{code:X} on Microsoft Learn", BugcheckCatalog.Url(code)) },
            };
        }
    }

    private static string? FirstDump(string attachedFiles) =>
        attachedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().Replace(@"\\?\", ""))
            .FirstOrDefault(s => s.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- sleep / wake

    private static IEnumerable<RebootEntry> SleepCycles(List<RawEvent> events, List<RawEvent> boots)
    {
        var sleeps = events.Where(e => e.Is(EventLogSource.KernelPower, 42)).ToList();
        var wakes = events.Where(e => e.Is(EventLogSource.PowerTroubleshooter, 1)).ToList();

        foreach (var sleep in sleeps)
        {
            var nextBoot = boots.FirstOrDefault(b => b.Time > sleep.Time)?.Time ?? DateTime.MaxValue;
            var nextSleep = sleeps.FirstOrDefault(s => s.Time > sleep.Time)?.Time ?? DateTime.MaxValue;
            var wake = wakes.FirstOrDefault(w => w.Time > sleep.Time && w.Time < nextBoot && w.Time < nextSleep);

            var reason = SleepReason(sleep.Get("Reason", 1));
            var details = new List<KeyValuePair<string, string>>
            {
                new("Went to sleep", Format.When(sleep.Time)),
                new("Sleep reason", reason),
            };
            var evidence = new List<EvidenceEvent> { sleep.ToEvidence() };

            if (wake is null)
            {
                yield return new RebootEntry
                {
                    Timestamp = sleep.Time,
                    Category = RebootCategory.Sleep,
                    Title = "Went to sleep and did not wake before the next boot",
                    Summary = $"The PC went to sleep ({reason}) and the next thing in the log is a boot, not a wake. It either hibernated, was shut down while asleep, or lost power.",
                    Details = details,
                    Evidence = evidence,
                };
                continue;
            }

            var wakeTime = ParseUtc(wake.Get("WakeTime")) ?? wake.Time;
            var sleepTime = ParseUtc(wake.Get("SleepTime")) ?? sleep.Time;
            var source = wake.Get("WakeSourceText");
            if (string.IsNullOrWhiteSpace(source)) source = WakeSourceType(wake.Get("WakeSourceType"));
            details.Add(new("Woke up", Format.When(wakeTime)));
            details.Add(new("Asleep for", Format.Duration(wakeTime - sleepTime)));
            details.Add(new("Wake source", source));
            evidence.Add(wake.ToEvidence());

            yield return new RebootEntry
            {
                Timestamp = sleep.Time,
                Category = RebootCategory.Sleep,
                Title = $"Slept for {Format.Duration(wakeTime - sleepTime)}",
                Summary = $"Went to sleep ({reason}) and woke at {Format.WhenShort(wakeTime)}. Wake source: {source}. Nothing restarted.",
                Details = details,
                Evidence = evidence,
            };
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Keeps the first sentence or two of a history description; some run to a paragraph.</summary>
    public static string? Shorten(string? text, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Length <= max) return t;
        var cut = t.LastIndexOf(". ", max, StringComparison.Ordinal);
        if (cut >= 60) return t[..(cut + 1)];
        var space = t.LastIndexOf(' ', max);
        return (space > 60 ? t[..space] : t[..max]).TrimEnd('.', ',', ';', ':') + "…";
    }

    /// <summary>Accepts a support URL only when it points at a page, not just a host such as http://support.microsoft.com.</summary>
    public static string? SpecificUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        return uri.AbsolutePath.Length > 1 || uri.Query.Length > 1 ? uri.ToString() : null;
    }

    private static DateTime? ParseUnexpectedShutdownTime(RawEvent e6008)
    {
        // "The previous system shutdown at %1 on %2 was unexpected." Both parts are localized strings.
        var time = e6008.Get("param1", 0);
        var date = e6008.Get("param2", 1);
        if (DateTime.TryParse($"{date} {time}", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dt)) return dt;
        if (DateTime.TryParse($"{date} {time}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
        return null;
    }

    private static DateTime? ParseUtc(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt.ToLocalTime()
            : null;

    private static string ExeName(string process)
    {
        var p = process.Trim();
        var paren = p.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && p.EndsWith(')')) p = p[..paren];
        var slash = p.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) p = p[(slash + 1)..];
        return p.ToLowerInvariant();
    }

    public static string FriendlyProcess(string process)
    {
        var exe = ExeName(process);
        return exe switch
        {
            "cloudexperiencehostbroker.exe" or "oobe.exe" or "windeploy.exe" => "Windows first-run setup (OOBE)",
            "sppextcomobj.exe" or "slui.exe" => "Windows activation",
            "systemsettingsadminflows.exe" or "systemresetplatform.exe" => "Reset this PC / recovery",
            "dism.exe" or "dismhost.exe" => "DISM servicing",
            "explorer.exe" => "the Start menu power button",
            "shutdown.exe" => "the shutdown command",
            "winlogon.exe" => "the sign-in / Ctrl+Alt+Del screen",
            "logonui.exe" => "the sign-in screen",
            "systemsettings.exe" => "the Settings app",
            "trustedinstaller.exe" or "tiworker.exe" => "Windows Update (Windows Modules Installer)",
            "mousocoreworker.exe" or "usoclient.exe" or "usocoreworker.exe" => "Windows Update (Update Orchestrator)",
            "musnotification.exe" or "musnotifyicon.exe" => "Windows Update (restart notification)",
            "wuauclt.exe" => "Windows Update",
            "setuphost.exe" or "setupprep.exe" => "Windows Setup (feature update)",
            "waasmedicagent.exe" => "Windows Update Medic",
            "wininit.exe" => "Windows (wininit)",
            "msiexec.exe" => "Windows Installer",
            "svchost.exe" => "a Windows service",
            "wsmprovhost.exe" => "a remote PowerShell session",
            "powershell.exe" or "pwsh.exe" => "PowerShell",
            "cmd.exe" => "a command prompt",
            "" => "an unknown process",
            _ => OriginalFileName(process),
        };
    }

    private static string OriginalFileName(string process)
    {
        var p = process.Trim();
        var paren = p.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && p.EndsWith(')')) p = p[..paren];
        var slash = p.LastIndexOfAny(new[] { '\\', '/' });
        return slash >= 0 ? p[(slash + 1)..] : p;
    }

    private static bool IsHumanUser(string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        var u = user.ToUpperInvariant();
        return !(u.Contains("NT AUTHORITY") || u.EndsWith("$") || u.Contains("SYSTEM") || u.Contains("LOCAL SERVICE") || u.Contains("NETWORK SERVICE"));
    }

    private static string ShortUser(string user)
    {
        var i = user.LastIndexOf('\\');
        return i >= 0 ? user[(i + 1)..] : user;
    }

    private static string ActionVerbPast(string action)
    {
        var a = action.ToLowerInvariant();
        if (a.Contains("restart") || a.Contains("reboot")) return "restarted";
        if (a.Contains("power")) return "powered off";
        return "shut down";
    }

    private static string ActionVerbNoun(string action)
    {
        var a = action.ToLowerInvariant();
        if (a.Contains("restart") || a.Contains("reboot")) return "restart";
        if (a.Contains("power")) return "power off";
        return "shut down";
    }

    private static string SleepReason(string code) => code switch
    {
        "0" => "power button, sleep key or lid",
        "1" => "the power button",
        "2" => "an app requested it",
        "4" => "the idle timeout",
        "5" => "the idle timeout",
        "6" => "the lid was closed",
        "7" => "hibernate from sleep (timer)",
        "9" => "battery critically low",
        "" => "reason not recorded",
        _ => "reason code " + code,
    };

    private static string WakeSourceType(string code) => code switch
    {
        "0" => "Unknown",
        "1" => "Power button",
        "2" => "Wake-on-LAN or a device",
        "3" => "Wake timer",
        "4" => "Device",
        "5" => "Timer (scheduled task)",
        _ => "Unknown",
    };
}
