using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace WhyDidIReboot.Core;

/// <summary>
/// Downloads a release asset, verifies it, and swaps it into place next to the running executable.
/// Windows will not let a running .exe be overwritten, but it can be renamed, so every replaced file is
/// first renamed to "*.old" and the new file moved in; <see cref="CleanupOldFiles"/> removes the leftovers
/// on the next start.
/// </summary>
public static class UpdateInstaller
{
    public const string ExeName = "WhyDidIReboot.exe";
    public const string SelfContainedAsset = "WhyDidIReboot.exe";
    public const string FrameworkDependentAsset = "WhyDidIReboot-framework-dependent.zip";
    public const string OldSuffix = ".old";

    /// <summary>A framework-dependent install keeps WhyDidIReboot.dll beside the exe; the single-file build does not.</summary>
    public static bool IsFrameworkDependent(string exePath) =>
        File.Exists(Path.Combine(Path.GetDirectoryName(exePath) ?? "", "WhyDidIReboot.dll"));

    /// <summary>The asset that matches how this copy was installed, or null if the release lacks it.</summary>
    public static ReleaseAsset? ChooseAsset(UpdateInfo info, bool frameworkDependent)
    {
        var wanted = frameworkDependent ? FrameworkDependentAsset : SelfContainedAsset;
        return info.Assets.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Downloads to <paramref name="destination"/>, reporting 0..1 progress, then checks size and SHA-256.</summary>
    public static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
            }
        }

        Verify(destination, asset);
    }

    /// <summary>Size and digest checks against what the release API advertised.</summary>
    public static void Verify(string path, ReleaseAsset asset)
    {
        var length = new FileInfo(path).Length;
        if (asset.Size > 0 && length != asset.Size)
            throw new InvalidDataException($"Downloaded {length:N0} bytes but the release lists {asset.Size:N0}.");
        if (asset.Sha256 is not null)
        {
            var actual = Sha256Of(path);
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download's SHA-256 does not match the digest published with the release.");
        }
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Confirms the file is a Windows executable whose version resource matches the release.</summary>
    public static void VerifyExecutable(string path, Version expected)
    {
        using (var f = File.OpenRead(path))
        {
            var header = new byte[2];
            if (f.Read(header, 0, 2) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                throw new InvalidDataException("The downloaded file is not a Windows executable.");
        }
        var info = FileVersionInfo.GetVersionInfo(path);
        var text = info.ProductVersion ?? info.FileVersion;
        var found = UpdateChecker.ParseTag(text);
        if (found is null || found != UpdateChecker.Normalise(expected))
            throw new InvalidDataException($"The downloaded executable reports version '{text}', not {expected}.");
    }

    /// <summary>Extracts the framework-dependent zip and returns the staged file paths keyed by relative name.</summary>
    public static Dictionary<string, string> StageZip(string zipPath, string stagingDir)
    {
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(stagingDir);
        ZipFile.ExtractToDirectory(zipPath, stagingDir);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
            files[Path.GetRelativePath(stagingDir, f)] = f;
        if (!files.ContainsKey(ExeName)) throw new InvalidDataException("The zip does not contain " + ExeName + ".");
        return files;
    }

    /// <summary>
    /// Moves each staged file into <paramref name="appDir"/>, renaming any existing file to "*.old" first.
    /// If anything fails the renamed files are restored, so the installed copy is never left half replaced.
    /// </summary>
    public static void Swap(string appDir, IReadOnlyDictionary<string, string> staged)
    {
        var renamed = new List<(string original, string old)>();
        var moved = new List<string>();
        try
        {
            foreach (var (relative, newFile) in staged)
            {
                var target = Path.Combine(appDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    var old = target + OldSuffix;
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(target, old);
                    renamed.Add((target, old));
                }
                File.Move(newFile, target);
                moved.Add(target);
            }
        }
        catch
        {
            foreach (var m in moved) { try { File.Delete(m); } catch { } }
            foreach (var (original, old) in renamed) { try { if (!File.Exists(original)) File.Move(old, original); } catch { } }
            throw;
        }
    }

    /// <summary>Deletes leftovers from a previous swap. Safe to call every start.</summary>
    public static int CleanupOldFiles(string appDir)
    {
        var count = 0;
        if (!Directory.Exists(appDir)) return 0;
        foreach (var f in Directory.GetFiles(appDir, "*" + OldSuffix, SearchOption.AllDirectories))
        {
            try { File.Delete(f); count++; } catch { /* still locked by the exiting process; next time */ }
        }
        return count;
    }
}
