using System.Globalization;
using System.IO;
using System.Text;

namespace WhyDidIReboot.Core;

/// <summary>
/// The app's CSV format: one row per card with the summary columns first, then the details, links,
/// installed updates and log records packed into multi-line cells so a file can be re-opened later
/// with everything intact.
/// </summary>
public static class CsvReport
{
    public static readonly string[] Columns =
    {
        "Timestamp", "Category", "Title", "Summary", "ShutdownTime", "BootTime", "DowntimeSeconds", "PreviousUptimeSeconds", "DumpPath",
        "Details", "Links", "Updates", "LogRecords",
    };

    private const string Stamp = "yyyy-MM-dd HH:mm:ss";
    private const char Tab = '\t';

    public static string ToCsv(IEnumerable<RebootEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns));
        foreach (var e in entries)
        {
            var cells = new[]
            {
                e.Timestamp.ToString(Stamp, CultureInfo.InvariantCulture),
                Format.Category(e.Category),
                e.Title,
                e.Summary,
                e.ShutdownTime?.ToString(Stamp, CultureInfo.InvariantCulture) ?? "",
                e.BootTime?.ToString(Stamp, CultureInfo.InvariantCulture) ?? "",
                e.Downtime is TimeSpan d ? ((long)d.TotalSeconds).ToString(CultureInfo.InvariantCulture) : "",
                e.PreviousUptime is TimeSpan u ? ((long)u.TotalSeconds).ToString(CultureInfo.InvariantCulture) : "",
                e.DumpPath ?? "",
                string.Join("\n", e.Details.Select(d => Field(d.Key) + Tab + Field(d.Value))),
                string.Join("\n", e.Links.Select(l => Field(l.Label) + Tab + Field(l.Url))),
                string.Join("\n", e.Updates.Select(x => string.Join(Tab, Field(x.Title), Field(x.Kb), Field(x.Description), Field(x.Category), Field(x.Url), x.Failed ? "1" : "0", Field(x.Group)))),
                string.Join("\n", e.Evidence.Select(v => string.Join(Tab, v.Time.ToString(Stamp, CultureInfo.InvariantCulture), Field(v.Log), v.Id.ToString(CultureInfo.InvariantCulture), Field(v.Provider), Field(v.Message)))),
            };
            sb.AppendLine(string.Join(",", cells.Select(Quote)));
        }
        return sb.ToString();
    }

    /// <summary>Reads a CSV file this app wrote. Rows that cannot be understood are skipped and reported.</summary>
    public static List<RebootEntry> Load(string path, List<string>? warnings = null) =>
        Parse(File.ReadAllText(path), warnings);

    public static List<RebootEntry> Parse(string text, List<string>? warnings = null)
    {
        var rows = ReadRows(text);
        if (rows.Count == 0) throw new InvalidDataException("The file is empty.");

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Col(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        var required = new[] { "Timestamp", "Category", "Title", "Summary" };
        var missing = required.Where(r => Col(r) < 0).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException("This is not a Why Did I Reboot CSV: missing column(s) " + string.Join(", ", missing) + ".");

        var byLabel = Enum.GetValues<RebootCategory>().ToDictionary(Format.Category, c => c, StringComparer.OrdinalIgnoreCase);
        var entries = new List<RebootEntry>();

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count == 1 && row[0].Length == 0) continue;   // blank line
            string Cell(string name) { var i = Col(name); return i >= 0 && i < row.Count ? row[i] : ""; }

            try
            {
                var timestamp = ParseStamp(Cell("Timestamp")) ?? throw new FormatException("bad Timestamp");
                var category = byLabel.TryGetValue(Cell("Category").Trim(), out var c) ? c
                    : Enum.TryParse<RebootCategory>(Cell("Category"), true, out var c2) ? c2
                    : RebootCategory.Unknown;

                var dump = Cell("DumpPath");
                entries.Add(new RebootEntry
                {
                    Timestamp = timestamp,
                    Category = category,
                    Title = Cell("Title"),
                    Summary = Cell("Summary"),
                    ShutdownTime = ParseStamp(Cell("ShutdownTime")),
                    BootTime = ParseStamp(Cell("BootTime")),
                    Downtime = ParseSeconds(Cell("DowntimeSeconds")),
                    PreviousUptime = ParseSeconds(Cell("PreviousUptimeSeconds")),
                    DumpPath = dump.Length > 0 ? dump : null,
                    DumpExists = dump.Length > 0 && File.Exists(dump),
                    Details = Lines(Cell("Details")).Select(l => Split(l, 2)).Where(p => p.Length == 2).Select(p => new KeyValuePair<string, string>(p[0], p[1])).ToList(),
                    Links = Lines(Cell("Links")).Select(l => Split(l, 2)).Where(p => p.Length == 2).Select(p => new LinkItem(p[0], p[1])).ToList(),
                    Updates = Lines(Cell("Updates")).Select(l => Split(l, 7)).Where(p => p.Length == 7)
                        .Select(p => new UpdateItem(p[0], Null(p[1]), Null(p[2]), Null(p[3]), Null(p[4]), p[5] == "1", p[6].Length > 0 ? p[6] : UpdateClassifier.Other)).ToList(),
                    Evidence = Lines(Cell("LogRecords")).Select(l => Split(l, 5)).Where(p => p.Length == 5 && ParseStamp(p[0]) is not null)
                        .Select(p => new EvidenceEvent(ParseStamp(p[0])!.Value, p[1], int.TryParse(p[2], out var id) ? id : 0, p[3], p[4])).ToList(),
                });
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
            {
                warnings?.Add($"CSV row {r + 1} skipped: {ex.Message}");
            }
        }
        return entries;
    }

    // ------------------------------------------------------------ helpers

    private static string Field(string? s) => (s ?? "").Replace('\t', ' ').Replace("\r", "").Replace('\n', ' ');
    private static string? Null(string s) => s.Length == 0 ? null : s;
    private static IEnumerable<string> Lines(string cell) => cell.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    private static string[] Split(string line, int parts) => line.Split(Tab, parts);

    private static DateTime? ParseStamp(string s) =>
        DateTime.TryParseExact(s.Trim(), Stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt) ? dt : null;

    private static TimeSpan? ParseSeconds(string s) =>
        long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? TimeSpan.FromSeconds(n) : null;

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>RFC 4180 reader: quoted fields may contain commas, quotes ("" escaped) and newlines.</summary>
    public static List<List<string>> ReadRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n': row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); break;
                default: cell.Append(ch); break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
