using RichForever.Core;

namespace RichForever.App;

/// <summary>
/// Copies the small strip region from the screen into a reusable 32-bit top-down DIB with GDI
/// BitBlt. Read-only; nothing is sent to the game. One allocation for the life of the app.
/// </summary>
internal sealed unsafe class ScreenCapture : IDisposable
{
    private readonly nint _memoryDc;
    private readonly nint _bitmap;
    private readonly nint _previous;
    private readonly byte* _bits;

    public ScreenCapture()
    {
        var header = new Native.BitmapInfoHeader
        {
            Size = (uint)sizeof(Native.BitmapInfoHeader),
            Width = StripDecoder.Width,
            Height = -StripDecoder.Height, // negative height = top-down rows
            Planes = 1,
            BitCount = 32,
        };
        _memoryDc = Native.CreateCompatibleDC(0);
        _bitmap = Native.CreateDIBSection(_memoryDc, ref header, 0, out var bits, 0, 0);
        if (_memoryDc == 0 || _bitmap == 0) throw new InvalidOperationException("Could not create the capture bitmap.");
        _bits = (byte*)bits;
        _previous = Native.SelectObject(_memoryDc, _bitmap);
    }

    public static int Stride => StripDecoder.Width * 4;

    /// <summary>Capture the strip region whose top-left is at the given physical screen position.</summary>
    public ReadOnlySpan<byte> Capture(int x, int y)
    {
        var screen = Native.GetDC(0);
        try
        {
            Native.BitBlt(_memoryDc, 0, 0, StripDecoder.Width, StripDecoder.Height, screen, x, y, Native.SrcCopy);
            Native.GdiFlush();
        }
        finally
        {
            _ = Native.ReleaseDC(0, screen);
        }
        return new ReadOnlySpan<byte>(_bits, Stride * StripDecoder.Height);
    }

    public void Dispose()
    {
        Native.SelectObject(_memoryDc, _previous);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(_memoryDc);
    }
}
