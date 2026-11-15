using System.Text;
using RichForever.Core;

namespace RichForever.Tests;

public class StripDecoderTests
{
    public static TheoryData<string> Fixtures => new() { "world", "dungeon", "raid", "unknown-instance", "battleground", "private", "unicode" };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decodes_addon_fixture(string name)
    {
        var result = StripDecoder.Decode(Fixture.Bgra(name), Fixture.Stride);

        Assert.Equal(DecodeStatus.Ok, result.Status);
        Assert.Equal(Fixture.Payload(name), Encoding.UTF8.GetString(result.Payload!));
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(1.6)]
    public void Calibration_cells_absorb_gamma_changes(double gamma)
    {
        var pixels = Fixture.Bgra("dungeon");
        for (var i = 0; i < pixels.Length; i++)
        {
            if (i % 4 != 3) pixels[i] = (byte)Math.Round(255 * Math.Pow(pixels[i] / 255.0, gamma));
        }

        Assert.Equal(DecodeStatus.Ok, StripDecoder.Decode(pixels, Fixture.Stride).Status);
    }

    [Fact]
    public void Small_colour_noise_still_decodes()
    {
        var pixels = Fixture.Bgra("world");
        var random = new Random(7);
        for (var i = 0; i < pixels.Length; i++)
        {
            if (i % 4 != 3) pixels[i] = (byte)Math.Clamp(pixels[i] + random.Next(-15, 16), 0, 255);
        }

        Assert.Equal(DecodeStatus.Ok, StripDecoder.Decode(pixels, Fixture.Stride).Status);
    }

    [Fact]
    public void Blank_screen_is_not_a_strip()
    {
        var pixels = new byte[Fixture.Stride * StripDecoder.Height];
        Assert.Equal(DecodeStatus.NoStrip, StripDecoder.Decode(pixels, Fixture.Stride).Status);
        Array.Fill(pixels, (byte)200);
        Assert.Equal(DecodeStatus.NoStrip, StripDecoder.Decode(pixels, Fixture.Stride).Status);
    }

    [Fact]
    public void Corrupted_payload_cell_fails_checksum()
    {
        var pixels = Fixture.Bgra("world");
        PaintCell(pixels, 20, 255, 255, 255);
        PaintCell(pixels, 21, 0, 0, 0);

        Assert.Equal(DecodeStatus.BadChecksum, StripDecoder.Decode(pixels, Fixture.Stride).Status);
    }

    [Fact]
    public void Unknown_protocol_version_is_reported()
    {
        var pixels = Fixture.Bgra("world");
        PaintCell(pixels, 7, 0, 0, 170); // version 2

        Assert.Equal(DecodeStatus.UnsupportedVersion, StripDecoder.Decode(pixels, Fixture.Stride).Status);
    }

    [Fact]
    public void Oversized_length_is_rejected()
    {
        var pixels = Fixture.Bgra("world");
        PaintCell(pixels, 8, 255, 255, 255); // 63 * 64 + low cell > max payload

        Assert.Equal(DecodeStatus.BadLength, StripDecoder.Decode(pixels, Fixture.Stride).Status);
    }

    [Fact]
    public void Too_small_image_throws()
    {
        Assert.Throws<ArgumentException>(() => StripDecoder.Decode(new byte[100], Fixture.Stride));
    }

    [Fact]
    public void Fletcher16_matches_reference_values()
    {
        Assert.Equal(0xC8F0, StripDecoder.Fletcher16("abcde"u8));
        Assert.Equal(0x2057, StripDecoder.Fletcher16("abcdef"u8));
    }

    private static void PaintCell(byte[] pixels, int cell, byte r, byte g, byte b)
    {
        var x0 = cell % StripDecoder.Columns * StripDecoder.CellPixels;
        var y0 = cell / StripDecoder.Columns * StripDecoder.CellPixels;
        for (var y = y0; y < y0 + StripDecoder.CellPixels; y++)
        {
            for (var x = x0; x < x0 + StripDecoder.CellPixels; x++)
            {
                var o = y * Fixture.Stride + x * 4;
                (pixels[o], pixels[o + 1], pixels[o + 2]) = (b, g, r);
            }
        }
    }
}

public class PayloadParserTests
{
    private static GameSnapshot? Parse(string text, out string? error) => PayloadParser.Parse(Encoding.UTF8.GetBytes(text), out error);

    [Fact]
    public void Parses_all_fields()
    {
        var s = Parse(Fixture.Payload("dungeon"), out var error)!;

        Assert.Null(error);
        Assert.Equal("Archer", s.Name);
        Assert.Equal(58, s.Level);
        Assert.Equal("PALADIN", s.ClassToken);
        Assert.Equal("Paladin", s.ClassName);
        Assert.Equal("Human", s.RaceName);
        Assert.Equal("Alliance", s.Faction);
        Assert.Equal(2, s.Sex);
        Assert.Equal("Stratholme", s.Zone);
        Assert.Equal(1423, s.MapId);
        Assert.Equal(InstanceKind.Dungeon, s.Instance);
        Assert.Equal("Stratholme", s.InstanceName);
        Assert.Equal(329, s.InstanceId);
        Assert.Equal("Normal", s.Difficulty);
        Assert.Equal(5, s.MaxPlayers);
        Assert.Equal(5, s.GroupSize);
        Assert.Equal(85, s.HealthPercent);
        Assert.Equal(40, s.PowerPercent);
        Assert.Equal("MANA", s.PowerToken);
        Assert.Equal(42, s.XpPercent);
        Assert.True(s.InCombat);
        Assert.False(s.IsDead);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763200000), s.SessionStart);
    }

    [Theory]
    [InlineData("n=Archer", "unsupported payload version")]
    [InlineData("v=2\nn=Archer", "unsupported payload version")]
    [InlineData("v=1\nnonsense", "malformed line")]
    [InlineData("v=1\n=x", "malformed line")]
    [InlineData("v=1\nn=A\nn=B", "duplicate key")]
    [InlineData("v=1\nn=Arch\ter", "control character in value")]
    [InlineData("v=1\n", "malformed line")]
    public void Rejects_malformed_payloads(string text, string expected)
    {
        Assert.Null(Parse(text, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Rejects_invalid_utf8_and_bad_sizes()
    {
        Assert.Null(PayloadParser.Parse([(byte)'v', (byte)'=', (byte)'1', 0xC3, 0x28], out var error));
        Assert.Equal("payload is not valid UTF-8", error);
        Assert.Null(PayloadParser.Parse([], out _));
        Assert.Null(PayloadParser.Parse(new byte[StripDecoder.MaxPayload + 1], out _));
    }

    [Fact]
    public void Drops_invalid_fields_but_keeps_the_rest()
    {
        var s = Parse("v=1\nn=Archer\nl=0\nc=PAL ADIN\nf=Scourge\nhp=150\nm=abc\nss=12\nit=delve\nz= padded\nfuture=field", out var error)!;

        Assert.Null(error);
        Assert.Equal("Archer", s.Name);
        Assert.Null(s.Level);
        Assert.Null(s.ClassToken);
        Assert.Null(s.Faction);
        Assert.Null(s.HealthPercent);
        Assert.Null(s.MapId);
        Assert.Null(s.SessionStart);
        Assert.Null(s.Zone);
        Assert.Equal(InstanceKind.Unknown, s.Instance);
    }

    [Fact]
    public void Missing_instance_type_means_open_world()
    {
        Assert.Equal(InstanceKind.None, Parse("v=1", out _)!.Instance);
    }

    [Fact]
    public void Equal_payloads_give_equal_snapshots()
    {
        Assert.Equal(Parse(Fixture.Payload("raid"), out _), Parse(Fixture.Payload("raid"), out _));
    }
}
