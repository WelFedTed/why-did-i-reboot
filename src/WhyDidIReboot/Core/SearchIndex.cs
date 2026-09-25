using System.Runtime.CompilerServices;

namespace WhyDidIReboot.Core;

/// <summary>
/// Everything on a card that a search can hit, tagged with the section it lives in so the UI can
/// open the right expander. The expander labels themselves are indexed too, under
/// <see cref="Labels"/>, so they match and highlight without forcing the block open.
/// </summary>
public static class SearchIndex
{
    public const string Head = "head";
    public const string Updates = "updates";
    public const string Details = "details";
    public const string Labels = "labels";

    public const string UpdatesLabel = "Installed updates";
    public const string DetailsLabel = "Details and log records";

    /// <summary>Splits a query into distinct whitespace-separated terms; all must match for a card to show.</summary>
    public static string[] Terms(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : query.Split(' ', '\t', '\r', '\n').Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IEnumerable<(string Section, string Text)> Fields(RebootEntry e)
    {
        yield return (Head, e.Title);
        yield return (Head, e.Summary);
        yield return (Head, Format.Category(e.Category));
        yield return (Head, Format.When(e.Timestamp));
        if (e.ShutdownTime is not null) yield return (Head, Format.When(e.ShutdownTime));
        if (e.BootTime is not null) yield return (Head, Format.When(e.BootTime));
        foreach (var l in e.Links) yield return (Head, l.Label);

        if (e.Updates.Count > 0) yield return (Labels, UpdatesLabel);
        yield return (Labels, DetailsLabel);

        foreach (var u in e.Updates)
        {
            yield return (Updates, u.Title);
            yield return (Updates, u.Group);
            if (u.Kb is not null) yield return (Updates, u.Kb);
            if (u.Description is not null) yield return (Updates, u.Description);
            if (u.Category is not null) yield return (Updates, u.Category);
            if (u.Failed) yield return (Updates, "failed");
        }

        foreach (var d in e.Details)
        {
            yield return (Details, d.Key);
            yield return (Details, d.Value);
        }
        if (e.DumpPath is not null) yield return (Details, e.DumpPath);
        foreach (var v in e.Evidence)
        {
            yield return (Details, v.Message);
            yield return (Details, v.Provider);
            yield return (Details, $"{v.Log} {v.Id}");
        }
    }

    // Every keystroke re-filters every card and re-checks each card's expanders, so build a card's
    // fields (dates formatted and all) once. Entries are not changed after they are built.
    private static readonly ConditionalWeakTable<RebootEntry, (string Section, string Text)[]> Cache = new();

    private static (string Section, string Text)[] CachedFields(RebootEntry e) =>
        Cache.GetValue(e, x => Fields(x).ToArray());

    /// <summary>True when every term appears somewhere on the card (any section, case-insensitive).</summary>
    public static bool Matches(RebootEntry e, string[] terms)
    {
        if (terms.Length == 0) return true;
        var fields = CachedFields(e);
        return terms.All(t => fields.Any(f => f.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>True when any term appears in the given section, used to auto-expand collapsed blocks.</summary>
    public static bool SectionMatches(RebootEntry e, string[] terms, string section)
    {
        if (terms.Length == 0) return false;
        return CachedFields(e).Any(f => f.Section == section && terms.Any(t => f.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }
}
