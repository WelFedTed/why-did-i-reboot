using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace WhyDidIReboot.Core;

/// <summary>
/// Whether the app is running as administrator, and how to start it again elevated. The app works
/// without elevation; it only matters for crash dumps and offline logs that ordinary accounts cannot read.
/// </summary>
public static class Elevation
{
    /// <summary>ERROR_CANCELLED: the user answered No (or closed) the UAC prompt.</summary>
    public const int UacCancelled = 1223;

    /// <summary>True when this process holds an elevated administrator token.</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Start info that launches <paramref name="exe"/> through the UAC prompt ("runas").</summary>
    public static ProcessStartInfo RelaunchStartInfo(string exe) => new(exe)
    {
        UseShellExecute = true,   // the runas verb only exists for shell launches
        Verb = "runas",
        WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
    };

    /// <summary>
    /// Starts an elevated copy of <paramref name="exe"/>. Blocks while the UAC prompt is open. Returns false
    /// when the user declined it; other failures throw.
    /// </summary>
    public static bool Relaunch(string exe, Func<ProcessStartInfo, Process?>? start = null)
    {
        start ??= Process.Start;
        try
        {
            using var _ = start(RelaunchStartInfo(exe));
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelled)
        {
            return false;
        }
    }
}
