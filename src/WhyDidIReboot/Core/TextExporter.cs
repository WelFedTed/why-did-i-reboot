using System.Text;

namespace WhyDidIReboot.Core;

/// <summary>Plain text and CSV renderings of an analysis, shared by the UI export and the command line.</summary>
public static class TextExporter
{
    public static string ToText(AnalysisResult result, IEnumerable<RebootEntry> entries, string rangeLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Why Did I Reboot — report for {Environment.MachineName}");
        sb.AppendLine($"Generated {Format.When(DateTime.Now)} · Range: {rangeLabel} · Log records read: {result.RecordsRead}");
        sb.AppendLine($"Current session: up since {Format.When(result.CurrentBootTime)} ({Format.Duration(DateTime.Now - result.CurrentBootTime)})");
        foreach (var w in result.Warnings) sb.AppendLine($"Warning: {w}");
        sb.AppendLine();

        foreach (var e in entries)
        {
            sb.AppendLine(EntryToText(e));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string EntryToText(RebootEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}]  {e.Title.ToUpperInvariant()}");
        sb.AppendLine($"    Category: {Format.Category(e.Category)}");
        sb.AppendLine($"    {e.Summary}");
        foreach (var d in e.Details) sb.AppendLine($"    {d.Key}: {d.Value}");
        if (e.Evidence.Count > 0)
        {
            sb.AppendLine("    Log records:");
            foreach (var ev in e.Evidence)
                sb.AppendLine($"      - {ev.Time:yyyy-MM-dd HH:mm:ss}  {ev.Log} #{ev.Id} {ev.Provider}: {Truncate(ev.Message, 300)}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ToCsv(IEnumerable<RebootEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Category,Title,Summary,ShutdownTime,BootTime,DowntimeSeconds,PreviousUptimeSeconds,DumpPath");
        foreach (var e in entries)
        {
            sb.Append(Csv(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
              .Append(Csv(Format.Category(e.Category))).Append(',')
              .Append(Csv(e.Title)).Append(',')
              .Append(Csv(e.Summary)).Append(',')
              .Append(Csv(e.ShutdownTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")).Append(',')
              .Append(Csv(e.BootTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")).Append(',')
              .Append(e.Downtime is TimeSpan d ? ((long)d.TotalSeconds).ToString() : "").Append(',')
              .Append(e.PreviousUptime is TimeSpan u ? ((long)u.TotalSeconds).ToString() : "").Append(',')
              .Append(Csv(e.DumpPath ?? ""))
              .AppendLine();
        }
        return sb.ToString();
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
