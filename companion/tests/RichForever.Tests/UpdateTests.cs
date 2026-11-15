using System.Text.Json;
using RichForever.App;
using RichForever.Core;

namespace RichForever.Tests;

public class UpdateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "richforever-update-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Versions_follow_semver_precedence()
    {
        string[] ordered = ["0.1.0-alpha", "0.1.0-alpha.1", "0.1.0-alpha.beta", "0.1.0-beta", "0.1.0-beta.2", "0.1.0-beta.11", "0.1.0-rc.1", "0.1.0", "0.2.0", "0.10.0", "1.0.0", "99999999999999999999.0.0"];
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            Assert.True(Updates.Compare(ordered[i], ordered[i + 1]) < 0, $"{ordered[i]} < {ordered[i + 1]}");
            Assert.True(Updates.Compare(ordered[i + 1], ordered[i]) > 0, $"{ordered[i + 1]} > {ordered[i]}");
        }
        Assert.Equal(0, Updates.Compare("1.2.3-beta.1", "1.2.3-beta.1+build.5"));
    }

    private static string Release(string tag, bool draft = false, string host = "github.com", params string[] assets) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        draft,
        prerelease = tag.Contains('-', StringComparison.Ordinal),
        html_url = $"https://github.com/clintgalendez/richforever/releases/tag/{tag}",
        assets = (assets.Length > 0 ? assets : [$"RichForever-{tag[1..]}-win-x64.zip", $"RichForever-{tag[1..]}-win-x64-framework-dependent.zip", "SHA256SUMS.txt"])
            .Select(name => new { name, browser_download_url = $"https://{host}/clintgalendez/richforever/releases/download/{tag}/{name}", size = 1 }),
    });

    private static string Releases(params string[] releases) => "[" + string.Join(",", releases) + "]";

    [Fact]
    public void Picks_the_newest_matching_release()
    {
        var json = Releases(Release("v0.3.0-beta.1", draft: true), Release("v0.2.0-beta.1"), Release("v0.2.0"), Release("v0.1.0-beta.3"), Release("v0.1.0-beta.2"));

        var update = Updates.Pick(json, "0.1.0-beta.2", "win-x64-framework-dependent");

        Assert.NotNull(update);
        Assert.Equal("0.2.0", update.Version);
        Assert.Equal("RichForever-0.2.0-win-x64-framework-dependent.zip", update.PackageName);
        Assert.Equal("https://github.com/clintgalendez/richforever/releases/download/v0.2.0/RichForever-0.2.0-win-x64-framework-dependent.zip", update.Package.ToString());
        Assert.EndsWith("/v0.2.0/SHA256SUMS.txt", update.Checksums.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://github.com/clintgalendez/richforever/releases/tag/v0.2.0", update.Page.ToString());
    }

    [Fact]
    public void Stable_builds_ignore_prereleases()
    {
        var json = Releases(Release("v1.1.0-beta.1"), Release("v1.0.1"), Release("v1.0.0"));

        Assert.Equal("1.0.1", Updates.Pick(json, "1.0.0", "win-x64")?.Version);
        Assert.Null(Updates.Pick(json, "1.0.1", "win-x64"));
        Assert.Equal("1.1.0-beta.1", Updates.Pick(json, "1.0.1-beta.1", "win-x64")?.Version);
    }

    [Fact]
    public void Skips_releases_without_a_usable_package()
    {
        var json = Releases(
            Release("v0.5.0", assets: ["RichForever-0.5.0-win-x64.zip"]),
            Release("v0.4.0", host: "example.com"),
            Release("not-a-version"),
            Release("v0.2.0"));

        Assert.Equal("0.2.0", Updates.Pick(json, "0.1.0", "win-x64")?.Version);
        Assert.Null(Updates.Pick(Releases(), "0.1.0", "win-x64"));
    }

    [Fact]
    public void Reads_sha256sum_files()
    {
        var sums = "AAAA  RichForever-0.2.0-win-x64.zip\nbbbb  RichForever-0.2.0-win-x64-framework-dependent.zip\n";

        Assert.Equal("aaaa", Updates.ChecksumFor(sums, "RichForever-0.2.0-win-x64.zip"));
        Assert.Equal("bbbb", Updates.ChecksumFor(sums.Replace("\n", "\r\n", StringComparison.Ordinal), "RichForever-0.2.0-win-x64-framework-dependent.zip"));
        Assert.Null(Updates.ChecksumFor(sums, "RichForever-0.2.0.zip"));
    }

    private string Folder(string name, params (string File, string Content)[] files)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(path);
        foreach (var (file, content) in files) File.WriteAllText(Path.Combine(path, file), content);
        return path;
    }

    [Fact]
    public void Apply_replaces_files_and_keeps_the_old_ones_aside()
    {
        var app = Folder("app", ("My RichForever.exe", "old exe"), ("LICENSE", "old license"), ("notes.txt", "user file"));
        var package = Folder("package", ("RichForever.exe", "new exe"), ("LICENSE", "new license"), ("README.txt", "new readme"));

        Updater.Apply(package, app, "My RichForever.exe");

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(app, "My RichForever.exe")));
        Assert.Equal("new license", File.ReadAllText(Path.Combine(app, "LICENSE")));
        Assert.Equal("new readme", File.ReadAllText(Path.Combine(app, "README.txt")));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(app, "notes.txt")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(app, "My RichForever.exe" + Updater.OldSuffix)));
        Assert.False(File.Exists(Path.Combine(app, "RichForever.exe")));
        Assert.Empty(Directory.GetFiles(package));
    }

    [Fact]
    public void Apply_rolls_back_when_a_file_cannot_be_replaced()
    {
        var app = Folder("app", ("RichForever.exe", "old exe"), ("LICENSE", "old license"));
        var package = Folder("package", ("RichForever.exe", "new exe"), ("LICENSE", "new license"), ("README.txt", "new readme"));
        // A folder where RichForever.exe.update-old should go makes that rename fail; Windows lists
        // files alphabetically, so LICENSE and README.txt are already in place by then.
        Directory.CreateDirectory(Path.Combine(app, "RichForever.exe" + Updater.OldSuffix));

        Assert.ThrowsAny<Exception>(() => Updater.Apply(package, app, "RichForever.exe"));

        Assert.Equal("old exe", File.ReadAllText(Path.Combine(app, "RichForever.exe")));
        Assert.Equal("old license", File.ReadAllText(Path.Combine(app, "LICENSE")));
        Assert.False(File.Exists(Path.Combine(app, "README.txt")));
        Assert.False(File.Exists(Path.Combine(app, "LICENSE" + Updater.OldSuffix)));
    }

    [Fact]
    public void Development_builds_do_not_update()
    {
        Assert.Null(Updater.Flavor);
        Assert.False(Updater.Supported);
    }
}
