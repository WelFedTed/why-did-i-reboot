using System.IO;

namespace WhyDidIReboot.Core;

/// <summary>Finds WinDbg (the Store/WinDbgX build or the Windows SDK build), and knows how to install it with winget.</summary>
public static class WinDbgLocator
{
    /// <summary>winget package id for the modern WinDbg.</summary>
    public const string WingetId = "Microsoft.WinDbg";
    public const string StoreUri = "ms-windows-store://pdp/?productid=9PGJGD53TN86";
    public const string LearnUrl = "https://learn.microsoft.com/windows-hardware/drivers/debugger/";

    /// <summary>Candidate executables in preference order, given an environment lookup (injectable for tests).</summary>
    public static IEnumerable<string> Candidates(Func<string, string?> env)
    {
        var local = env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "Microsoft", "WindowsApps", "WinDbgX.exe");   // Store / winget install alias

        foreach (var root in new[] { env("ProgramFiles(x86)"), env("ProgramFiles") })
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "Windows Kits", "10", "Debuggers", "x64", "windbg.exe");
            yield return Path.Combine(root, "Windows Kits", "10", "Debuggers", "x86", "windbg.exe");
        }

        var path = env("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(dir, "WinDbgX.exe");
            yield return Path.Combine(dir, "windbg.exe");
        }
    }

    /// <summary>The first candidate that exists, or null.</summary>
    public static string? Find(Func<string, bool>? exists = null, Func<string, string?>? env = null)
    {
        exists ??= File.Exists;
        env ??= Environment.GetEnvironmentVariable;
        return Candidates(env).FirstOrDefault(exists);
    }

    /// <summary>Opens the dump and runs the standard crash analysis straight away.</summary>
    public static string Arguments(string dumpPath) => $"-z \"{dumpPath}\" -c \"!analyze -v\"";

    /// <summary>winget.exe if it is installed (App Installer), or null.</summary>
    public static string? FindWinget(Func<string, bool>? exists = null, Func<string, string?>? env = null)
    {
        exists ??= File.Exists;
        env ??= Environment.GetEnvironmentVariable;
        var local = env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
        {
            var alias = Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
            if (exists(alias)) return alias;
        }
        foreach (var dir in (env("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = Path.Combine(dir, "winget.exe");
            if (exists(p)) return p;
        }
        return null;
    }

    /// <summary>Unattended install of WinDbg from the winget/msstore sources.</summary>
    public static string WingetInstallArguments =>
        $"install --id {WingetId} --exact --accept-source-agreements --accept-package-agreements --disable-interactivity";
}
