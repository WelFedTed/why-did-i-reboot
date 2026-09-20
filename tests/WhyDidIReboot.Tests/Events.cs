using WhyDidIReboot.Core;

namespace WhyDidIReboot.Tests;

/// <summary>Builds the raw event records the analyzer consumes, shaped like the real Windows records.</summary>
internal static class Events
{
    public static readonly DateTime T0 = new(2026, 9, 13, 15, 0, 0, DateTimeKind.Local);

    private static long _record;

    public static RawEvent Ev(DateTime time, string provider, int id, Dictionary<string, string>? data = null, string log = "System", string message = "")
    {
        data ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new RawEvent
        {
            Time = time,
            Provider = provider,
            Id = id,
            Log = log,
            RecordId = Interlocked.Increment(ref _record),
            Message = message.Length > 0 ? message : $"{provider} {id}",
            Data = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase),
            Ordered = data.Values.ToList(),
        };
    }

    public static RawEvent Boot(DateTime t) => Ev(t, EventLogSource.KernelGeneral, 12, message: "The operating system started.");
    public static RawEvent ShuttingDown(DateTime t) => Ev(t, EventLogSource.KernelGeneral, 13, message: "The operating system is shutting down.");
    public static RawEvent LogStopped(DateTime t) => Ev(t, EventLogSource.EventLogSvc, 6006);
    public static RawEvent LogStarted(DateTime t) => Ev(t, EventLogSource.EventLogSvc, 6005);
    public static RawEvent BootType(DateTime t, string type) => Ev(t, EventLogSource.KernelBoot, 27, new() { ["BootType"] = type, ["LoadOptions"] = "" });

    public static RawEvent Requested(DateTime t, string process, string user, string reason, string action = "restart", string code = "0x0", string comment = "") =>
        Ev(t, EventLogSource.User32, 1074, new()
        {
            ["param1"] = process,
            ["param2"] = "LUNCHBOX",
            ["param3"] = reason,
            ["param4"] = code,
            ["param5"] = action,
            ["param6"] = comment,
            ["param7"] = user,
        }, message: $"The process {process} has initiated the {action} of computer LUNCHBOX on behalf of user {user} for the following reason: {reason}");

    public static RawEvent KernelPower41(DateTime t, int bugcheck, bool powerButton = false, bool sleeping = false) =>
        Ev(t, EventLogSource.KernelPower, 41, new()
        {
            ["BugcheckCode"] = bugcheck.ToString(),
            ["BugcheckParameter1"] = "0x4",
            ["BugcheckParameter2"] = "0x12c",
            ["BugcheckParameter3"] = "0xffffc30df3df2040",
            ["BugcheckParameter4"] = "0xffffd40cf206f5d0",
            ["SleepInProgress"] = sleeping ? "6" : "0",
            ["PowerButtonTimestamp"] = powerButton ? "133000000000000000" : "0",
            ["LongPowerButtonPressDetected"] = powerButton ? "true" : "false",
            ["ConnectedStandbyInProgress"] = "false",
            ["WHEABootErrorCount"] = "0",
            ["BootAppStatus"] = "0",
        }, message: "The system has rebooted without cleanly shutting down first.");

    public static RawEvent Bugcheck1001(DateTime t, string code, string dump, string reportId) =>
        Ev(t, EventLogSource.BugCheck, 1001, new()
        {
            ["param1"] = $"{code} (0x0000000000000004, 0x000000000000012c, 0xffffc30df3df2040, 0xffffd40cf206f5d0)",
            ["param2"] = dump,
            ["param3"] = reportId,
        }, message: $"The computer has rebooted from a bugcheck. The bugcheck was: {code}. A dump was saved in: {dump}. Report Id: {reportId}.");

    public static RawEvent UpdateInstalled(DateTime t, string title) =>
        Ev(t, EventLogSource.WindowsUpdateClient, 19, new() { ["updateTitle"] = title, ["updateGuid"] = Guid.NewGuid().ToString() },
            message: $"Installation Successful: Windows successfully installed the following update: {title}");

    public static RawEvent Sleep(DateTime t, string reason = "4") =>
        Ev(t, EventLogSource.KernelPower, 42, new() { ["TargetState"] = "4", ["EffectiveState"] = "4", ["Reason"] = reason }, message: "The system is entering sleep.");

    public static RawEvent Wake(DateTime t, DateTime sleptAt, DateTime wokeAt, string source) =>
        Ev(t, EventLogSource.PowerTroubleshooter, 1, new()
        {
            ["SleepTime"] = sleptAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
            ["WakeTime"] = wokeAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
            ["WakeSourceType"] = "1",
            ["WakeSourceText"] = source,
        }, message: "The system has returned from a low power state.");

    public static RawEvent LiveKernelEvent(DateTime t, string code, string dump, string reportId) =>
        Ev(t, EventLogSource.Wer, 1001, new()
        {
            ["EventName"] = "LiveKernelEvent",
            ["P1"] = code,
            ["P2"] = "fffff8048358b800",
            ["P3"] = "ffffc30df33d9040",
            ["P4"] = "45",
            ["P5"] = "2d",
            ["AttachedFiles"] = $"\n\\\\?\\{dump}\n\\\\?\\C:\\Windows\\SystemTemp\\WER-1935484-0.sysdata.xml",
            ["ReportId"] = reportId,
        }, log: "Application", message: "Fault bucket, type 0 Event Name: LiveKernelEvent");

    public static RawEvent WerBlueScreen(DateTime t, string reportId) =>
        Ev(t, EventLogSource.Wer, 1001, new() { ["EventName"] = "BlueScreen", ["P1"] = "9f", ["ReportId"] = reportId },
            log: "Application", message: "Fault bucket, type 0 Event Name: BlueScreen");

    /// <summary>A complete clean restart: request, log stop, kernel shutdown, boot, boot type.</summary>
    public static IEnumerable<RawEvent> CleanRestart(DateTime shutdown, RawEvent? request, TimeSpan? downtime = null)
    {
        if (request is not null) yield return request;
        yield return LogStopped(shutdown.AddSeconds(-2));
        yield return ShuttingDown(shutdown);
        var boot = shutdown + (downtime ?? TimeSpan.FromSeconds(30));
        yield return Boot(boot);
        yield return BootType(boot.AddMilliseconds(300), "0");
    }
}
