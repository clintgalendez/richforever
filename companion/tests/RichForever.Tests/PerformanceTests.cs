using System.Diagnostics;
using System.Text;
using RichForever.App;
using RichForever.Core;
using Xunit.Abstractions;

namespace RichForever.Tests;

/// <summary>
/// Coarse guards against accidental slowdowns of the per-second work. Bounds are generous so
/// shared CI runners do not flake; the measured values are printed for docs/PERFORMANCE.md.
/// </summary>
[Collection(DesktopTests.Name)]
public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Decode_parse_and_build_take_well_under_a_millisecond()
    {
        var pixels = Fixture.Bgra("dungeon");
        var catalog = AssetCatalog.LoadDefault();
        var settings = new AppSettings { ShowStats = true };
        const int runs = 2000;

        for (var i = 0; i < 200; i++) Pipeline();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < runs; i++) Pipeline();
        var average = Stopwatch.GetElapsedTime(started).TotalMilliseconds / runs;

        output.WriteLine($"decode + parse + presence build: {average:0.0000} ms average over {runs} runs");
        Assert.True(average < 1, $"{average} ms");

        void Pipeline()
        {
            var decoded = StripDecoder.Decode(pixels, Fixture.Stride);
            var snapshot = PayloadParser.Parse(decoded.Payload, out _);
            _ = PresenceBuilder.Build(snapshot, settings, catalog, DateTimeOffset.UnixEpoch, "p").ToJson();
        }
    }

    [Fact]
    public void Screen_capture_of_the_strip_region_is_cheap()
    {
        using var capture = new ScreenCapture();
        const int runs = 200;
        for (var i = 0; i < 20; i++) capture.Capture(0, 0);

        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < runs; i++) capture.Capture(0, 0);
        var average = Stopwatch.GetElapsedTime(started).TotalMilliseconds / runs;

        output.WriteLine($"GDI capture of {StripDecoder.Width}x{StripDecoder.Height}: {average:0.000} ms average over {runs} runs");
        Assert.True(average < 20, $"{average} ms");
    }

    [Fact]
    public void Unchanged_payloads_are_not_parsed_again()
    {
        var engine = new PresenceEngine();
        engine.GameStarted();
        var payload = Encoding.UTF8.GetBytes(Fixture.Payload("world"));
        engine.OnPayload(payload, DateTimeOffset.UnixEpoch);

        var changes = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (engine.OnPayload(payload, DateTimeOffset.UnixEpoch.AddSeconds(i))) changes++;
        }

        Assert.Equal(0, changes);
    }
}
