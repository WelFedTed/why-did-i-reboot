using System.IO;
using System.IO.Compression;
using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wdir-tests-" + Guid.NewGuid().ToString("N"));

    public UpdateInstallerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string relative, string content)
    {
        var p = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    // ------------------------------------------------------------ release parsing

    private const string ReleaseJson = """
        {
          "tag_name": "v0.6.0",
          "html_url": "https://github.com/WelFedTed/why-did-i-reboot/releases/tag/v0.6.0",
          "assets": [
            { "name": "WhyDidIReboot.exe", "browser_download_url": "https://example/WhyDidIReboot.exe", "size": 145000000, "digest": "sha256:AB12cd" },
            { "name": "WhyDidIReboot-framework-dependent.zip", "browser_download_url": "https://example/fd.zip", "size": 200000 },
            { "name": "junk", "size": 1 }
          ]
        }
        """;

    [Fact]
    public void Assets_are_parsed_with_size_and_digest()
    {
        var info = UpdateChecker.Parse(ReleaseJson, new Version(0, 5, 0));

        Assert.True(info.IsNewer);
        Assert.Equal(2, info.Assets.Count);   // the entry without a download url is dropped
        var exe = info.Assets.Single(a => a.Name == "WhyDidIReboot.exe");
        Assert.Equal(145000000, exe.Size);
        Assert.Equal("ab12cd", exe.Sha256);
        Assert.Null(info.Assets.Single(a => a.Name.EndsWith(".zip")).Sha256);
    }

    [Fact]
    public void Asset_choice_follows_the_install_flavour()
    {
        var info = UpdateChecker.Parse(ReleaseJson, new Version(0, 5, 0));

        Assert.Equal("WhyDidIReboot.exe", UpdateInstaller.ChooseAsset(info, frameworkDependent: false)!.Name);
        Assert.Equal("WhyDidIReboot-framework-dependent.zip", UpdateInstaller.ChooseAsset(info, frameworkDependent: true)!.Name);

        var none = UpdateChecker.Parse("""{"tag_name":"v9.0.0","assets":[]}""", new Version(0, 5, 0));
        Assert.Null(UpdateInstaller.ChooseAsset(none, false));
    }

    [Fact]
    public void Framework_dependent_install_is_detected_by_the_dll_beside_the_exe()
    {
        var exe = Write("app\\WhyDidIReboot.exe", "MZ");
        Assert.False(UpdateInstaller.IsFrameworkDependent(exe));
        Write("app\\WhyDidIReboot.dll", "x");
        Assert.True(UpdateInstaller.IsFrameworkDependent(exe));
    }

    // ------------------------------------------------------------ verification

    [Fact]
    public void Verify_accepts_matching_size_and_digest_and_rejects_mismatches()
    {
        var file = Write("dl.bin", "hello");
        var sha = UpdateInstaller.Sha256Of(file);
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", sha);

        UpdateInstaller.Verify(file, new ReleaseAsset("dl.bin", "u", 5, sha));
        UpdateInstaller.Verify(file, new ReleaseAsset("dl.bin", "u", 0, null));   // nothing advertised: nothing to check

        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Verify(file, new ReleaseAsset("dl.bin", "u", 6, sha)));
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Verify(file, new ReleaseAsset("dl.bin", "u", 5, "00" + sha[2..])));
    }

    [Fact]
    public void Non_executable_download_is_rejected()
    {
        var file = Write("not-an-exe.exe", "<html>oops</html>");
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.VerifyExecutable(file, new Version(0, 6, 0)));
    }

    [Fact]
    public void Real_executable_must_carry_the_expected_version()
    {
        // The test host is a genuine PE with a version resource, so it passes the header check and fails the version check.
        var host = Environment.ProcessPath!;
        var ex = Assert.Throws<InvalidDataException>(() => UpdateInstaller.VerifyExecutable(host, new Version(99, 99, 99)));
        Assert.Contains("reports version", ex.Message);
    }

    // ------------------------------------------------------------ staging and swapping

    [Fact]
    public void Zip_is_staged_and_must_contain_the_exe()
    {
        var zip = Path.Combine(_dir, "fd.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var s = z.CreateEntry("WhyDidIReboot.exe").Open()) s.Write("MZ"u8);
            using (var s = z.CreateEntry("WhyDidIReboot.dll").Open()) s.Write("dll"u8);
        }

        var staged = UpdateInstaller.StageZip(zip, Path.Combine(_dir, "staged"));

        Assert.Equal(2, staged.Count);
        Assert.True(File.Exists(staged["WhyDidIReboot.exe"]));

        var bad = Path.Combine(_dir, "bad.zip");
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
        using (var s = z.CreateEntry("readme.txt").Open()) s.Write("x"u8);
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.StageZip(bad, Path.Combine(_dir, "staged2")));
    }

    [Fact]
    public void Swap_renames_existing_files_to_old_and_moves_new_ones_in()
    {
        var app = Path.Combine(_dir, "app");
        Write("app\\WhyDidIReboot.exe", "old exe");
        Write("app\\WhyDidIReboot.dll", "old dll");
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WhyDidIReboot.exe"] = Write("new\\WhyDidIReboot.exe", "new exe"),
            ["WhyDidIReboot.dll"] = Write("new\\WhyDidIReboot.dll", "new dll"),
            ["extra.json"] = Write("new\\extra.json", "{}"),
        };

        UpdateInstaller.Swap(app, staged);

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(app, "WhyDidIReboot.exe")));
        Assert.Equal("new dll", File.ReadAllText(Path.Combine(app, "WhyDidIReboot.dll")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(app, "extra.json")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(app, "WhyDidIReboot.exe.old")));
        Assert.False(File.Exists(Path.Combine(_dir, "new", "WhyDidIReboot.exe")));

        Assert.Equal(2, UpdateInstaller.CleanupOldFiles(app));
        Assert.Empty(Directory.GetFiles(app, "*.old"));
        Assert.Equal(0, UpdateInstaller.CleanupOldFiles(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void Failed_swap_restores_the_original_files()
    {
        var app = Path.Combine(_dir, "app");
        Write("app\\WhyDidIReboot.exe", "old exe");
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WhyDidIReboot.exe"] = Write("new\\WhyDidIReboot.exe", "new exe"),
            ["WhyDidIReboot.dll"] = Path.Combine(_dir, "new", "does-not-exist.dll"),   // second move throws
        };

        Assert.ThrowsAny<IOException>(() => UpdateInstaller.Swap(app, staged));

        Assert.Equal("old exe", File.ReadAllText(Path.Combine(app, "WhyDidIReboot.exe")));
        Assert.False(File.Exists(Path.Combine(app, "WhyDidIReboot.exe.old")));
        Assert.False(File.Exists(Path.Combine(app, "WhyDidIReboot.dll")));
    }
}
