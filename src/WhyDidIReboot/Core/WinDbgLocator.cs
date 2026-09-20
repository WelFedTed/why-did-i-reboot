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

    /// <summary>
    /// Opens the dump, logs "!analyze -v" to <paramref name="logPath"/>, closes the log and ends the session.
    /// WinDbg cannot take quotes inside the -c command, so the log path must contain no spaces or quotes;
    /// use <see cref="AnalysisWorkDir"/> to get such a folder.
    /// </summary>
    public static string AnalyzeToLogArguments(string dumpPath, string logPath)
    {
        if (logPath.IndexOfAny(new[] { ' ', '"', ';' }) >= 0)
            throw new ArgumentException("The log path must not contain spaces, quotes or semicolons.", nameof(logPath));
        // "qq" rather than "q": WinDbgX treats a single q as quitting a remote client and stays open.
        return $"-z \"{dumpPath}\" -c \".logopen {logPath}; !analyze -v; .logclose; qq\"";
    }

    private static readonly string[] DebuggerProcessNames = { "DbgX.Shell", "WinDbgX", "windbg", "EngHost" };

    /// <summary>
    /// Closes debugger windows that are showing <paramref name="dumpPath"/> (title match), politely first,
    /// then forcibly after <paramref name="grace"/>. Returns how many were closed.
    /// </summary>
    public static int CloseDebuggerWindowsFor(string dumpPath, TimeSpan grace)
    {
        var name = Path.GetFileName(dumpPath);
        var closed = 0;
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (!DebuggerProcessNames.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                var title = proc.MainWindowTitle;
                if (string.IsNullOrEmpty(title) || !title.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!proc.CloseMainWindow() || !proc.WaitForExit((int)grace.TotalMilliseconds)) proc.Kill(entireProcessTree: true);
                closed++;
            }
            catch
            {
                // Access denied or already gone.
            }
            finally
            {
                proc.Dispose();
            }
        }
        return closed;
    }

    /// <summary>
    /// A writable folder whose path has no spaces, for WinDbg log files. Tries the Public profile
    /// (C:\Users\Public), then ProgramData, then the temp folder; <paramref name="probe"/> and
    /// <paramref name="env"/> are injectable for tests.
    /// </summary>
    public static string? AnalysisWorkDir(Func<string, bool>? probe = null, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        probe ??= dir =>
        {
            try { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, ".probe"), ""); File.Delete(Path.Combine(dir, ".probe")); return true; }
            catch { return false; }
        };
        var candidates = new[] { env("PUBLIC"), env("ProgramData"), env("TEMP"), env("TMP"), @"C:\" }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.Combine(root!, "WhyDidIReboot", "analysis"));
        return candidates.FirstOrDefault(dir => !dir.Contains(' ') && probe(dir));
    }

    /// <summary>"071226-7000-01.dmp" → "071226-7000-01.analyze.txt", with anything WinDbg's command parser dislikes replaced.</summary>
    public static string LogFileName(string dumpPath)
    {
        var name = Path.GetFileNameWithoutExtension(dumpPath);
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
        return (safe.Length == 0 ? "dump" : safe) + ".analyze.txt";
    }

    /// <summary>
    /// Waits for WinDbg to finish writing the log: the file exists, is no longer open for writing, and
    /// has not grown for a couple of seconds. Returns the text, or null on timeout.
    /// </summary>
    public static async Task<string?> WaitForLogAsync(string logPath, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        long lastSize = -1;
        var stableSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct).ConfigureAwait(false);
            if (!File.Exists(logPath)) continue;
            var size = new FileInfo(logPath).Length;
            if (size != lastSize) { lastSize = size; stableSince = DateTime.UtcNow; continue; }
            if (size == 0 || DateTime.UtcNow - stableSince < TimeSpan.FromSeconds(2)) continue;
            try
            {
                using var f = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.None);
                using var r = new StreamReader(f);
                return await r.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Still open by WinDbg; keep waiting.
            }
        }
        return null;
    }

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
