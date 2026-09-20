using System.IO;

namespace WhyDidIReboot.Core;

/// <summary>What kind of thing ended (or interrupted) a session.</summary>
public enum RebootCategory
{
    WindowsUpdate,
    UserInitiated,
    Application,
    BlueScreen,
    Unexpected,
    CleanNoReason,
    Unknown,
    LiveKernelEvent,
    Sleep,
}

/// <summary>A single Windows event log record, reduced to what the analyzer needs.</summary>
public sealed class RawEvent
{
    public required DateTime Time { get; init; }
    public required int Id { get; init; }
    public required string Provider { get; init; }
    public required string Log { get; init; }
    public long RecordId { get; init; }
    public string Message { get; init; } = "";

    /// <summary>EventData values keyed by their Name attribute (empty for unnamed data).</summary>
    public Dictionary<string, string> Data { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EventData values in document order, for events whose data is unnamed.</summary>
    public List<string> Ordered { get; init; } = new();

    public string Get(string name, int? fallbackIndex = null)
    {
        if (Data.TryGetValue(name, out var v)) return v;
        if (fallbackIndex is int i && i >= 0 && i < Ordered.Count) return Ordered[i];
        return "";
    }

    public bool Is(string provider, int id) =>
        Id == id && string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);

    public EvidenceEvent ToEvidence() => new(Time, Log, Id, Provider, Message);
}

/// <summary>A log record shown to the user as evidence for an entry.</summary>
public sealed record EvidenceEvent(DateTime Time, string Log, int Id, string Provider, string Message);

/// <summary>A clickable reference shown on a card, e.g. the Microsoft Learn page for a STOP code.</summary>
public sealed record LinkItem(string Label, string Url);

/// <summary>An update installed shortly before a reboot, enriched from Windows Update history when available.</summary>
public sealed record UpdateItem(string Title, string? Kb, string? Description, string? Category, string? Url, bool Failed, string Group = UpdateClassifier.Other)
{
    /// <summary>"KB5094126 on Microsoft Support", "Microsoft Update Catalog" for catalog searches, else "Microsoft Support".</summary>
    public string LinkLabel =>
        Kb is not null ? $"{Kb} on Microsoft Support"
        : Url is not null && Url.Contains("catalog.update.microsoft.com", StringComparison.OrdinalIgnoreCase) ? "Microsoft Update Catalog"
        : "Microsoft Support";
}

/// <summary>An inclusive window of event times. Null bounds are open-ended.</summary>
public sealed record TimeRange(DateTime? From, DateTime? To)
{
    public static readonly TimeRange All = new(null, null);

    /// <summary>"Last N days" as a window ending now; null means everything.</summary>
    public static TimeRange LastDays(int? days) => days is int d ? new(DateTime.Now.AddDays(-d), null) : All;

    /// <summary>Whole calendar days: From at 00:00 on the first day, To at the end of the last.</summary>
    public static TimeRange Days(DateTime firstDay, DateTime lastDay)
    {
        var a = firstDay.Date;
        var b = lastDay.Date;
        if (b < a) (a, b) = (b, a);
        return new(a, b.AddDays(1).AddTicks(-1));
    }

    public bool Contains(DateTime t) => (From is null || t >= From) && (To is null || t <= To);

    public string Label =>
        From is null && To is null ? "everything"
        : To is null ? $"since {From:d MMM yyyy}"
        : From is null ? $"until {To:d MMM yyyy}"
        : $"{From:d MMM yyyy} – {To:d MMM yyyy}";
}

/// <summary>Updates on a card bucketed under one heading, e.g. "Drivers".</summary>
public sealed record UpdateGroup(string Name, IReadOnlyList<UpdateItem> Items);

/// <summary>What Windows Update history knows about an update, keyed by KB number.</summary>
public sealed record UpdateDetails(string Title, string? Description, string? Category, string? SupportUrl, DateTime? InstalledOn);

/// <summary>Where the event logs come from: this PC, or .evtx files from another Windows installation.</summary>
public sealed record LogLocation(string SystemPath, string? ApplicationPath, string? Root, string Display, bool IsOffline)
{
    public static readonly LogLocation Local = new("System", "Application", null, "this PC", false);

    /// <summary>True when <see cref="SystemPath"/> is a CSV report this app exported, not an event log.</summary>
    public bool IsCsv { get; init; }

    public static LogLocation ForCsv(string path) =>
        new(path, null, null, Path.GetFileName(path), true) { IsCsv = true };

    /// <summary>
    /// Accepts a drive root (D:\), a Windows folder (D:\Windows), the winevt\Logs folder, a System.evtx file,
    /// or a .csv report exported by this app, and finds what to read. <paramref name="fileExists"/> is injectable for tests.
    /// </summary>
    public static LogLocation? Resolve(string path, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim().TrimEnd('\\', '/');
        if (p.Length == 2 && p[1] == ':') p += '\\';   // "D:" → "D:\"
        if (p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return fileExists(p) ? ForCsv(p) : null;

        var candidates = new List<string>();
        if (p.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)) candidates.Add(p);
        else
        {
            candidates.Add(Path.Combine(p, "System.evtx"));
            candidates.Add(Path.Combine(p, "System32", "winevt", "Logs", "System.evtx"));
            candidates.Add(Path.Combine(p, "Windows", "System32", "winevt", "Logs", "System.evtx"));
        }

        var system = candidates.FirstOrDefault(fileExists);
        if (system is null) return null;

        var logsDir = Path.GetDirectoryName(system) ?? "";
        var app = Path.Combine(logsDir, "Application.evtx");
        var appPath = fileExists(app) ? app : null;

        // D:\Windows\System32\winevt\Logs → D:\  (used to remap C:\Windows\Minidump\... paths)
        string? root = null;
        var marker = logsDir.IndexOf(@"\Windows\System32\winevt\Logs", StringComparison.OrdinalIgnoreCase);
        if (marker > 0) root = logsDir[..marker] + "\\";

        return new LogLocation(system, appPath, root, root ?? logsDir, true);
    }

    /// <summary>Maps a path recorded on the original machine (C:\Windows\...) onto the offline drive.</summary>
    public string MapPath(string path)
    {
        if (!IsOffline || Root is null || path.Length < 3 || path[1] != ':') return path;
        return Path.Combine(Root, path[3..]);
    }
}

/// <summary>Extra inputs to the analysis that are not event records.</summary>
public sealed class AnalysisOptions
{
    public static readonly AnalysisOptions Default = new();

    /// <summary>Rewrites paths found in the log (dump files) before checking whether they exist.</summary>
    public Func<string, string> MapPath { get; init; } = p => p;

    /// <summary>Windows Update history keyed by KB number ("KB5094126"), used to describe updates on cards.</summary>
    public IReadOnlyDictionary<string, UpdateDetails> UpdateDetails { get; init; } =
        new Dictionary<string, UpdateDetails>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One line in the human readable timeline.</summary>
public sealed class RebootEntry
{
    public required DateTime Timestamp { get; init; }
    public required RebootCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }

    public DateTime? ShutdownTime { get; init; }
    public DateTime? BootTime { get; init; }
    public TimeSpan? Downtime { get; init; }
    public TimeSpan? PreviousUptime { get; init; }
    public string? DumpPath { get; init; }

    /// <summary>True when the dump file was found on disk (after any offline path mapping) at analysis time.</summary>
    public bool DumpExists { get; init; }

    public List<KeyValuePair<string, string>> Details { get; init; } = new();
    public List<EvidenceEvent> Evidence { get; init; } = new();
    public List<LinkItem> Links { get; init; } = new();
    public List<UpdateItem> Updates { get; init; } = new();

    public bool HasLinks => Links.Count > 0;
    public bool HasUpdates => Updates.Count > 0;

    /// <summary>Updates bucketed by <see cref="UpdateClassifier"/> group, in display order, empty groups omitted.</summary>
    public IReadOnlyList<UpdateGroup> UpdateGroups =>
        UpdateClassifier.Order
            .Select(g => new UpdateGroup(g, Updates.Where(u => u.Group == g).ToList()))
            .Where(g => g.Items.Count > 0)
            .ToList();

    /// <summary>True when this entry represents the machine actually going down and coming back.</summary>
    public bool IsReboot => Category is not (RebootCategory.LiveKernelEvent or RebootCategory.Sleep);

    public string DateGroup => Timestamp.ToString("dddd, d MMMM yyyy");
}

public sealed class AnalysisResult
{
    public List<RebootEntry> Entries { get; init; } = new();
    public int RecordsRead { get; init; }
    public TimeSpan Elapsed { get; init; }
    /// <summary>This PC's boot time, or for offline logs the last boot recorded in them.</summary>
    public DateTime CurrentBootTime { get; init; }
    public LogLocation Source { get; init; } = LogLocation.Local;
    public List<string> Warnings { get; init; } = new();
}
