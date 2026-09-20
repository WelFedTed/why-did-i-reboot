using System.Net.Http;
using System.Text.Json;

namespace WhyDidIReboot.Core;

public sealed record UpdateInfo(Version Latest, string Tag, string Url, bool IsNewer);

/// <summary>Looks up the newest GitHub Release and compares it with the running version.</summary>
public static class UpdateChecker
{
    public const string Repo = "WelFedTed/why-did-i-reboot";
    public static readonly string ReleasesUrl = $"https://github.com/{Repo}/releases";
    public static readonly string LatestApiUrl = $"https://api.github.com/repos/{Repo}/releases/latest";

    public static async Task<UpdateInfo> CheckAsync(HttpClient http, Version current, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Parse(json, current);
    }

    /// <summary>Pure parse of the /releases/latest payload so it can be tested without a network.</summary>
    public static UpdateInfo Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesUrl : ReleasesUrl;
        var latest = ParseTag(tag) ?? throw new FormatException($"Release tag '{tag}' is not a version.");
        return new UpdateInfo(latest, tag, url, latest > Normalise(current));
    }

    /// <summary>"v1.2.3", "1.2.3" or "v1.2.3-beta.1" → 1.2.3. Pre-release suffixes are ignored.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s, out var v) ? Normalise(v) : null;
    }

    /// <summary>Pads to three components so 0.3 == 0.3.0 and 0.3.0.0 == 0.3.0 compare equal.</summary>
    public static Version Normalise(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
