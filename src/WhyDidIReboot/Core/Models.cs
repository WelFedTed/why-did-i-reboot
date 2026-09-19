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

    public List<KeyValuePair<string, string>> Details { get; init; } = new();
    public List<EvidenceEvent> Evidence { get; init; } = new();

    /// <summary>True when this entry represents the machine actually going down and coming back.</summary>
    public bool IsReboot => Category is not (RebootCategory.LiveKernelEvent or RebootCategory.Sleep);

    public string DateGroup => Timestamp.ToString("dddd, d MMMM yyyy");
}

public sealed class AnalysisResult
{
    public List<RebootEntry> Entries { get; init; } = new();
    public int RecordsRead { get; init; }
    public TimeSpan Elapsed { get; init; }
    public DateTime CurrentBootTime { get; init; }
    public List<string> Warnings { get; init; } = new();
}
