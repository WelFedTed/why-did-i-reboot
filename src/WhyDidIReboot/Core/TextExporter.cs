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

    /// <summary>A self-contained HTML report that mirrors the app's cards and follows the viewer's light/dark preference.</summary>
    public static string ToHtml(AnalysisResult result, IEnumerable<RebootEntry> entries, string rangeLabel)
    {
        var list = entries.ToList();
        var sb = new StringBuilder();
        var machine = H(Environment.MachineName);

        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
        sb.Append("<title>Why Did I Reboot — ").Append(machine).Append("</title>\n<style>\n");
        sb.Append(@"
:root{color-scheme:light dark;--bg:#f3f4f6;--card:#fff;--border:#e5e7eb;--text:#111827;--text2:#374151;--muted:#6b7280;--faint:#9ca3af;--evidence:#f9fafb;--link:#2563eb;--banner:#eff6ff;--banner-b:#bfdbfe;--banner-t:#1e3a8a;--warn:#b45309}
@media (prefers-color-scheme:dark){:root{--bg:#111318;--card:#1b1e25;--border:#2c3039;--text:#e6e8ee;--text2:#c0c5cf;--muted:#8b919e;--faint:#6b7280;--evidence:#14171d;--link:#6ea0ff;--banner:#172033;--banner-b:#27406b;--banner-t:#bfd3ff;--warn:#f0b45a}}
*{box-sizing:border-box}
body{margin:0;padding:32px 20px;background:var(--bg);color:var(--text);font:14px/1.45 ""Segoe UI Variable Text"",""Segoe UI"",system-ui,-apple-system,sans-serif}
.wrap{max-width:980px;margin:0 auto}
h1{font-size:26px;font-weight:600;margin:0 0 4px}
.sub{color:var(--muted);margin:0 0 18px;font-size:13px}
.banner{background:var(--banner);border:1px solid var(--banner-b);color:var(--banner-t);border-radius:8px;padding:10px 14px;margin-bottom:14px}
.warn{color:var(--warn);font-size:13px;margin-top:4px}
.counts{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 22px}
.chip{--col:var(--c);display:inline-flex;align-items:center;gap:8px;border:1px solid var(--col);background:color-mix(in srgb,var(--col) 12%,transparent);border-radius:14px;padding:4px 10px;font-size:12px}
.chip b{background:var(--col);color:#fff;border-radius:9px;padding:0 7px;font-size:11px}
h2.day{font-size:13px;font-weight:600;color:var(--muted);margin:26px 0 10px}
.card{--col:var(--c);background:var(--card);border:1px solid var(--border);border-left:6px solid var(--col);border-radius:10px;padding:14px 18px;margin-bottom:12px;box-shadow:0 1px 3px rgba(0,0,0,.05)}
@media (prefers-color-scheme:dark){.chip,.card{--col:var(--cd)}}
.head{display:flex;align-items:flex-start;gap:12px}
.dot{flex:none;width:36px;height:36px;border-radius:50%;background:color-mix(in srgb,var(--col) 14%,transparent);position:relative}
.dot::after{content:"""";position:absolute;inset:12px;border-radius:50%;background:var(--col)}
.title{font-size:16px;font-weight:600;line-height:1.3}
.when{font-size:12px;color:var(--muted);margin-top:2px}
.badge{margin-left:auto;flex:none;font-size:11px;font-weight:600;padding:3px 8px;border-radius:10px;background:color-mix(in srgb,var(--col) 14%,transparent);color:var(--col)}
.summary{color:var(--text2);margin:10px 0 0 48px}
details{margin:8px 0 0 48px;font-size:12.5px}
summary{cursor:pointer;color:var(--link);user-select:none}
table.kv{border-collapse:collapse;margin-top:8px}
.kv td{padding:2px 14px 2px 0;vertical-align:top}
.kv td:first-child{color:var(--muted);white-space:nowrap}
.logs{margin-top:10px;font-weight:600;color:var(--text2)}
.ev{background:var(--evidence);border-radius:6px;padding:6px 10px;margin-top:6px}
.ev .meta{color:var(--muted);font-size:11px}
.ev .msg{font:11.5px/1.4 ""Cascadia Mono"",Consolas,monospace;white-space:pre-wrap;word-break:break-word;margin-top:2px}
.foot{color:var(--faint);font-size:12px;margin-top:28px}
@media print{body{padding:0;background:#fff}.card{break-inside:avoid;box-shadow:none}details{display:block}details>summary{display:none}details>*{display:block}}
");
        sb.Append("</style>\n</head>\n<body>\n<div class=\"wrap\">\n");
        sb.Append("<h1>Why Did I Reboot</h1>\n");
        sb.Append($"<p class=\"sub\">Report for <b>{machine}</b> · generated {H(Format.When(DateTime.Now))} · range: {H(rangeLabel)} · {result.RecordsRead:N0} log records read</p>\n");
        sb.Append($"<div class=\"banner\">This PC has been running since {H(Format.When(result.CurrentBootTime))} ({H(Format.Duration(DateTime.Now - result.CurrentBootTime))} ago).");
        foreach (var w in result.Warnings) sb.Append($"<div class=\"warn\">{H(w)}</div>");
        sb.Append("</div>\n");

        sb.Append("<div class=\"counts\">");
        foreach (var group in list.GroupBy(e => e.Category).OrderByDescending(g => g.Count()))
            sb.Append($"<span class=\"chip\" style=\"--c:{CategoryPalette.Hex(group.Key, false)};--cd:{CategoryPalette.Hex(group.Key, true)}\">{H(Format.Category(group.Key))} <b>{group.Count()}</b></span>");
        sb.Append("</div>\n");

        if (list.Count == 0) sb.Append("<p class=\"sub\">No events in this range.</p>\n");

        string? day = null;
        foreach (var e in list)
        {
            if (e.DateGroup != day)
            {
                day = e.DateGroup;
                sb.Append($"<h2 class=\"day\">{H(day)}</h2>\n");
            }

            var when = new List<string> { Format.When(e.Timestamp) };
            if (e.IsReboot)
            {
                if (e.ShutdownTime is null && e.BootTime is not null) when[0] = "Back up at " + Format.When(e.BootTime);
                if (e.Downtime is TimeSpan d) when.Add("down for " + Format.Duration(d));
                if (e.PreviousUptime is TimeSpan u) when.Add("previous session ran " + Format.Duration(u));
            }

            sb.Append($"<div class=\"card\" style=\"--c:{CategoryPalette.Hex(e.Category, false)};--cd:{CategoryPalette.Hex(e.Category, true)}\">\n");
            sb.Append("<div class=\"head\"><div class=\"dot\"></div><div>");
            sb.Append($"<div class=\"title\">{H(e.Title)}</div><div class=\"when\">{H(string.Join("  ·  ", when))}</div></div>");
            sb.Append($"<span class=\"badge\">{H(Format.Category(e.Category))}</span></div>\n");
            sb.Append($"<div class=\"summary\">{H(e.Summary)}</div>\n");
            sb.Append("<details><summary>Details and log records</summary>\n<table class=\"kv\">");
            foreach (var d in e.Details) sb.Append($"<tr><td>{H(d.Key)}</td><td>{H(d.Value)}</td></tr>");
            sb.Append("</table>\n");
            if (e.Evidence.Count > 0)
            {
                sb.Append("<div class=\"logs\">Log records</div>\n");
                foreach (var ev in e.Evidence)
                    sb.Append($"<div class=\"ev\"><div class=\"meta\"><b>{ev.Time:d MMM yyyy HH:mm:ss}</b> · {H(ev.Log)} · Event {ev.Id} · {H(ev.Provider)}</div><div class=\"msg\">{H(ev.Message)}</div></div>\n");
            }
            sb.Append("</details>\n</div>\n");
        }

        sb.Append("<p class=\"foot\">Generated by Why Did I Reboot. Categories are inferred from the Windows System and Application event logs.</p>\n");
        sb.Append("</div>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string H(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
