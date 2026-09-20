using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Xml.Linq;

namespace WhyDidIReboot.Core;

/// <summary>Reads the handful of System / Application log records that explain reboots.</summary>
public static class EventLogSource
{
    public const string KernelGeneral = "Microsoft-Windows-Kernel-General";
    public const string KernelPower = "Microsoft-Windows-Kernel-Power";
    public const string KernelBoot = "Microsoft-Windows-Kernel-Boot";
    public const string PowerTroubleshooter = "Microsoft-Windows-Power-Troubleshooter";
    public const string User32 = "User32";
    public const string EventLogSvc = "EventLog";
    public const string BugCheck = "Microsoft-Windows-WER-SystemErrorReporting";
    public const string BugCheckLegacy = "BugCheck";
    public const string WindowsUpdateClient = "Microsoft-Windows-WindowsUpdateClient";
    public const string Wer = "Windows Error Reporting";

    /// <summary>(provider, id) pairs the analyzer understands. Anything else is dropped.</summary>
    private static readonly HashSet<(string, int)> Wanted = new(new ProviderIdComparer())
    {
        (KernelGeneral, 12), (KernelGeneral, 13),
        (KernelBoot, 27),
        (KernelPower, 41), (KernelPower, 42), (KernelPower, 109),
        (PowerTroubleshooter, 1),
        (User32, 1074), (User32, 1076),
        (EventLogSvc, 6005), (EventLogSvc, 6006), (EventLogSvc, 6008),
        (BugCheck, 1001), (BugCheckLegacy, 1001),
        (WindowsUpdateClient, 19), (WindowsUpdateClient, 20),
        (Wer, 1001),
    };

    private static readonly int[] SystemIds = { 1, 12, 13, 19, 20, 27, 41, 42, 109, 1001, 1074, 1076, 6005, 6006, 6008 };

    /// <summary>Reads all relevant records from this PC's logs, newest last. <paramref name="days"/> null means the whole log.</summary>
    public static List<RawEvent> Read(int? days, List<string> warnings, out int recordsRead) =>
        Read(LogLocation.Local, days, warnings, out recordsRead);

    public static List<RawEvent> Read(LogLocation location, int? days, List<string> warnings, out int recordsRead) =>
        Read(location, TimeRange.LastDays(days), warnings, out recordsRead);

    /// <summary>Reads from the live logs or from .evtx files of another Windows installation, within a time window.</summary>
    public static List<RawEvent> Read(LogLocation location, TimeRange range, List<string> warnings, out int recordsRead)
    {
        var result = new List<RawEvent>();
        recordsRead = 0;
        var pathType = location.IsOffline ? PathType.FilePath : PathType.LogName;
        var timeClause = TimeClause(range);

        var idClause = string.Join(" or ", SystemIds.Select(i => $"EventID={i}"));
        var systemQuery = $"*[System[({idClause}){timeClause}]]";
        recordsRead += ReadLog(location.SystemPath, pathType, "System", systemQuery, result, warnings);

        if (location.ApplicationPath is null)
        {
            warnings.Add("Application.evtx was not found next to System.evtx, so live kernel events are omitted.");
        }
        else
        {
            // WER 1001 in the Application log covers LiveKernelEvents (kernel trouble that did not reboot)
            // and the report record for blue screens. Filter on EventName in the query itself to skip
            // the thousands of ordinary application crash reports.
            var werQuery =
                $"*[System[Provider[@Name='{Wer}'] and EventID=1001{timeClause}]]" +
                " and *[EventData[Data[@Name='EventName']='LiveKernelEvent' or Data[@Name='EventName']='BlueScreen']]";
            try
            {
                recordsRead += ReadLog(location.ApplicationPath, pathType, "Application", werQuery, result, warnings, throwOnQueryError: true);
            }
            catch (EventLogException)
            {
                // Some builds reject EventData predicates; fall back to filtering in code.
                var plain = $"*[System[Provider[@Name='{Wer}'] and EventID=1001{timeClause}]]";
                recordsRead += ReadLog(location.ApplicationPath, pathType, "Application", plain, result, warnings, werFilter: true);
            }
        }

        result.Sort((a, b) =>
        {
            var c = a.Time.CompareTo(b.Time);
            return c != 0 ? c : a.RecordId.CompareTo(b.RecordId);
        });
        return result;
    }

    /// <summary>XPath predicate for the window, in UTC as the event log stores SystemTime.</summary>
    public static string TimeClause(TimeRange range)
    {
        var parts = new List<string>();
        if (range.From is DateTime from) parts.Add($"@SystemTime>='{from.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss.fff'Z'}'");
        if (range.To is DateTime to) parts.Add($"@SystemTime<='{to.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss.fff'Z'}'");
        return parts.Count == 0 ? "" : " and TimeCreated[" + string.Join(" and ", parts) + "]";
    }

    private static int ReadLog(string path, PathType pathType, string log, string xpath, List<RawEvent> sink, List<string> warnings,
        bool throwOnQueryError = false, bool werFilter = false)
    {
        var read = 0;
        try
        {
            var query = new EventLogQuery(path, pathType, xpath) { ReverseDirection = false };
            using var reader = new EventLogReader(query);
            while (true)
            {
                EventRecord? record;
                try { record = reader.ReadEvent(); }
                catch (EventLogException ex) when (!throwOnQueryError)
                {
                    warnings.Add($"{log} log: stopped reading early ({ex.Message.Trim()}).");
                    break;
                }
                if (record is null) break;
                using (record)
                {
                    read++;
                    var raw = ToRaw(record, log);
                    if (raw is null) continue;
                    if (!Wanted.Contains((raw.Provider, raw.Id))) continue;
                    if (werFilter)
                    {
                        var name = raw.Get("EventName");
                        if (name is not ("LiveKernelEvent" or "BlueScreen")) continue;
                    }
                    sink.Add(raw);
                }
            }
        }
        catch (EventLogException) when (throwOnQueryError)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            warnings.Add($"{log} log: access denied ({ex.Message.Trim()}).");
        }
        catch (EventLogException ex)
        {
            warnings.Add($"{log} log: {ex.Message.Trim()}");
        }
        return read;
    }

    private static RawEvent? ToRaw(EventRecord record, string log)
    {
        if (record.TimeCreated is not DateTime time) return null;

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        try
        {
            var root = XElement.Parse(record.ToXml());
            var ns = root.Name.Namespace;
            var eventData = root.Element(ns + "EventData");
            if (eventData is not null)
            {
                foreach (var el in eventData.Elements(ns + "Data"))
                {
                    var value = el.Value.Trim();
                    ordered.Add(value);
                    var name = el.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(name)) data[name] = value;
                }
            }
        }
        catch
        {
            // Malformed XML is rare; fall back to the untyped property list.
            foreach (var p in record.Properties) ordered.Add(p.Value?.ToString() ?? "");
        }

        string? message = null;
        try { message = record.FormatDescription(); } catch { /* provider metadata missing */ }
        if (string.IsNullOrWhiteSpace(message))
        {
            var sb = new StringBuilder();
            foreach (var kv in data) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("; ");
            if (sb.Length == 0) sb.Append(string.Join("; ", ordered));
            message = sb.ToString().TrimEnd(' ', ';');
        }

        return new RawEvent
        {
            Time = time,
            Id = record.Id,
            Provider = record.ProviderName ?? "",
            Log = log,
            RecordId = record.RecordId ?? 0,
            Message = CollapseWhitespace(message),
            Data = data,
            Ordered = ordered,
        };
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastSpace = false;
        foreach (var ch in s)
        {
            // Left-to-right / right-to-left marks wrap dates in localized messages; drop them outright.
            if (ch is '‎' or '‏') continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private sealed class ProviderIdComparer : IEqualityComparer<(string, int)>
    {
        public bool Equals((string, int) x, (string, int) y) =>
            x.Item2 == y.Item2 && string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, int) obj) =>
            HashCode.Combine(obj.Item1.ToLowerInvariant(), obj.Item2);
    }
}
