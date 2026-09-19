namespace WhyDidIReboot.Core;

/// <summary>Accent colour per category, tuned separately for light and dark backgrounds.</summary>
public static class CategoryPalette
{
    public sealed record Colors(string Light, string Dark);

    public static readonly IReadOnlyDictionary<RebootCategory, Colors> All = new Dictionary<RebootCategory, Colors>
    {
        [RebootCategory.WindowsUpdate] = new("#2563EB", "#6EA0FF"),
        [RebootCategory.UserInitiated] = new("#16A34A", "#4ADE80"),
        [RebootCategory.Application] = new("#0D9488", "#2DD4BF"),
        [RebootCategory.BlueScreen] = new("#DC2626", "#F87171"),
        [RebootCategory.Unexpected] = new("#EA580C", "#FB923C"),
        [RebootCategory.CleanNoReason] = new("#6B7280", "#9CA3AF"),
        [RebootCategory.Unknown] = new("#9CA3AF", "#7C8494"),
        [RebootCategory.LiveKernelEvent] = new("#CA8A04", "#FACC15"),
        [RebootCategory.Sleep] = new("#7C3AED", "#A78BFA"),
    };

    public static string Hex(RebootCategory c, bool dark) =>
        All.TryGetValue(c, out var v) ? (dark ? v.Dark : v.Light) : (dark ? "#9CA3AF" : "#6B7280");
}

public static class Format
{
    public static string Duration(TimeSpan? span)
    {
        if (span is not TimeSpan t) return "unknown";
        if (t < TimeSpan.Zero) t = t.Negate();
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min {t.Seconds} s";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours} h {t.Minutes} min";
        return $"{(int)t.TotalDays} d {t.Hours} h";
    }

    public static string When(DateTime? time) =>
        time is DateTime t ? t.ToString("ddd d MMM yyyy, h:mm:ss tt") : "unknown";

    public static string WhenShort(DateTime? time) =>
        time is DateTime t ? t.ToString("d MMM yyyy h:mm tt") : "unknown";

    public static string Category(RebootCategory c) => c switch
    {
        RebootCategory.WindowsUpdate => "Windows Update",
        RebootCategory.UserInitiated => "User",
        RebootCategory.Application => "App or service",
        RebootCategory.BlueScreen => "Blue screen",
        RebootCategory.Unexpected => "Power loss / freeze",
        RebootCategory.CleanNoReason => "Clean, no reason",
        RebootCategory.Unknown => "Unknown",
        RebootCategory.LiveKernelEvent => "Kernel error (no reboot)",
        RebootCategory.Sleep => "Sleep / wake",
        _ => c.ToString(),
    };
}
