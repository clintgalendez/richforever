namespace RichForever.Core;

public enum DecodeStatus
{
    Ok,
    NoStrip,
    UnsupportedVersion,
    BadLength,
    BadChecksum,
}

public readonly record struct DecodeResult(DecodeStatus Status, byte[]? Payload = null);

/// <summary>
/// Decodes the pixel block drawn by the addon (protocol v1, see addon/RichForever/Protocol.lua).
/// Grey calibration cells in the header teach the decoder how the four colour levels look on
/// this screen, so moderate gamma or brightness changes still decode.
/// </summary>
public static class StripDecoder
{
    public const int Columns = 64;
    public const int MaxRows = 10;
    public const int CellPixels = 3;
    public const int Version = 1;
    public const int MaxPayload = 468;
    public const int Width = Columns * CellPixels;
    public const int Height = MaxRows * CellPixels;

    private const int HeaderCells = 13;
    private const int MinLevelGap = 24;
    private static readonly int[] Magic = [48, 12, 3, 63, 42, 21, 0];

    /// <summary>Decode a top-down 32-bit BGRA image whose top-left pixel is the strip's top-left.</summary>
    public static DecodeResult Decode(ReadOnlySpan<byte> bgra, int stride)
    {
        if (stride < Width * 4 || bgra.Length < stride * (Height - 1) + Width * 4)
        {
            throw new ArgumentException("Image is smaller than the strip region.", nameof(bgra));
        }

        // Cells 3..6 are white, light grey, dark grey and black: levels 3, 2, 1, 0 on every channel.
        Span<int> levels = stackalloc int[12];
        for (var level = 0; level < 4; level++)
        {
            var pixel = Sample(bgra, stride, 6 - level);
            levels[level] = pixel.R;
            levels[4 + level] = pixel.G;
            levels[8 + level] = pixel.B;
        }
        for (var channel = 0; channel < 3; channel++)
        {
            for (var level = 1; level < 4; level++)
            {
                if (levels[channel * 4 + level] - levels[channel * 4 + level - 1] < MinLevelGap)
                {
                    return new(DecodeStatus.NoStrip);
                }
            }
        }

        for (var i = 0; i < Magic.Length; i++)
        {
            if (Read(bgra, stride, i, levels) != Magic[i])
            {
                return new(DecodeStatus.NoStrip);
            }
        }

        if (Read(bgra, stride, 7, levels) != Version)
        {
            return new(DecodeStatus.UnsupportedVersion);
        }

        var length = Read(bgra, stride, 8, levels) * 64 + Read(bgra, stride, 9, levels);
        if (length == 0 || length > MaxPayload)
        {
            return new(DecodeStatus.BadLength);
        }

        var checksum = Read(bgra, stride, 10, levels) * 4096 + Read(bgra, stride, 11, levels) * 64 + Read(bgra, stride, 12, levels);
        var payload = new byte[length];
        var cell = HeaderCells;
        for (var i = 0; i < length; i += 3)
        {
            var value = Read(bgra, stride, cell, levels) << 18 | Read(bgra, stride, cell + 1, levels) << 12
                | Read(bgra, stride, cell + 2, levels) << 6 | Read(bgra, stride, cell + 3, levels);
            cell += 4;
            payload[i] = (byte)(value >> 16);
            if (i + 1 < length) payload[i + 1] = (byte)(value >> 8);
            if (i + 2 < length) payload[i + 2] = (byte)value;
        }

        return Fletcher16(payload) == checksum ? new(DecodeStatus.Ok, payload) : new(DecodeStatus.BadChecksum);
    }

    public static int Fletcher16(ReadOnlySpan<byte> data)
    {
        int a = 0, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 255;
            b = (b + a) % 255;
        }
        return b << 8 | a;
    }

    private static (int R, int G, int B) Sample(ReadOnlySpan<byte> bgra, int stride, int cell)
    {
        var x = cell % Columns * CellPixels + 1;
        var y = cell / Columns * CellPixels + 1;
        var offset = y * stride + x * 4;
        return (bgra[offset + 2], bgra[offset + 1], bgra[offset]);
    }

    private static int Read(ReadOnlySpan<byte> bgra, int stride, int cell, ReadOnlySpan<int> levels)
    {
        var (r, g, b) = Sample(bgra, stride, cell);
        return Nearest(r, levels[..4]) << 4 | Nearest(g, levels.Slice(4, 4)) << 2 | Nearest(b, levels.Slice(8, 4));
    }

    private static int Nearest(int value, ReadOnlySpan<int> levels)
    {
        var best = 0;
        for (var i = 1; i < 4; i++)
        {
            if (Math.Abs(value - levels[i]) < Math.Abs(value - levels[best])) best = i;
        }
        return best;
    }
}
