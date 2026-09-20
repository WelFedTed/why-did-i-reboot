using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WhyDidIReboot;

public enum ThemeMode { System, Light, Dark }

/// <summary>Swaps the light/dark resource dictionary, follows the Windows setting, and remembers the choice.</summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;
    private static bool _initialized;

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static bool IsDark { get; private set; }

    /// <summary>Raised after the resource dictionary has been swapped.</summary>
    public static event Action? Changed;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Mode = LoadSetting();
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Mode == ThemeMode.System)
                Application.Current?.Dispatcher.Invoke(Apply);
        };
    }

    public static void SetMode(ThemeMode mode)
    {
        if (mode == Mode && _current is not null) return;
        Mode = mode;
        SaveSetting(mode);
        Apply();
    }

    private static void Apply()
    {
        var dark = Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemIsDark(),
        };

        var app = Application.Current;
        if (app is null) return;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        var merged = app.Resources.MergedDictionaries;

        // Later merged dictionaries win lookups, so drop every previous theme dictionary
        // (including the default one declared in App.xaml) and append the new one last.
        foreach (var old in merged.Where(IsThemeDictionary).ToList()) merged.Remove(old);
        merged.Add(dict);
        _current = dict;
        IsDark = dark;

        foreach (Window w in app.Windows) ApplyTitleBar(w);
        Changed?.Invoke();
    }

    private static bool IsThemeDictionary(ResourceDictionary d) =>
        d.Source?.OriginalString is { } s &&
        (s.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) || s.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));

    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Asks DWM to draw the caption bar dark or light to match the content.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var value = IsDark ? 1 : 0;
        try
        {
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 20H1+, 19 on older builds.
            if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
        }
        catch
        {
            // Older Windows without the attribute; the caption stays light.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static ThemeMode LoadSetting() =>
        Enum.TryParse<ThemeMode>(AppSettings.Current.Theme, true, out var m) ? m : ThemeMode.System;

    private static void SaveSetting(ThemeMode mode)
    {
        AppSettings.Current.Theme = mode.ToString();
        AppSettings.Current.Save();
    }
}
