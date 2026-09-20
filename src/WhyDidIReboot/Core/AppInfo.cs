using System.Reflection;

namespace WhyDidIReboot.Core;

/// <summary>The running app's version, as stamped from the csproj.</summary>
public static class AppInfo
{
    private static readonly Lazy<string> Version = new(() =>
    {
        var asm = typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');            // strip any "+commit" suffix
            return plus >= 0 ? info[..plus] : info;
        }
        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : UpdateChecker.Normalise(v).ToString();
    });

    /// <summary>"0.7.0"</summary>
    public static string VersionString => Version.Value;

    /// <summary>"v0.7.0"</summary>
    public static string VersionTag => "v" + Version.Value;
}
