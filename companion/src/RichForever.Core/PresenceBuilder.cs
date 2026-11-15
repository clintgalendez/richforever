using System.Globalization;

namespace RichForever.Core;

/// <summary>Turns a game snapshot into the Discord activity, applying privacy settings.</summary>
public static class PresenceBuilder
{
    public const int MaxText = 128;
    public const string GameTitle = "World of Warcraft: Forever";

    public static Activity Build(GameSnapshot? snapshot, AppSettings settings, AssetCatalog catalog, DateTimeOffset? sessionStart, string partyId)
    {
        var timestamps = settings.HideSessionTime || sessionStart is null ? null : new ActivityTimestamps(sessionStart.Value.ToUnixTimeMilliseconds());
        var buttons = settings.Buttons.Where(AppSettings.IsValidButton).Take(2).Select(b => new ActivityButton(b.Label.Trim(), b.Url)).ToList();

        if (snapshot is null)
        {
            return new Activity
            {
                Details = "In menus",
                Timestamps = timestamps,
                Assets = new ActivityAssets { LargeImage = catalog.Default, LargeText = GameTitle },
                Buttons = buttons.Count > 0 ? buttons : null,
            };
        }

        var (largeImage, largeText) = Scene(snapshot, settings, catalog);
        return new Activity
        {
            Details = Fit(CharacterLine(snapshot, settings)),
            State = Fit(StateLine(snapshot, settings)),
            Timestamps = timestamps,
            Assets = new ActivityAssets
            {
                LargeImage = largeImage ?? catalog.Default,
                LargeText = Fit(largeText) ?? GameTitle,
                SmallImage = catalog.ForClass(snapshot.ClassToken),
                SmallText = Fit(ClassLine(snapshot, settings)),
            },
            Party = Party(snapshot, settings, partyId),
            Buttons = buttons.Count > 0 ? buttons : null,
        };
    }

    private static string CharacterLine(GameSnapshot s, AppSettings settings)
    {
        var description = Join(" ",
            s.Level is int level ? $"Level {level.ToString(CultureInfo.InvariantCulture)}" : null,
            s.RaceName ?? s.RaceToken,
            ClassLabel(s));
        if (description.Length == 0) description = "Adventuring";
        return !settings.HideCharacterName && s.Name is not null ? $"{s.Name} · {description}" : description;
    }

    private static string StateLine(GameSnapshot s, AppSettings settings)
    {
        string location;
        string verb;
        if (s.Instance == InstanceKind.None)
        {
            var zone = settings.HideZone ? null : s.Zone;
            location = zone ?? "the world";
            verb = "Exploring";
            if (s.IsResting && !settings.HideZone && (s.SubZone ?? s.Zone) is string rest)
            {
                location = rest;
                verb = "Resting in";
            }
        }
        else
        {
            location = settings.HideInstance ? "an instance" : s.InstanceName ?? "an instance";
            verb = s.Instance is InstanceKind.Battleground or InstanceKind.Arena && location != "an instance" ? "In" : "Inside";
        }

        if (s.IsDead) verb = "Dead in";
        else if (s.IsAway) verb = "Away in";
        else if (settings.ShowCombat && s.InCombat) verb = "In combat in";
        return $"{verb} {location}";
    }

    private static (string? Image, string? Text) Scene(GameSnapshot s, AppSettings settings, AssetCatalog catalog)
    {
        if (s.Instance != InstanceKind.None)
        {
            if (settings.HideInstance) return (null, null);
            var kind = KindLabel(s.Instance);
            var text = s.InstanceName is null ? kind : $"{kind}: {s.InstanceName}";
            if (s.Difficulty is not null) text += $" ({s.Difficulty})";
            return (catalog.ForInstance(s.InstanceId, s.InstanceName) ?? catalog.ForType(s.Instance), text);
        }
        if (settings.HideZone) return (catalog.ForType(InstanceKind.None), null);
        var zoneText = s.Zone is null ? null : s.SubZone is null ? s.Zone : $"{s.Zone} · {s.SubZone}";
        return (catalog.ForZone(s.MapId, s.Zone) ?? catalog.ForType(InstanceKind.None), zoneText);
    }

    private static string ClassLine(GameSnapshot s, AppSettings settings)
    {
        var line = Join(" · ", ClassLabel(s), s.Faction);
        if (!settings.ShowStats) return line;
        var stats = Join(" · ",
            s.HealthPercent is int hp ? $"HP {hp.ToString(CultureInfo.InvariantCulture)}%" : null,
            s.PowerPercent is int pp ? $"{PowerLabel(s.PowerToken)} {pp.ToString(CultureInfo.InvariantCulture)}%" : null,
            s.XpPercent is int xp ? $"XP {xp.ToString(CultureInfo.InvariantCulture)}%" : null);
        return Join(" · ", line, stats);
    }

    private static ActivityParty? Party(GameSnapshot s, AppSettings settings, string partyId)
    {
        if (!settings.ShowGroupSize || s.GroupSize is not int size || size < 2) return null;
        var max = s.Instance != InstanceKind.None && s.MaxPlayers is int mp && mp > 0 ? mp : size <= 5 ? 5 : 40;
        return new ActivityParty(partyId, [size, Math.Max(size, max)]);
    }

    private static string KindLabel(InstanceKind kind) => kind switch
    {
        InstanceKind.Dungeon => "Dungeon",
        InstanceKind.Raid => "Raid",
        InstanceKind.Battleground => "Battleground",
        InstanceKind.Arena => "Arena",
        InstanceKind.Scenario => "Scenario",
        _ => "Instance",
    };

    private static string? ClassLabel(GameSnapshot s) => s.ClassName ?? (s.ClassToken is null ? null : Title(s.ClassToken));

    private static string PowerLabel(string? token) => token is null ? "Power" : Title(token);

    private static string Title(string token) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.Replace('_', ' ').ToLowerInvariant());

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>Discord rejects strings shorter than 2 characters and truncates long ones.</summary>
    internal static string? Fit(string? text)
    {
        if (text is null) return null;
        text = text.Trim();
        if (text.Length < 2) return null;
        if (text.Length <= MaxText) return text;
        var cut = char.IsHighSurrogate(text[MaxText - 2]) ? MaxText - 2 : MaxText - 1;
        return string.Concat(text.AsSpan(0, cut), "…");
    }
}
