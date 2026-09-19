namespace WhyDidIReboot.Core;

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
