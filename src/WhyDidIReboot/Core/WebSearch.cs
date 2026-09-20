using System.Text.RegularExpressions;

namespace WhyDidIReboot.Core;

/// <summary>A web search engine that takes the query in its URL.</summary>
public sealed record SearchEngine(string Name, string UrlTemplate)
{
    public string Url(string query) => string.Format(UrlTemplate, Uri.EscapeDataString(query));
    public string ButtonLabel => "Search " + Name;
}

/// <summary>Builds a web search for a crash: the bugcheck name and code plus any driver names the card mentions.</summary>
public static partial class WebSearch
{
    public static readonly IReadOnlyList<SearchEngine> Engines = new[]
    {
        new SearchEngine("Google", "https://www.google.com/search?q={0}"),
        new SearchEngine("Bing", "https://www.bing.com/search?q={0}"),
        new SearchEngine("DuckDuckGo", "https://duckduckgo.com/?q={0}"),
        new SearchEngine("Brave", "https://search.brave.com/search?q={0}"),
        new SearchEngine("Startpage", "https://www.startpage.com/do/search?q={0}"),
    };

    public static SearchEngine EngineByName(string? name) =>
        Engines.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Engines[0];

    // "0x0000009F (DRIVER_POWER_STATE_FAILURE)" or "0x1CC (EXRESOURCE_TIMEOUT_LIVEDUMP)"
    [GeneratedRegex(@"0x([0-9A-Fa-f]+)\s*\(([A-Z0-9_]+)\)")]
    private static partial Regex CodeAndName();

    [GeneratedRegex(@"\b[A-Za-z0-9_\-]+\.sys\b", RegexOptions.IgnoreCase)]
    private static partial Regex DriverName();

    private static readonly string[] CodeDetailKeys = { "STOP code", "Live kernel event code" };

    /// <summary>
    /// e.g. "DRIVER_POWER_STATE_FAILURE 0x9F iaStorAC.sys pci.sys", or null when the card has no bugcheck code.
    /// Driver names come from the summary, details and log records, most recently mentioned first, at most three.
    /// </summary>
    public static string? QueryFor(RebootEntry e)
    {
        var codeValue = e.Details.FirstOrDefault(d => CodeDetailKeys.Contains(d.Key)).Value;
        if (string.IsNullOrEmpty(codeValue)) return null;
        var m = CodeAndName().Match(codeValue);
        if (!m.Success) return null;

        var code = "0x" + m.Groups[1].Value.TrimStart('0').ToUpperInvariant();
        if (code == "0x") code = "0x0";
        var name = m.Groups[2].Value;

        var texts = new List<string> { e.Summary };
        texts.AddRange(e.Details.Select(d => d.Value));
        texts.AddRange(e.Evidence.Select(v => v.Message));
        var drivers = texts
            .SelectMany(t => DriverName().Matches(t).Select(x => x.Value))
            .Where(d => !d.Equals("ntoskrnl.sys", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);

        var parts = new List<string> { name == "Unknown bugcheck" ? "bugcheck" : name, code };
        parts.AddRange(drivers);
        return string.Join(" ", parts);
    }
}
