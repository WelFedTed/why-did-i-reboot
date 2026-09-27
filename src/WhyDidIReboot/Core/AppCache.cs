using System.IO;

namespace WhyDidIReboot.Core;

/// <summary>
/// The files the app keeps to save work: WinDbg analyses that Ask AI reuses, and downloads left over from
/// updating. Settings → Clear cache measures and removes them. Nothing here is a setting or the user's data.
/// </summary>
public static class AppCache
{
    public sealed record Summary(int Files, long Bytes)
    {
        public static readonly Summary Empty = new(0, 0);
    }

    /// <summary>Where Update now downloads and unpacks a release before swapping it in.</summary>
    public static string UpdateDownloadDir => Path.Combine(Path.GetTempPath(), "WhyDidIReboot-update");

    /// <summary>Every cache folder: all places a WinDbg analysis may have been written, plus the update downloads.</summary>
    public static IEnumerable<string> Folders() =>
        WinDbgLocator.AnalysisDirCandidates().Append(UpdateDownloadDir).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Counts the files in the folders that exist.</summary>
    public static Summary Measure(IEnumerable<string> folders)
    {
        int files = 0; long bytes = 0;
        foreach (var file in FilesIn(folders))
        {
            try { bytes += new FileInfo(file).Length; files++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new Summary(files, bytes);
    }

    /// <summary>
    /// Deletes every file in the folders and any empty sub-folders, leaving the folders themselves.
    /// Returns what was removed, and how many files could not be (open in WinDbg, or no permission).
    /// </summary>
    public static (Summary Removed, int Skipped) Clear(IEnumerable<string> folders)
    {
        int files = 0, skipped = 0; long bytes = 0;
        var list = folders.ToList();
        foreach (var file in FilesIn(list))
        {
            try
            {
                var size = new FileInfo(file).Length;
                File.Delete(file);
                files++; bytes += size;
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        foreach (var dir in list.Where(Directory.Exists))
        {
            // Deepest first, so a parent empties before it is tried.
            foreach (var sub in SafeEnumerate(() => Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)).OrderByDescending(d => d.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return (new Summary(files, bytes), skipped);
    }

    /// <summary>"3 files, 30 KB".</summary>
    public static string Describe(Summary s) =>
        s.Files == 0 ? "nothing" : $"{s.Files} file{(s.Files == 1 ? "" : "s")}, {Size(s.Bytes)}";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };

    private static IEnumerable<string> FilesIn(IEnumerable<string> folders) =>
        folders.Where(Directory.Exists)
               .SelectMany(dir => SafeEnumerate(() => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)))
               .Distinct(StringComparer.OrdinalIgnoreCase);

    private static List<string> SafeEnumerate(Func<IEnumerable<string>> list)
    {
        try { return list().ToList(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }
}
