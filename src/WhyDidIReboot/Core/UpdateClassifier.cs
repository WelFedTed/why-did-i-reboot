using System.Text.RegularExpressions;

namespace WhyDidIReboot.Core;

/// <summary>Sorts installed updates into a few human groups for display: Drivers, Windows, Security, Apps, Other.</summary>
public static partial class UpdateClassifier
{
    public const string Drivers = "Drivers";
    public const string Windows = "Windows";
    public const string Security = "Security";
    public const string Apps = "Apps";
    public const string Other = "Other";

    /// <summary>Display order for groups on a card.</summary>
    public static readonly string[] Order = { Windows, Security, Drivers, Apps, Other };

    // "Intel - System - 2406.5.5.0", "Razer Inc - HIDClass - 6.2.9200.16547", "NVIDIA Display Driver Update (32.0.15.9186)"
    [GeneratedRegex(@"^[^-]+ - [^-]+ - \d+(\.\d+){1,3}$")]
    private static partial Regex DriverTitle();

    public static string Group(string title, string? category)
    {
        var t = title ?? "";
        var c = category ?? "";

        if (c.Contains("Driver", StringComparison.OrdinalIgnoreCase) || DriverTitle().IsMatch(t.Trim()) ||
            t.Contains("Driver Update", StringComparison.OrdinalIgnoreCase) || t.Contains(" driver", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Firmware", StringComparison.OrdinalIgnoreCase))
            return Drivers;

        if (c.Contains("Definition", StringComparison.OrdinalIgnoreCase) || c.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Defender", StringComparison.OrdinalIgnoreCase) || t.Contains("Security Intelligence", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Malicious Software Removal", StringComparison.OrdinalIgnoreCase) || t.Contains("Windows Security platform", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("antimalware", StringComparison.OrdinalIgnoreCase))
            return Security;

        if (t.Contains("Cumulative Update", StringComparison.OrdinalIgnoreCase) || t.Contains("Security Update for Windows", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Feature Update", StringComparison.OrdinalIgnoreCase) || t.Contains("Servicing Stack", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Windows 1", StringComparison.OrdinalIgnoreCase) || t.Contains("Windows Server", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Dynamic Update", StringComparison.OrdinalIgnoreCase) || t.Contains("Setup Dynamic", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Windows Configuration Update", StringComparison.OrdinalIgnoreCase) ||
            (c.Contains("Security Updates", StringComparison.OrdinalIgnoreCase) && t.Contains("Update (KB", StringComparison.OrdinalIgnoreCase)) ||
            c.Contains("Feature Pack", StringComparison.OrdinalIgnoreCase) || c.Contains("Service Pack", StringComparison.OrdinalIgnoreCase) ||
            c.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) || c.Contains("Rollup", StringComparison.OrdinalIgnoreCase))
            return Windows;

        if (t.Contains(".NET", StringComparison.OrdinalIgnoreCase) || t.Contains("Office", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase) || t.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) || t.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Teams", StringComparison.OrdinalIgnoreCase) || t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) || t.Contains("Microsoft 365", StringComparison.OrdinalIgnoreCase))
            return Apps;

        return Other;
    }
}
