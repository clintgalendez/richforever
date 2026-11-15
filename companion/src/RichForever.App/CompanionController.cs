using System.Text.Json;
using RichForever.Core;
using FormsTimer = System.Windows.Forms.Timer;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace RichForever.App;

/// <summary>
/// Application lifecycle. Waiting mode: one cheap process lookup every few seconds, no Discord
/// connection, no capture. Game mode: one strip capture per second while the game window is
/// visible; Discord updates only when the presence changes. Runs on the UI thread.
/// </summary>
internal sealed class CompanionController : IAsyncDisposable
{
    private readonly FormsTimer _pollTimer = new();
    private readonly FormsTimer _captureTimer = new();
    private readonly GameWatcher _game = new();
    private readonly ScreenCapture _capture = new();
    private readonly PresenceEngine _engine = new();
    private readonly DiscordPresenceSync _discord = new();
    private readonly SynchronizationContext _ui;
    private bool _connectNow;
    private DecodeStatus? _lastDecode;
    private bool _stripVisible;

    public CompanionController(AppSettings settings)
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the controller on the UI thread.");
        Settings = settings;
        Catalog = LoadCatalog();
        _pollTimer.Tick += (_, _) => PollGame();
        _captureTimer.Tick += (_, _) => CaptureTick();
        _discord.StateChanged += state => _ui.Post(_ =>
        {
            Log.Info($"Discord: {state.Status} - {state.Message}");
            Changed?.Invoke();
        }, null);
    }

    public AppSettings Settings { get; private set; }
    public AssetCatalog Catalog { get; private set; }
    public GameSnapshot? Snapshot => _engine.Snapshot;
    public bool GameRunning => _engine.GameRunning;
    public int? GameProcessId => _game.ProcessId;
    public DiscordState Discord => _discord.State;
    public Activity? CurrentActivity { get; private set; }
    public string? LastError { get; private set; }
    public string GameStatus => !_engine.GameRunning ? "Waiting for WoW Forever"
        : _engine.Snapshot is not null ? $"Running (PID {_game.ProcessId}) - receiving data"
        : !_stripVisible ? $"Running (PID {_game.ProcessId}) - game window hidden or covered"
        : _lastDecode is DecodeStatus.NoStrip or null ? $"Running (PID {_game.ProcessId}) - no RichForever strip (log in, enable the addon, keep the top-left corner uncovered)"
        : $"Running (PID {_game.ProcessId}) - strip unreadable ({_lastDecode})";

    // Diagnostics
    public long Captures { get; private set; }
    public double CaptureMilliseconds { get; private set; }
    public int PresenceUpdates => _discord.UpdatesSent;

    public event Action? Changed;
    public event Action? ExitRequested;

    public void Start()
    {
        _discord.Start();
        ApplySettings(Settings, save: false);
        PollGame();
        if (!_engine.GameRunning) _pollTimer.Start();
    }

    public void ApplySettings(AppSettings settings, bool save = true)
    {
        Settings = settings.Normalize();
        Log.Verbose = Settings.VerboseLogging;
        _pollTimer.Interval = Settings.GamePollSeconds * 1000;
        _captureTimer.Interval = Settings.CaptureIntervalMilliseconds;
        if (save)
        {
            try
            {
                Settings.Save(AppPaths.Settings);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Fail($"Could not save settings: {e.Message}");
            }
        }
        try
        {
            if (Environment.ProcessPath is string exe && (Settings.StartWithWindows || StartupRegistration.Current() is not null))
            {
                StartupRegistration.Apply(Settings.StartWithWindows, exe);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Fail($"Could not update Windows startup: {e.Message}");
        }
        Push();
    }

    public void SetPaused(bool paused)
    {
        var copy = Settings.Clone();
        copy.PausePresence = paused;
        ApplySettings(copy);
    }

    /// <summary>"Connect to Discord" from the tray: connect even if the game is not running.</summary>
    public void ConnectDiscordNow()
    {
        _connectNow = true;
        _discord.Reconnect();
        Push();
    }

    public async ValueTask DisposeAsync()
    {
        _pollTimer.Dispose();
        _captureTimer.Dispose();
        await _discord.DisposeAsync().ConfigureAwait(true);
        _capture.Dispose();
        _game.Dispose();
    }

    private void PollGame()
    {
        if (!_game.TryAttach(Settings)) return;
        _pollTimer.Stop();
        _engine.GameStarted();
        _lastDecode = null;
        Log.Info($"WoW Forever detected (PID {_game.ProcessId}).");
        _captureTimer.Start();
        Push();
    }

    private void CaptureTick()
    {
        if (_game.HasExited)
        {
            GameExited();
            return;
        }
        _stripVisible = _game.TryGetStripOrigin(out var x, out var y);
        if (!_stripVisible) return;

        var started = Stopwatch.GetTimestamp();
        var result = StripDecoder.Decode(_capture.Capture(x, y), ScreenCapture.Stride);
        Captures++;
        CaptureMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var now = DateTimeOffset.UtcNow;
        var changed = result.Status == DecodeStatus.Ok ? _engine.OnPayload(result.Payload, now) : _engine.OnStripMissing(now);
        if (result.Status != _lastDecode)
        {
            Log.Debug($"Strip: {result.Status}");
            _lastDecode = result.Status;
            Changed?.Invoke();
        }
        if (_engine.LastPayloadError is string error && LastError != $"Addon data rejected: {error}") Fail($"Addon data rejected: {error}");
        if (changed) Push();
    }

    private void GameExited()
    {
        Log.Info("WoW Forever closed.");
        _captureTimer.Stop();
        _game.Detach();
        _engine.GameStopped();
        _stripVisible = false;
        Push();
        if (Settings.ExitWhenGameCloses)
        {
            ExitRequested?.Invoke();
            return;
        }
        _pollTimer.Start();
    }

    private void Push()
    {
        var activity = _engine.DesiredActivity(Settings, Catalog, DateTimeOffset.UtcNow);
        if (activity?.ToJson() != CurrentActivity?.ToJson()) Log.Debug($"Presence: {activity?.ToJson() ?? "cleared"}");
        CurrentActivity = activity;
        var appId = Settings.HasValidApplicationId ? Settings.DiscordApplicationId : null;
        _discord.Update(appId, _connectNow || _engine.WantsDiscord(Settings), CurrentActivity);
        Changed?.Invoke();
    }

    private void Fail(string message)
    {
        LastError = message;
        Log.Warn(message);
        Changed?.Invoke();
    }

    private static AssetCatalog LoadCatalog()
    {
        if (!File.Exists(AppPaths.AssetOverride)) return AssetCatalog.LoadDefault();
        try
        {
            var catalog = AssetCatalog.Parse(File.ReadAllText(AppPaths.AssetOverride));
            Log.Info($"Using asset catalog {AppPaths.AssetOverride} ({catalog.Count} entries).");
            return catalog;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Ignoring {AppPaths.AssetOverride}: {e.Message}");
            return AssetCatalog.LoadDefault();
        }
    }
}
