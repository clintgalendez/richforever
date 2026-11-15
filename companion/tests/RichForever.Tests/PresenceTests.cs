using System.Text;
using RichForever.Core;

namespace RichForever.Tests;

public class PresenceBuilderTests
{
    private static readonly AssetCatalog Catalog = AssetCatalog.LoadDefault();
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1763200000);

    private static readonly GameSnapshot Paladin = new()
    {
        Name = "Archer",
        Level = 60,
        ClassToken = "PALADIN",
        ClassName = "Paladin",
        RaceName = "Human",
        Faction = "Alliance",
        Zone = "Eastern Plaguelands",
        SubZone = "Darrowshire",
        MapId = 1423,
    };

    private static Activity Build(GameSnapshot? snapshot, AppSettings? settings = null) =>
        PresenceBuilder.Build(snapshot, settings ?? new AppSettings(), Catalog, Start, "party-1");

    [Fact]
    public void Open_world()
    {
        var a = Build(Paladin);

        Assert.Equal("Archer · Level 60 Human Paladin", a.Details);
        Assert.Equal("Exploring Eastern Plaguelands", a.State);
        Assert.Equal("zone_eastern_plaguelands", a.Assets!.LargeImage);
        Assert.Equal("Eastern Plaguelands · Darrowshire", a.Assets.LargeText);
        Assert.Equal("class_paladin", a.Assets.SmallImage);
        Assert.Equal("Paladin · Alliance", a.Assets.SmallText);
        Assert.Equal(Start.ToUnixTimeMilliseconds(), a.Timestamps!.Start);
        Assert.Null(a.Party);
        Assert.Null(a.Buttons);
    }

    [Fact]
    public void Dungeon_with_group()
    {
        var a = Build(Paladin with { Instance = InstanceKind.Dungeon, InstanceName = "Stratholme", InstanceId = 329, Difficulty = "Normal", MaxPlayers = 5, GroupSize = 3 });

        Assert.Equal("Inside Stratholme", a.State);
        Assert.Equal("inst_stratholme", a.Assets!.LargeImage);
        Assert.Equal("Dungeon: Stratholme (Normal)", a.Assets.LargeText);
        Assert.Equal([3, 5], a.Party!.Size);
        Assert.Equal("party-1", a.Party.Id);
    }

    [Fact]
    public void Raid_found_by_name_when_id_is_unknown()
    {
        var a = Build(Paladin with { Instance = InstanceKind.Raid, InstanceName = "Molten Core", InstanceId = 999 });

        Assert.Equal("Inside Molten Core", a.State);
        Assert.Equal("inst_molten_core", a.Assets!.LargeImage);
        Assert.Equal("Raid: Molten Core", a.Assets.LargeText);
    }

    [Fact]
    public void Unknown_instance_uses_neutral_text_and_type_badge()
    {
        var a = Build(Paladin with { Instance = InstanceKind.Dungeon });

        Assert.Equal("Inside an instance", a.State);
        Assert.Equal("type_dungeon", a.Assets!.LargeImage);
        Assert.Equal("Dungeon", a.Assets.LargeText);
    }

    [Fact]
    public void New_instance_without_art_falls_back_to_type_badge()
    {
        var a = Build(Paladin with { Instance = InstanceKind.Raid, InstanceName = "Karazhan Crypts", InstanceId = 4242 });

        Assert.Equal("Inside Karazhan Crypts", a.State);
        Assert.Equal("type_raid", a.Assets!.LargeImage);
    }

    [Fact]
    public void Battleground_reads_naturally()
    {
        var a = Build(Paladin with { Instance = InstanceKind.Battleground, InstanceName = "Warsong Gulch", InstanceId = 489 });

        Assert.Equal("In Warsong Gulch", a.State);
        Assert.Equal("inst_warsong_gulch", a.Assets!.LargeImage);
        Assert.Equal("Battleground: Warsong Gulch", a.Assets.LargeText);
    }

    [Fact]
    public void Unknown_zone_falls_back_to_world_badge()
    {
        var a = Build(Paladin with { Zone = "Brand New Isle", SubZone = null, MapId = 9999 });

        Assert.Equal("Exploring Brand New Isle", a.State);
        Assert.Equal("type_world", a.Assets!.LargeImage);
    }

    [Fact]
    public void Zone_found_by_name_when_map_is_unknown()
    {
        Assert.Equal("zone_ungoro_crater", Build(Paladin with { Zone = "Un'Goro Crater", MapId = null }).Assets!.LargeImage);
    }

    [Fact]
    public void Status_words_take_priority()
    {
        Assert.Equal("Resting in Darrowshire", Build(Paladin with { IsResting = true }).State);
        Assert.Equal("Away in Darrowshire", Build(Paladin with { IsAway = true, IsResting = true }).State);
        Assert.Equal("Away in Eastern Plaguelands", Build(Paladin with { IsAway = true }).State);
        Assert.Equal("Dead in Eastern Plaguelands", Build(Paladin with { IsDead = true, IsAway = true }).State);
        Assert.Equal("Exploring Eastern Plaguelands", Build(Paladin with { InCombat = true }).State);
        Assert.Equal("In combat in Eastern Plaguelands", Build(Paladin with { InCombat = true }, new AppSettings { ShowCombat = true }).State);
    }

    [Fact]
    public void Privacy_settings_hide_information()
    {
        var settings = new AppSettings { HideCharacterName = true, HideSessionTime = true, HideZone = true, HideInstance = true };

        var world = Build(Paladin with { IsResting = true }, settings);
        Assert.Equal("Level 60 Human Paladin", world.Details);
        Assert.Equal("Exploring the world", world.State);
        Assert.Null(world.Timestamps);
        Assert.Equal("type_world", world.Assets!.LargeImage);
        Assert.Equal(PresenceBuilder.GameTitle, world.Assets.LargeText);
        Assert.DoesNotContain("Archer", world.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("Plaguelands", world.ToJson(), StringComparison.Ordinal);

        var raid = Build(Paladin with { Instance = InstanceKind.Raid, InstanceName = "Molten Core", InstanceId = 409 }, settings);
        Assert.Equal("Inside an instance", raid.State);
        Assert.Equal("logo", raid.Assets!.LargeImage);
        Assert.DoesNotContain("Molten", raid.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_are_opt_in()
    {
        var snapshot = Paladin with { HealthPercent = 85, PowerPercent = 40, PowerToken = "RUNIC_POWER", XpPercent = 42 };

        Assert.Equal("Paladin · Alliance", Build(snapshot).Assets!.SmallText);
        Assert.Equal("Paladin · Alliance · HP 85% · Runic Power 40% · XP 42%", Build(snapshot, new AppSettings { ShowStats = true }).Assets!.SmallText);
    }

    [Fact]
    public void Group_size_can_be_hidden_and_guesses_max_outside_instances()
    {
        Assert.Equal([12, 40], Build(Paladin with { GroupSize = 12 }).Party!.Size);
        Assert.Null(Build(Paladin with { GroupSize = 3 }, new AppSettings { ShowGroupSize = false }).Party);
    }

    [Fact]
    public void Missing_data_degrades_gracefully()
    {
        var a = Build(new GameSnapshot());

        Assert.Equal("Adventuring", a.Details);
        Assert.Equal("Exploring the world", a.State);
        Assert.Equal("type_world", a.Assets!.LargeImage);
        Assert.Null(a.Assets.SmallImage);
        Assert.Null(a.Assets.SmallText);
    }

    [Fact]
    public void Unknown_class_has_no_icon_but_keeps_its_name()
    {
        var a = Build(Paladin with { ClassToken = "TINKER", ClassName = null });

        Assert.Null(a.Assets!.SmallImage);
        Assert.Equal("Archer · Level 60 Human Tinker", a.Details);
    }

    [Fact]
    public void No_snapshot_means_menus()
    {
        var a = Build(null);

        Assert.Equal("In menus", a.Details);
        Assert.Null(a.State);
        Assert.Equal("logo", a.Assets!.LargeImage);
    }

    [Fact]
    public void Only_valid_https_buttons_are_sent()
    {
        var settings = new AppSettings
        {
            Buttons =
            [
                new("Guild", "https://example.com/guild"),
                new("Bad", "http://example.com"),
                new(new string('x', 33), "https://example.com"),
                new("Two", "https://example.com/2"),
                new("Three", "https://example.com/3"),
            ],
        };

        var buttons = Build(Paladin, settings).Buttons!;

        Assert.Equal(["Guild", "Two"], buttons.Select(b => b.Label));
    }

    [Fact]
    public void Long_text_is_truncated_and_short_text_dropped()
    {
        Assert.Equal(PresenceBuilder.MaxText, PresenceBuilder.Fit(new string('a', 300))!.Length);
        Assert.EndsWith("…", PresenceBuilder.Fit(new string('a', 300)), StringComparison.Ordinal);
        Assert.Null(PresenceBuilder.Fit("x"));
        Assert.Null(PresenceBuilder.Fit("   "));
        var emoji = new string('a', 126) + "😀😀";
        Assert.False(char.IsHighSurrogate(PresenceBuilder.Fit(emoji)![^2]));
    }

    [Fact]
    public void Json_uses_discord_field_names_and_omits_nulls()
    {
        var json = Build(Paladin).ToJson();

        Assert.Contains("\"large_image\":\"zone_eastern_plaguelands\"", json, StringComparison.Ordinal);
        Assert.Contains("\"timestamps\":{\"start\":1763200000000}", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("party", json, StringComparison.Ordinal);
    }
}

public class AssetCatalogTests
{
    [Fact]
    public void Default_catalog_covers_classes_zones_and_instances()
    {
        var catalog = AssetCatalog.LoadDefault();

        Assert.Equal("logo", catalog.Default);
        Assert.Equal("class_warlock", catalog.ForClass("warlock"));
        Assert.Equal("zone_elwynn_forest", catalog.ForZone(1429, null));
        Assert.Equal("zone_zephras_isle", catalog.ForZone(2521, null));
        Assert.Equal("inst_the_temple_of_atalhakkar", catalog.ForInstance(null, "Sunken Temple"));
        Assert.Equal("type_pvp", catalog.ForType(InstanceKind.Arena));
        Assert.Equal("type_world", catalog.ForType(InstanceKind.None));
    }

    [Fact]
    public void Every_catalog_key_has_generated_art()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets"));
        var json = File.ReadAllText(Path.Combine(root, "catalog.json"));
        var keys = System.Text.RegularExpressions.Regex.Matches(json, "\"(?:key|default|[A-Z]+|world|dungeon|raid|battleground|arena|scenario|unknown)\": \"([a-z0-9_]+)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.True(keys.Count > 80);
        Assert.All(keys, key => Assert.True(File.Exists(Path.Combine(root, "discord", key + ".png")), key));
    }

    [Fact]
    public void Invalid_entries_are_skipped_and_urls_allowed()
    {
        var catalog = AssetCatalog.Parse("""
            {
              "default": "Not A Key",
              "classes": { "MAGE": "https://cdn.example.com/mage.png", "ROGUE": "http://insecure.example.com/x.png" },
              "zones": [ { "key": "zone_x", "names": ["X Land"], "mapIds": [5] }, { "key": "BAD KEY", "names": ["Y"] }, 42 ],
              "instances": "nope"
            }
            """);

        Assert.Null(catalog.Default);
        Assert.Equal("https://cdn.example.com/mage.png", catalog.ForClass("MAGE"));
        Assert.Null(catalog.ForClass("ROGUE"));
        Assert.Equal("zone_x", catalog.ForZone(5, null));
        Assert.Equal("zone_x", catalog.ForZone(null, "x land"));
        Assert.Null(catalog.ForZone(null, "Y"));
        Assert.Null(catalog.ForInstance(1, "anything"));
    }

    [Fact]
    public void Malformed_json_throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => AssetCatalog.Parse("{ not json"));
    }
}

public class PresenceEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1763203600);
    private static readonly byte[] World = Encoding.UTF8.GetBytes(Fixture.Payload("world"));

    [Fact]
    public void Nothing_is_shown_until_the_game_runs()
    {
        var engine = new PresenceEngine();
        var settings = new AppSettings();

        Assert.Null(engine.DesiredActivity(settings, AssetCatalog.LoadDefault(), Now));
        Assert.False(engine.WantsDiscord(settings));
        Assert.True(engine.WantsDiscord(new AppSettings { DiscordMode = DiscordConnectMode.Always }));

        engine.GameStarted();
        Assert.True(engine.WantsDiscord(settings));
        Assert.Equal("In menus", engine.DesiredActivity(settings, AssetCatalog.LoadDefault(), Now)!.Details);
    }

    [Fact]
    public void Payloads_update_the_snapshot_once()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();

        Assert.True(engine.OnPayload(World, Now));
        Assert.False(engine.OnPayload(World, Now.AddSeconds(1)));
        Assert.Equal("Archer", engine.Snapshot!.Name);
        Assert.Equal(Now.AddSeconds(1), engine.LastFrameAt);
    }

    [Fact]
    public void Invalid_payload_keeps_the_last_good_snapshot()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        engine.OnPayload(World, Now);

        Assert.False(engine.OnPayload("garbage"u8, Now));
        Assert.Equal("Archer", engine.Snapshot!.Name);
        Assert.Equal("malformed line", engine.LastPayloadError);
    }

    [Fact]
    public void Snapshot_survives_short_gaps_but_not_long_ones()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        engine.OnPayload(World, Now);

        Assert.False(engine.OnStripMissing(Now.AddSeconds(1)));
        Assert.False(engine.OnStripMissing(Now.AddSeconds(15)));
        Assert.NotNull(engine.Snapshot);
        Assert.True(engine.OnStripMissing(Now.AddSeconds(22)));
        Assert.Null(engine.Snapshot);

        Assert.True(engine.OnPayload(World, Now.AddSeconds(30)));
        Assert.False(engine.OnStripMissing(Now.AddSeconds(45)));
    }

    [Fact]
    public void Session_start_comes_from_the_addon_when_plausible()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        engine.OnPayload(World, Now);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763200000), engine.SessionStart(Now));

        var stale = new PresenceEngine();
        stale.GameStarted();
        stale.OnPayload(World, Now.AddDays(5));
        Assert.Equal(Now.AddDays(5), stale.SessionStart(Now.AddDays(5)));

        var future = new PresenceEngine();
        future.GameStarted();
        future.OnPayload(World, Now.AddHours(-2));
        Assert.Equal(Now.AddHours(-2), future.SessionStart(Now.AddHours(-2)));
    }

    [Fact]
    public void Game_exit_clears_everything()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        var party = engine.PartyId;
        engine.OnPayload(World, Now);
        engine.GameStopped();

        Assert.False(engine.GameRunning);
        Assert.Null(engine.Snapshot);
        Assert.Null(engine.SessionStart(Now));
        Assert.Null(engine.DesiredActivity(new AppSettings(), AssetCatalog.LoadDefault(), Now));

        engine.GameStarted();
        Assert.NotEqual(party, engine.PartyId);
    }

    [Fact]
    public void Pause_clears_the_activity()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        engine.OnPayload(World, Now);

        Assert.Null(engine.DesiredActivity(new AppSettings { PausePresence = true }, AssetCatalog.LoadDefault(), Now));
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\World of Warcraft\_forever_\Wow.exe", true)]
    [InlineData(@"D:\Games\World of Warcraft\_forever_ptr_\Wow.exe", true)]
    [InlineData(@"C:\Program Files (x86)\World of Warcraft\_retail_\Wow.exe", false)]
    [InlineData(@"C:\Program Files (x86)\World of Warcraft\_classic_era_\WowClassic.exe", false)]
    [InlineData(null, true)]
    public void Recognises_the_forever_client(string? path, bool expected)
    {
        Assert.Equal(expected, GameProcess.IsForeverPath(path));
    }
}

/// <summary>Addon state → strip pixels → companion state → Discord activity, end to end.</summary>
public class IntegrationTests
{
    private static Activity Run(string fixture, AppSettings? settings = null)
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        var decoded = StripDecoder.Decode(Fixture.Bgra(fixture), Fixture.Stride);
        Assert.Equal(DecodeStatus.Ok, decoded.Status);
        Assert.True(engine.OnPayload(decoded.Payload, DateTimeOffset.FromUnixTimeSeconds(1763203600)));
        return engine.DesiredActivity(settings ?? new AppSettings(), AssetCatalog.LoadDefault(), DateTimeOffset.FromUnixTimeSeconds(1763203600))!;
    }

    [Fact]
    public void World()
    {
        var a = Run("world");
        Assert.Equal("Archer · Level 60 Human Paladin", a.Details);
        Assert.Equal("Exploring Eastern Plaguelands", a.State);
        Assert.Equal("zone_eastern_plaguelands", a.Assets!.LargeImage);
        Assert.Equal("class_paladin", a.Assets.SmallImage);
        Assert.Equal(1763200000000, a.Timestamps!.Start);
    }

    [Fact]
    public void Dungeon_with_stats()
    {
        var a = Run("dungeon", new AppSettings { ShowStats = true });
        Assert.Equal("Archer · Level 58 Human Paladin", a.Details);
        Assert.Equal("Inside Stratholme", a.State);
        Assert.Equal("Dungeon: Stratholme (Normal)", a.Assets!.LargeText);
        Assert.Equal("inst_stratholme", a.Assets.LargeImage);
        Assert.Equal("Paladin · Alliance · HP 85% · Mana 40% · XP 42%", a.Assets.SmallText);
        Assert.Equal([5, 5], a.Party!.Size);
    }

    [Fact]
    public void Raid()
    {
        var a = Run("raid");
        Assert.Equal("Grommash · Level 60 Orc Warrior", a.Details);
        Assert.Equal("Inside Molten Core", a.State);
        Assert.Equal("Raid: Molten Core (40 Player)", a.Assets!.LargeText);
        Assert.Equal("class_warrior", a.Assets.SmallImage);
        Assert.Equal([25, 40], a.Party!.Size);
    }

    [Fact]
    public void Unknown_instance()
    {
        var a = Run("unknown-instance");
        Assert.Equal("Inside an instance", a.State);
        Assert.Equal("type_dungeon", a.Assets!.LargeImage);
    }

    [Fact]
    public void Battleground()
    {
        var a = Run("battleground");
        Assert.Equal("In Warsong Gulch", a.State);
        Assert.Equal("inst_warsong_gulch", a.Assets!.LargeImage);
    }

    [Fact]
    public void Name_hidden_in_the_addon_never_arrives()
    {
        var a = Run("private");
        Assert.Equal("Level 60 Human Paladin", a.Details);
        Assert.Equal("Resting in Trade District", a.State);
        Assert.Equal("zone_stormwind_city", a.Assets!.LargeImage);
    }

    [Fact]
    public void Unicode_text_and_new_zones()
    {
        var a = Run("unicode");
        Assert.Equal("Ælfwyn · Level 23 Skyborne Hunter", a.Details);
        Assert.Equal("Exploring Zephras Isle", a.State);
        Assert.Equal("Zephras Isle · Sølvhavn", a.Assets!.LargeText);
        Assert.Equal("zone_zephras_isle", a.Assets.LargeImage);
    }
}
