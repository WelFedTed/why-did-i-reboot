using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WhyDidIReboot.Core;

/// <summary>
/// A report that carries its crash dumps: a .zip holding the app's CSV (<see cref="CsvReport"/>) as
/// "report.csv" and each card's dump file under "dumps/", laid out by the path recorded in the log
/// ("dumps/C/Windows/Minidump/100426-10875-01.dmp"). Opened on another PC, the dumps are unpacked and the
/// cards' recorded paths are mapped onto them, so Debug in WinDbg and Ask AI work on the right files.
/// </summary>
public static class ReportBundle
{
    public const string Extension = ".zip";
    public const string ReportEntry = "report.csv";
    public const string DumpsFolder = "dumps";
    private const string CompleteMarker = ".complete";

    /// <summary>Above this, a dump is stored with fast compression so a multi-gigabyte kernel dump does not take minutes.</summary>
    private const long FastCompressionAbove = 64L * 1024 * 1024;

    public static bool IsBundlePath(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>A dump to pack: the path the log recorded, and where that file is on this machine.</summary>
    public sealed record DumpFile(string RecordedPath, string LocalPath, long Bytes);

    /// <summary>
    /// What a bundle of these entries would hold, and which dumps cannot be included: <paramref name="Missing"/>
    /// are no longer on disk, <paramref name="Unreadable"/> exist but cannot be opened (Windows lets only
    /// administrators read most crash dumps).
    /// </summary>
    public sealed record Plan(IReadOnlyList<RebootEntry> Entries, IReadOnlyList<DumpFile> Dumps, IReadOnlyList<string> Missing, IReadOnlyList<string> Unreadable)
    {
        public long DumpBytes => Dumps.Sum(d => d.Bytes);
    }

    public sealed record WriteResult(int Entries, int Dumps, long DumpBytes, long FileBytes, IReadOnlyList<string> Skipped);

    /// <summary>Finds each distinct dump the entries name. <paramref name="mapPath"/> turns a recorded path into a local one.</summary>
    public static Plan PlanFor(IEnumerable<RebootEntry> entries, Func<string, string>? mapPath = null)
    {
        mapPath ??= p => p;
        var list = entries.ToList();
        var dumps = new List<DumpFile>();
        var missing = new List<string>();
        var unreadable = new List<string>();
        foreach (var recorded in list.Select(e => e.DumpPath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                // Open it rather than ask whether it exists: a dump is often visible but readable only when
                // elevated, and one in an admin-only folder (LiveKernelReports) looks absent to everyone else.
                var local = Path.GetFullPath(mapPath(recorded!));
                using var stream = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                dumps.Add(new DumpFile(recorded!, local, stream.Length));
            }
            catch (UnauthorizedAccessException)
            {
                unreadable.Add(recorded!);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                missing.Add(recorded!);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                missing.Add(recorded!);
            }
        }
        return new Plan(list, dumps, missing, unreadable);
    }

    /// <summary>
    /// Writes the bundle. Dumps that cannot be read (no permission, in use) are left out and listed in the
    /// result rather than failing the export. The file appears under its final name only when complete.
    /// </summary>
    public static WriteResult Write(string zipPath, Plan plan, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var temp = zipPath + ".partial";
        var skipped = new List<string>();
        int included = 0; long bytes = 0;
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                var report = zip.CreateEntry(ReportEntry, CompressionLevel.Optimal);
                using (var writer = new StreamWriter(report.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    writer.Write(CsvReport.ToCsv(plan.Entries));

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dump in plan.Dumps)
                {
                    ct.ThrowIfCancellationRequested();
                    var name = EntryNameFor(dump.RecordedPath);
                    if (!names.Add(name)) continue;
                    progress?.Report($"Packing {Path.GetFileName(dump.LocalPath)} ({AppCache.Size(dump.Bytes)})…");
                    try
                    {
                        using var source = new FileStream(dump.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var entry = zip.CreateEntry(name, dump.Bytes > FastCompressionAbove ? CompressionLevel.Fastest : CompressionLevel.Optimal);
                        entry.LastWriteTime = File.GetLastWriteTime(dump.LocalPath);   // the analysis cache keys on it
                        using var target = entry.Open();
                        var buffer = new byte[1 << 20];
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            target.Write(buffer, 0, read);
                        }
                        included++; bytes += dump.Bytes;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add($"{dump.RecordedPath} ({ex.Message.Trim()})");
                    }
                }
            }
            File.Move(temp, zipPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
        skipped.AddRange(plan.Unreadable.Select(m => m + " (access denied: readable only as administrator)"));
        // Without elevation an admin-only folder hides its files, so "not found" cannot be told from "not visible".
        var notFound = Elevation.IsElevated() ? " (file not found)" : " (file not found, or not visible without administrator rights)";
        skipped.AddRange(plan.Missing.Select(m => m + notFound));
        return new WriteResult(plan.Entries.Count, included, bytes, new FileInfo(zipPath).Length, skipped);
    }

    /// <summary>Where unpacked bundles live. It is a cache: Clear cache empties it and an open bundle is unpacked again.</summary>
    public static string DefaultCacheRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhyDidIReboot", "bundles");

    /// <summary>
    /// Unpacks the bundle (once; the folder is named after the file's path, size and time) and returns the
    /// location to load. Throws <see cref="InvalidDataException"/> if the zip is not one of ours.
    /// </summary>
    public static LogLocation Open(string zipPath, string? cacheRoot = null)
    {
        var full = Path.GetFullPath(zipPath);
        var dir = Path.Combine(cacheRoot ?? DefaultCacheRoot, FolderNameFor(full));
        Extract(full, dir);
        return LogLocation.ForBundle(full, dir);
    }

    /// <summary>Unpacks an open bundle again if its folder was cleared in the meantime.</summary>
    public static void EnsureExtracted(LogLocation location)
    {
        if (location.BundlePath is null || location.Root is null) return;
        Extract(location.BundlePath, Path.GetDirectoryName(location.Root.TrimEnd('\\', '/'))!);
    }

    private static void Extract(string zipPath, string dir)
    {
        var marker = Path.Combine(dir, CompleteMarker);
        if (File.Exists(marker) && File.Exists(Path.Combine(dir, ReportEntry))) return;

        using var zip = ZipFile.OpenRead(zipPath);
        var report = zip.GetEntry(ReportEntry)
            ?? throw new InvalidDataException($"This zip is not a Why Did I Reboot report: it has no {ReportEntry}.");

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);   // half-unpacked earlier
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var wanted = zip.Entries.Where(e => e.Name.Length > 0 && (e == report || e.FullName.StartsWith(DumpsFolder + "/", StringComparison.OrdinalIgnoreCase))).ToList();
        var needed = wanted.Sum(e => e.Length);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.AvailableFreeSpace < needed)
            throw new IOException($"Not enough free space on {drive.Name} to unpack the report's crash dumps: {AppCache.Size(needed)} needed, {AppCache.Size(drive.AvailableFreeSpace)} free.");

        foreach (var entry in wanted)
        {
            // Only ever write inside the bundle's own folder, whatever the entry is called.
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);   // keeps the entry's last-write time
        }
        File.WriteAllText(marker, zipPath);
    }

    /// <summary>"C:\Windows\Minidump\x.dmp" → "dumps/C/Windows/Minidump/x.dmp". Paths without a drive letter go under "dumps/_/".</summary>
    public static string EntryNameFor(string recordedPath) =>
        DumpsFolder + "/" + string.Join("/", Segments(recordedPath));

    /// <summary>Where a recorded dump path lives inside an unpacked bundle's dumps folder.</summary>
    public static string LocalPathFor(string dumpsRoot, string recordedPath) =>
        Path.Combine(new[] { dumpsRoot }.Concat(Segments(recordedPath)).ToArray());

    private static IEnumerable<string> Segments(string recordedPath)
    {
        var p = recordedPath.Trim();
        var hasDrive = p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] is '\\' or '/';
        var parts = (hasDrive ? p[3..] : Path.GetFileName(p.TrimEnd('\\', '/')))
            .Split('\\', '/')
            .Where(s => s.Length > 0 && s != "." && s != "..")
            .Select(Safe)
            .ToList();
        if (parts.Count == 0) parts.Add("dump.dmp");
        return parts.Prepend(hasDrive ? char.ToUpperInvariant(p[0]).ToString() : "_");
    }

    private static string Safe(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(segment.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
    }

    private static string FolderNameFor(string fullZipPath)
    {
        var info = new FileInfo(fullZipPath);
        var key = $"{fullZipPath.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 4).ToLowerInvariant();
        var name = new string(Path.GetFileNameWithoutExtension(fullZipPath).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
        if (name.Length > 40) name = name[..40];
        return $"{(name.Length == 0 ? "report" : name)}.{tag}";
    }
}
