using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using RichForever.Core;

namespace RichForever.App;

/// <summary>
/// Self-update from GitHub Releases: find a newer release, download the ZIP of this build's flavour,
/// check it against the release's SHA256SUMS.txt and put its files next to the executable. A running
/// executable can be renamed but not overwritten, so every replaced file is first renamed to
/// *.update-old; those are deleted at the next update check, after the old process has exited.
/// </summary>
internal static class Updater
{
    public const string OldSuffix = ".update-old";
    private const string ReleasesUrl = "https://api.github.com/repos/clintgalendez/richforever/releases?per_page=30";
    private const string PackagedExecutable = "RichForever.exe";

    /// <summary>
    /// Release ZIP flavour ("win-x64" or "win-x64-framework-dependent"). Only scripts/package.ps1 sets
    /// it, so development builds never replace themselves.
    /// </summary>
    public static string? Flavor { get; } = typeof(Updater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateFlavor")?.Value;

    /// <summary>Captured at start: the path the new version is written to and restarted from.</summary>
    public static string Executable { get; } = Environment.ProcessPath ?? "";

    public static bool Supported => !string.IsNullOrEmpty(Flavor) && Executable.Length > 0;

    private static string Staging => Path.Combine(AppPaths.LogDirectory, "update");

    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancel)
    {
        using var http = Client();
        var json = await http.GetStringAsync(ReleasesUrl, cancel).ConfigureAwait(false);
        return Updates.Pick(json, TrayApplication.Version, Flavor!);
    }

    /// <summary>Download, verify and put the new version in place of <paramref name="executable"/> and its siblings.</summary>
    public static async Task InstallAsync(AvailableUpdate update, string executable, CancellationToken cancel)
    {
        using var http = Client();
        var sums = await http.GetStringAsync(update.Checksums, cancel).ConfigureAwait(false);
        var expected = Updates.ChecksumFor(sums, update.PackageName) ?? throw new InvalidDataException($"SHA256SUMS.txt has no entry for {update.PackageName}.");

        if (Directory.Exists(Staging)) Directory.Delete(Staging, recursive: true);
        Directory.CreateDirectory(Staging);
        var zip = Path.Combine(Staging, update.PackageName);
        using (var response = await http.GetAsync(update.Package, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var file = File.Create(zip);
            await using (file.ConfigureAwait(false)) await response.Content.CopyToAsync(file, cancel).ConfigureAwait(false);
        }
        var actual = Convert.ToHexStringLower(await HashAsync(zip, cancel).ConfigureAwait(false));
        if (actual != expected) throw new InvalidDataException($"{update.PackageName} does not match SHA256SUMS.txt.");

        var files = Path.Combine(Staging, "files");
        ZipFile.ExtractToDirectory(zip, files);
        if (Directory.GetDirectories(files).Length > 0 || !File.Exists(Path.Combine(files, PackagedExecutable)))
        {
            throw new InvalidDataException($"{update.PackageName} does not have the expected files.");
        }
        Apply(files, Path.GetDirectoryName(executable)!, Path.GetFileName(executable));
        Directory.Delete(Staging, recursive: true);
    }

    /// <summary>
    /// Move every file from <paramref name="source"/> into <paramref name="target"/>, renaming files
    /// it replaces to *.update-old. RichForever.exe goes to <paramref name="executableName"/>, in case
    /// the user renamed it. On any failure the original files are put back.
    /// </summary>
    internal static void Apply(string source, string target, string executableName)
    {
        var placed = new List<(string Path, bool Replaced)>();
        try
        {
            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                var destination = Path.Combine(target, name.Equals(PackagedExecutable, StringComparison.OrdinalIgnoreCase) ? executableName : name);
                var replaced = File.Exists(destination);
                if (replaced) File.Move(destination, destination + OldSuffix, overwrite: true);
                placed.Add((destination, replaced));
                File.Move(file, destination);
            }
        }
        catch
        {
            foreach (var (path, replaced) in placed)
            {
                try
                {
                    if (replaced) File.Move(path + OldSuffix, path, overwrite: true);
                    else File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Error($"Could not restore {path}: {e.Message}");
                }
            }
            throw;
        }
    }

    /// <summary>Delete what an earlier update left behind. Files still in use stay for next time.</summary>
    public static void CleanUp()
    {
        if (!Supported) return;
        try
        {
            foreach (var old in Directory.GetFiles(Path.GetDirectoryName(Executable)!, "*" + OldSuffix)) File.Delete(old);
            if (Directory.Exists(Staging)) Directory.Delete(Staging, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"Update clean-up: {e.Message}");
        }
    }

    /// <summary>Start the (new) executable. Called after the single-instance mutex is released.</summary>
    public static void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Executable, "--updated") { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Error($"Could not restart {Executable}: {e.Message}");
        }
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancel)
    {
        var file = File.OpenRead(path);
        await using (file.ConfigureAwait(false)) return await SHA256.HashDataAsync(file, cancel).ConfigureAwait(false);
    }

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RichForever", TrayApplication.Version));
        return http;
    }
}
