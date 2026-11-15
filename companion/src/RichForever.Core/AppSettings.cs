using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RichForever.Core;

public enum DiscordConnectMode
{
    /// <summary>Connect to Discord only while WoW Forever is running (default, lightest).</summary>
    WhenGameRunning,

    /// <summary>Connect to Discord as soon as RichForever starts.</summary>
    Always,
}

public sealed record PresenceButton(string Label, string Url);

/// <summary>User preferences, stored as JSON in %APPDATA%\RichForever\settings.json.</summary>
public sealed partial class AppSettings
{
    /// <summary>RichForever's own Discord application ("World of Warcraft Forever"), with all artwork uploaded.</summary>
    public const string DefaultApplicationId = "1571656707078103191";

    /// <summary>A custom Discord application ID; empty means <see cref="DefaultApplicationId"/>.</summary>
    public string DiscordApplicationId { get; set; } = "";

    // Privacy
    public bool PausePresence { get; set; }
    public bool HideCharacterName { get; set; }
    public bool HideSessionTime { get; set; }
    public bool HideZone { get; set; }
    public bool HideInstance { get; set; }

    // Presence content
    public bool ShowStats { get; set; }
    public bool ShowGroupSize { get; set; } = true;
    public bool ShowCombat { get; set; }
    public List<PresenceButton> Buttons { get; set; } = [];

    // Lifecycle
    public bool StartWithWindows { get; set; }
    public DiscordConnectMode DiscordMode { get; set; } = DiscordConnectMode.WhenGameRunning;
    public bool ExitWhenGameCloses { get; set; }

    // Advanced
    public int GamePollSeconds { get; set; } = 5;
    public int CaptureIntervalMilliseconds { get; set; } = 1000;
    public List<string> GameProcessNames { get; set; } = ["Wow", "WowB"];
    public bool RequireForeverFolder { get; set; } = true;
    public bool VerboseLogging { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [GeneratedRegex("^[0-9]{17,20}$")]
    private static partial Regex ApplicationIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex ProcessNamePattern();

    public string EffectiveApplicationId => DiscordApplicationId.Length == 0 ? DefaultApplicationId : DiscordApplicationId;

    public bool HasValidApplicationId => ApplicationIdPattern().IsMatch(EffectiveApplicationId);

    /// <summary>Load settings. A missing file gives defaults; an unreadable one gives defaults and a warning.</summary>
    public static AppSettings Load(string path, out string? warning)
    {
        warning = null;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            return settings.Normalize();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warning = $"Settings file could not be read ({e.Message}); using defaults.";
            return new AppSettings();
        }
    }

    /// <summary>Write atomically so a crash never leaves a half-written file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Clamp numbers, drop invalid buttons and process names.</summary>
    public AppSettings Normalize()
    {
        DiscordApplicationId = (DiscordApplicationId ?? "").Trim();
        GamePollSeconds = Math.Clamp(GamePollSeconds, 2, 60);
        CaptureIntervalMilliseconds = Math.Clamp(CaptureIntervalMilliseconds, 250, 5000);
        if (!Enum.IsDefined(DiscordMode)) DiscordMode = DiscordConnectMode.WhenGameRunning;
        Buttons = (Buttons ?? []).Where(IsValidButton).Take(2).ToList();
        GameProcessNames = (GameProcessNames ?? []).Where(n => n is not null && ProcessNamePattern().IsMatch(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (GameProcessNames.Count == 0) GameProcessNames = ["Wow", "WowB"];
        return this;
    }

    public static bool IsValidButton(PresenceButton? button) =>
        button is not null
        && !string.IsNullOrWhiteSpace(button.Label) && button.Label.Length <= 32
        && button.Url is { Length: <= 512 }
        && Uri.TryCreate(button.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
