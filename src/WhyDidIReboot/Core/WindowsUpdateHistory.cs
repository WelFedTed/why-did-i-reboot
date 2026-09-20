using System.Runtime.InteropServices;

namespace WhyDidIReboot.Core;

/// <summary>
/// Reads this PC's Windows Update history through the Windows Update Agent COM API
/// (Microsoft.Update.Session). Each entry carries the update's title, a short description of
/// what it changes, its category and the Microsoft Support link, all available offline.
/// </summary>
public static class WindowsUpdateHistory
{
    /// <summary>The standard Windows Update classification names.</summary>
    private static readonly HashSet<string> Classifications = new(StringComparer.OrdinalIgnoreCase)
    {
        "Security Updates", "Critical Updates", "Definition Updates", "Update Rollups", "Updates",
        "Feature Packs", "Service Packs", "Drivers", "Driver Updates", "Tools", "Upgrades", "Microsoft Defender Antivirus",
    };

    private static readonly object Gate = new();
    private static Dictionary<string, UpdateDetails>? _cache;
    private static DateTime _cachedAt;

    /// <summary>
    /// Returns history keyed by KB number ("KB5094126") and, for entries without one such as driver
    /// updates, by the exact title. Empty (never null) if the API is unavailable.
    /// </summary>
    public static IReadOnlyDictionary<string, UpdateDetails> Load(List<string>? warnings = null)
    {
        lock (Gate)
        {
            if (_cache is not null && DateTime.Now - _cachedAt < TimeSpan.FromMinutes(10)) return _cache;
        }

        var result = new Dictionary<string, UpdateDetails>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (type is null) throw new InvalidOperationException("Windows Update Agent is not registered.");
            dynamic session = Activator.CreateInstance(type)!;
            dynamic searcher = session.CreateUpdateSearcher();
            int count = searcher.GetTotalHistoryCount();
            if (count > 0)
            {
                dynamic history = searcher.QueryHistory(0, count);
                for (var i = 0; i < count; i++)
                {
                    dynamic entry = history.Item(i);
                    string title = entry.Title ?? "";
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var key = KnowledgeBase.KbNumber(title) ?? title.Trim();

                    // Only the newest entry per update matters; history is newest-first.
                    if (result.ContainsKey(key)) continue;

                    string? description = null;
                    string? supportUrl = null;
                    string? category = null;
                    DateTime? date = null;
                    try { description = entry.Description; } catch { }
                    try { supportUrl = entry.SupportUrl; } catch { }
                    try { date = (DateTime)entry.Date; } catch { }
                    try
                    {
                        // Categories mix classifications ("Security Updates") with product names ("Windows 11",
                        // "EU Browser Choice Update-For Europe Only"). Only the classification is worth showing.
                        dynamic categories = entry.Categories;
                        int n = categories.Count;
                        for (var c = 0; c < n && category is null; c++)
                        {
                            string name = categories.Item(c).Name ?? "";
                            if (Classifications.Contains(name)) category = name;
                        }
                    }
                    catch { }

                    result[key] = new UpdateDetails(title, Clean(description), category, supportUrl, date);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException
                                   || ex.GetType().Name.Contains("RuntimeBinder", StringComparison.Ordinal))
        {
            warnings?.Add("Windows Update history is unavailable (" + ex.Message.Trim() + "), so update descriptions are omitted.");
        }

        lock (Gate)
        {
            _cache = result;
            _cachedAt = DateTime.Now;
        }
        return result;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r", " ").Replace("\n", " ");
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Trim();
    }
}
