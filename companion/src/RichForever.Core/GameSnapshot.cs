using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RichForever.Core;

public enum InstanceKind
{
    None,
    Dungeon,
    Raid,
    Battleground,
    Arena,
    Scenario,
    Unknown,
}

/// <summary>The character state reported by the addon. Every field is optional.</summary>
public sealed record GameSnapshot
{
    public string? Name { get; init; }
    public int? Level { get; init; }
    public string? ClassToken { get; init; }
    public string? ClassName { get; init; }
    public string? RaceToken { get; init; }
    public string? RaceName { get; init; }
    public string? Faction { get; init; }
    public int? Sex { get; init; }
    public string? Zone { get; init; }
    public string? SubZone { get; init; }
    public int? MapId { get; init; }
    public InstanceKind Instance { get; init; }
    public string? InstanceName { get; init; }
    public int? InstanceId { get; init; }
    public string? Difficulty { get; init; }
    public int? MaxPlayers { get; init; }
    public int? GroupSize { get; init; }
    public int? HealthPercent { get; init; }
    public int? PowerPercent { get; init; }
    public string? PowerToken { get; init; }
    public int? XpPercent { get; init; }
    public bool InCombat { get; init; }
    public bool IsDead { get; init; }
    public bool IsResting { get; init; }
    public bool IsAway { get; init; }
    public DateTimeOffset? SessionStart { get; init; }
}

/// <summary>
/// Parses and validates the addon payload: UTF-8 "key=value" lines. Structural problems reject the
/// whole payload; a known field with an invalid value is dropped; unknown keys are ignored so newer
/// addons keep working with older companions.
/// </summary>
public static partial class PayloadParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private static readonly Dictionary<string, InstanceKind> InstanceTypes = new(StringComparer.Ordinal)
    {
        ["none"] = InstanceKind.None,
        ["party"] = InstanceKind.Dungeon,
        ["raid"] = InstanceKind.Raid,
        ["pvp"] = InstanceKind.Battleground,
        ["arena"] = InstanceKind.Arena,
        ["scenario"] = InstanceKind.Scenario,
        ["unknown"] = InstanceKind.Unknown,
    };

    [GeneratedRegex("^[A-Za-z][A-Za-z_]{1,15}$")]
    private static partial Regex TokenPattern();

    public static GameSnapshot? Parse(ReadOnlySpan<byte> payload, out string? error)
    {
        error = null;
        if (payload.IsEmpty || payload.Length > StripDecoder.MaxPayload)
        {
            error = "payload size out of range";
            return null;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            error = "payload is not valid UTF-8";
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                error = "malformed line";
                return null;
            }
            var value = line[(separator + 1)..];
            if (value.Any(char.IsControl))
            {
                error = "control character in value";
                return null;
            }
            if (!fields.TryAdd(line[..separator], value))
            {
                error = "duplicate key";
                return null;
            }
        }

        if (!fields.TryGetValue("v", out var version) || version != "1")
        {
            error = "unsupported payload version";
            return null;
        }

        string? Text(string key, int max) =>
            fields.TryGetValue(key, out var v) && v.Length is > 0 && v.Length <= max && v.Trim().Length == v.Length ? v : null;
        string? Token(string key) => fields.TryGetValue(key, out var v) && TokenPattern().IsMatch(v) ? v : null;
        int? Int(string key, int min, int max) =>
            fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n >= min && n <= max ? n : null;

        var flags = fields.GetValueOrDefault("fl") ?? "";
        DateTimeOffset? sessionStart = null;
        if (fields.TryGetValue("ss", out var ss)
            && long.TryParse(ss, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds is > 946684800 and < 4102444800)
        {
            sessionStart = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return new GameSnapshot
        {
            Name = Text("n", 36),
            Level = Int("l", 1, 999),
            ClassToken = Token("c")?.ToUpperInvariant(),
            ClassName = Text("cn", 24),
            RaceToken = Token("r"),
            RaceName = Text("rn", 24),
            Faction = fields.GetValueOrDefault("f") is "Alliance" or "Horde" or "Neutral" ? fields["f"] : null,
            Sex = Int("g", 1, 3),
            Zone = Text("z", 48),
            SubZone = Text("sz", 48),
            MapId = Int("m", 1, 999999),
            Instance = InstanceTypes.GetValueOrDefault(fields.GetValueOrDefault("it") ?? "none", InstanceKind.Unknown),
            InstanceName = Text("in", 48),
            InstanceId = Int("ii", 1, 999999),
            Difficulty = Text("d", 24),
            MaxPlayers = Int("mp", 1, 99),
            GroupSize = Int("gs", 1, 99),
            HealthPercent = Int("hp", 0, 100),
            PowerPercent = Int("pp", 0, 100),
            PowerToken = Token("pt")?.ToUpperInvariant(),
            XpPercent = Int("xp", 0, 100),
            InCombat = flags.Contains('c', StringComparison.Ordinal),
            IsDead = flags.Contains('d', StringComparison.Ordinal),
            IsResting = flags.Contains('r', StringComparison.Ordinal),
            IsAway = flags.Contains('a', StringComparison.Ordinal),
            SessionStart = sessionStart,
        };
    }
}
