using System.Drawing.Imaging;
using RichForever.App;
using RichForever.Core;
using Native = RichForever.App.Native;

namespace RichForever.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "richforever-tests-" + Guid.NewGuid().ToString("N"));

    private string File(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = AppSettings.Load(File("none.json"), out var warning);

        Assert.Null(warning);
        Assert.Equal(DiscordConnectMode.WhenGameRunning, settings.DiscordMode);
        Assert.True(settings.ShowGroupSize);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(["Wow", "WowB"], settings.GameProcessNames);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var path = File("settings.json");
        new AppSettings { DiscordApplicationId = "123456789012345678", HideZone = true, DiscordMode = DiscordConnectMode.Always, Buttons = [new("Guild", "https://example.com")] }.Save(path);

        var loaded = AppSettings.Load(path, out _);

        Assert.Equal("123456789012345678", loaded.DiscordApplicationId);
        Assert.True(loaded.HasValidApplicationId);
        Assert.True(loaded.HideZone);
        Assert.Equal(DiscordConnectMode.Always, loaded.DiscordMode);
        Assert.Single(loaded.Buttons);
        Assert.Contains("\"Always\"", System.IO.File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_a_warning()
    {
        Directory.CreateDirectory(_directory);
        System.IO.File.WriteAllText(File("bad.json"), "{ this is not json");

        var settings = AppSettings.Load(File("bad.json"), out var warning);

        Assert.NotNull(warning);
        Assert.False(settings.PausePresence);
    }

    [Fact]
    public void Values_are_normalized()
    {
        Directory.CreateDirectory(_directory);
        System.IO.File.WriteAllText(File("odd.json"), """
            {
              // comments are fine
              "DiscordApplicationId": "  123456789012345678  ",
              "GamePollSeconds": 0,
              "CaptureIntervalMilliseconds": 99999,
              "DiscordMode": 7,
              "GameProcessNames": ["Wow", "wow", "bad name!", ""],
              "Buttons": [ { "Label": "x", "Url": "javascript:alert(1)" } ],
            }
            """);

        var s = AppSettings.Load(File("odd.json"), out var warning);

        Assert.Null(warning);
        Assert.Equal("123456789012345678", s.DiscordApplicationId);
        Assert.Equal(2, s.GamePollSeconds);
        Assert.Equal(5000, s.CaptureIntervalMilliseconds);
        Assert.Equal(DiscordConnectMode.WhenGameRunning, s.DiscordMode);
        Assert.Equal(["Wow"], s.GameProcessNames);
        Assert.Empty(s.Buttons);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("123", false)]
    [InlineData("12345678901234567a", false)]
    [InlineData("123456789012345678", true)]
    public void Application_id_format(string id, bool valid)
    {
        Assert.Equal(valid, new AppSettings { DiscordApplicationId = id }.HasValidApplicationId);
    }
}

public class StartupRegistrationTests : IDisposable
{
    private readonly string _key = @"Software\RichForeverTests\" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Enable_and_disable_are_reversible()
    {
        Assert.Null(StartupRegistration.Current(_key));

        StartupRegistration.Apply(true, @"C:\Apps\RichForever\RichForever.exe", _key);
        Assert.Equal("\"C:\\Apps\\RichForever\\RichForever.exe\" --startup", StartupRegistration.Current(_key));

        StartupRegistration.Apply(true, @"D:\Moved\RichForever.exe", _key);
        Assert.Equal("\"D:\\Moved\\RichForever.exe\" --startup", StartupRegistration.Current(_key));

        StartupRegistration.Apply(false, "", _key);
        Assert.Null(StartupRegistration.Current(_key));
        StartupRegistration.Apply(false, "", _key);
    }
}

public class GameWatcherTests
{
    private static readonly string Self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

    [Fact]
    public void Finds_a_running_process_with_a_visible_window()
    {
        bool attached = false, exitedAfterDetach = false;
        int? processId = null;
        var thread = new Thread(() =>
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
            form.Show();
            form.Hide(); // the test host starts hidden; see ScreenCaptureTests
            form.Show();
            Application.DoEvents();

            using var watcher = new GameWatcher();
            attached = watcher.TryAttach(new AppSettings { GameProcessNames = ["NotARealGame", Self.ToUpperInvariant()], RequireForeverFolder = false });
            processId = watcher.ProcessId;
            watcher.Detach();
            exitedAfterDetach = watcher.HasExited;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(attached);
        Assert.Equal(Environment.ProcessId, processId);
        Assert.True(exitedAfterDetach);
    }

    [Fact]
    public void Ignores_processes_outside_a_forever_folder_and_unknown_names()
    {
        using var watcher = new GameWatcher();

        Assert.False(watcher.TryAttach(new AppSettings { GameProcessNames = [Self], RequireForeverFolder = true }));
        Assert.False(watcher.TryAttach(new AppSettings { GameProcessNames = ["NotARealGame"], RequireForeverFolder = false }));
    }
}

/// <summary>Draws a real strip on screen and reads it back through the production GDI capture path.</summary>
public class ScreenCaptureTests
{
    [Fact]
    public void Captures_and_decodes_a_strip_drawn_on_screen()
    {
        var result = new DecodeResult(DecodeStatus.NoStrip);
        var diagnostics = "";
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var bitmap = new Bitmap(StripDecoder.Width, StripDecoder.Height, PixelFormat.Format32bppArgb);
            var pixels = Fixture.Bgra("dungeon");
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (var row = 0; row < bitmap.Height; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels, row * Fixture.Stride, data.Scan0 + row * data.Stride, Fixture.Stride);
            }
            bitmap.UnlockBits(data);

            using var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(40, 40),
                TopMost = true,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.None,
            };
            form.Paint += (_, e) => e.Graphics.DrawImage(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            // A process started hidden (like the test host) may have its first ShowWindow call
            // replaced by the startup show state, so keep re-showing until the window is on top.
            form.Show();
            form.ClientSize = bitmap.Size;
            var origin = form.PointToScreen(Point.Empty);
            using var capture = new ScreenCapture();
            for (var attempt = 0; attempt < 40 && result.Status != DecodeStatus.Ok; attempt++)
            {
                var hit = Native.GetAncestor(Native.WindowFromPoint(new Native.Point { X = origin.X + 1, Y = origin.Y + 1 }), Native.GaRoot);
                if (hit != form.Handle)
                {
                    form.Hide();
                    form.Show();
                    form.TopMost = false;
                    form.TopMost = true;
                }
                form.Refresh();
                Application.DoEvents();
                Thread.Sleep(50);
                result = StripDecoder.Decode(capture.Capture(origin.X, origin.Y), ScreenCapture.Stride);
                diagnostics = $"attempt {attempt}: window at strip {hit}, form {form.Handle}, bounds {form.Bounds}, dpi {form.DeviceDpi}";
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(result.Status == DecodeStatus.Ok, $"{result.Status}; {diagnostics}");
        Assert.Equal(Fixture.Payload("dungeon"), System.Text.Encoding.UTF8.GetString(result.Payload!));
    }
}
