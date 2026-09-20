using System.Text.RegularExpressions;

namespace WhyDidIReboot.Core;

/// <summary>KB numbers in update titles and the Microsoft Support pages that describe them.</summary>
public static partial class KnowledgeBase
{
    [GeneratedRegex(@"\bKB\s?(\d{6,8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex KbPattern();

    /// <summary>"2026-06 Security Update (KB5094126) (26200.8655)" → "KB5094126", or null.</summary>
    public static string? KbNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = KbPattern().Match(title);
        return m.Success ? "KB" + m.Groups[1].Value : null;
    }

    /// <summary>The support article for a KB number, e.g. https://support.microsoft.com/help/5094126.</summary>
    public static string? Url(string? kb)
    {
        var digits = KbNumber(kb)?[2..];
        return digits is null ? null : $"https://support.microsoft.com/help/{digits}";
    }
}
