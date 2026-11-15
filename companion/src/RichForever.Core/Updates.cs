using System.Text.Json;
using System.Text.RegularExpressions;

namespace RichForever.Core;

/// <summary>A newer release with the companion ZIP for this build's flavour.</summary>
public sealed record AvailableUpdate(string Version, Uri Page, string PackageName, Uri Package, Uri Checksums);

/// <summary>Picks updates from the GitHub Releases API (GET /repos/{owner}/{repo}/releases).</summary>
public static partial class Updates
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();

    /// <summary>
    /// The newest release above <paramref name="currentVersion"/> that has
    /// RichForever-{version}-{flavor}.zip and SHA256SUMS.txt. Prereleases are offered only to
    /// prerelease builds; drafts, malformed tags and non-github.com URLs are ignored.
    /// </summary>
    public static AvailableUpdate? Pick(string releasesJson, string currentVersion, string flavor)
    {
        using var document = JsonDocument.Parse(releasesJson);
        var wantsPrereleases = IsPrerelease(currentVersion);
        AvailableUpdate? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = tag.StartsWith('v') ? tag[1..] : tag;
            if (!VersionPattern().IsMatch(version) || release.GetProperty("draft").GetBoolean()) continue;
            if (IsPrerelease(version) && !wantsPrereleases) continue;
            if (Compare(version, best?.Version ?? currentVersion) <= 0) continue;

            var packageName = $"RichForever-{version}-{flavor}.zip";
            if (GitHubUrl(release.GetProperty("html_url").GetString()) is not Uri page
                || Asset(release, packageName) is not Uri package
                || Asset(release, "SHA256SUMS.txt") is not Uri checksums) continue;
            best = new AvailableUpdate(version, page, packageName, package, checksums);
        }
        return best;
    }

    /// <summary>The lower-case SHA-256 for <paramref name="fileName"/> in a sha256sum file, or null.</summary>
    public static string? ChecksumFor(string sums, string fileName) =>
        sums.Split('\n')
            .Select(line => line.Trim().Split("  ", 2))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1] == fileName)?[0].ToLowerInvariant();

    /// <summary>Semantic Versioning 2.0 precedence; build metadata (+...) is ignored.</summary>
    public static int Compare(string a, string b)
    {
        var (coreA, preA) = Split(a);
        var (coreB, preB) = Split(b);
        for (var i = 0; i < 3; i++)
        {
            var c = CompareNumbers(coreA.ElementAtOrDefault(i) ?? "0", coreB.ElementAtOrDefault(i) ?? "0");
            if (c != 0) return c;
        }
        // A release ranks above its prereleases.
        if (preA is null || preB is null) return (preA is null).CompareTo(preB is null);
        var x = preA.Split('.');
        var y = preB.Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool numericX = IsNumber(x[i]), numericY = IsNumber(y[i]);
            var c = numericX && numericY ? CompareNumbers(x[i], y[i])
                : numericX != numericY ? (numericX ? -1 : 1)
                : Math.Sign(string.CompareOrdinal(x[i], y[i]));
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    private static bool IsPrerelease(string version) => version.Split('+')[0].Contains('-', StringComparison.Ordinal);

    private static (string[] Core, string? Prerelease) Split(string version)
    {
        version = version.Split('+')[0];
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (version.Split('.'), null) : (version[..dash].Split('.'), version[(dash + 1)..]);
    }

    private static bool IsNumber(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    /// <summary>Digit strings of any length, without parsing (no overflow).</summary>
    private static int CompareNumbers(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : Math.Sign(string.CompareOrdinal(a, b));
    }

    private static Uri? Asset(JsonElement release, string name)
    {
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == name) return GitHubUrl(asset.GetProperty("browser_download_url").GetString());
        }
        return null;
    }

    private static Uri? GitHubUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" ? uri : null;
}
