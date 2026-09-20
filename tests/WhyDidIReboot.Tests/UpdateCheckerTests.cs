using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("v1.2.3.4", "1.2.3")]
    [InlineData("v0.4.0-beta.1", "0.4.0")]
    [InlineData("v0.4.0+build.7", "0.4.0")]
    [InlineData("  v2.0.0  ", "2.0.0")]
    public void Tags_parse_to_three_part_versions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData(null)]
    public void Non_version_tags_return_null(string? tag) =>
        Assert.Null(UpdateChecker.ParseTag(tag));

    [Fact]
    public void Newer_release_is_flagged()
    {
        var json = """{"tag_name":"v0.4.0","html_url":"https://github.com/WelFedTed/why-did-i-reboot/releases/tag/v0.4.0","name":"v0.4.0"}""";

        var info = UpdateChecker.Parse(json, new Version(0, 3, 0));

        Assert.True(info.IsNewer);
        Assert.Equal(new Version(0, 4, 0), info.Latest);
        Assert.Equal("v0.4.0", info.Tag);
        Assert.Equal("https://github.com/WelFedTed/why-did-i-reboot/releases/tag/v0.4.0", info.Url);
    }

    [Fact]
    public void Same_release_is_not_newer_regardless_of_component_count()
    {
        var json = """{"tag_name":"v0.3.0","html_url":"https://example/r"}""";

        Assert.False(UpdateChecker.Parse(json, new Version(0, 3, 0)).IsNewer);
        Assert.False(UpdateChecker.Parse(json, new Version(0, 3, 0, 0)).IsNewer);
        Assert.False(UpdateChecker.Parse(json, new Version(0, 3)).IsNewer);
    }

    [Fact]
    public void Older_release_is_not_newer()
    {
        var json = """{"tag_name":"v0.2.0","html_url":"https://example/r"}""";

        Assert.False(UpdateChecker.Parse(json, new Version(0, 3, 0)).IsNewer);
    }

    [Fact]
    public void Missing_url_falls_back_to_the_releases_page()
    {
        var info = UpdateChecker.Parse("""{"tag_name":"v9.0.0"}""", new Version(0, 3, 0));

        Assert.True(info.IsNewer);
        Assert.Equal(UpdateChecker.ReleasesUrl, info.Url);
    }

    [Fact]
    public void Unparseable_tag_throws_a_format_error()
    {
        Assert.Throws<FormatException>(() => UpdateChecker.Parse("""{"tag_name":"nightly"}""", new Version(0, 3, 0)));
    }

    [Fact]
    public void Api_url_points_at_the_repository()
    {
        Assert.Equal("https://api.github.com/repos/WelFedTed/why-did-i-reboot/releases/latest", UpdateChecker.LatestApiUrl);
    }
}
