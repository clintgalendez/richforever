using System.Reflection;
using RichForever.Core;

namespace RichForever.App;

/// <summary>Tray icon and menu. The app has no window unless the user opens one.</summary>
internal sealed class TrayApplication : ApplicationContext
{
    public static readonly Icon AppIcon = new(typeof(TrayApplication).Assembly.GetManifestResourceStream("RichForever.App.richforever.ico")!);
    public static string Version { get; } =
        typeof(TrayApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private readonly CompanionController _controller;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _gameItem = Status(), _discordItem = Status(), _presenceItem = Status(), _pauseItem;
    private MainForm? _window;
    private Uri? _balloonLink;
    private bool _exiting;

    public TrayApplication(bool startedByWindows, bool updated)
    {
        var firstRun = !File.Exists(AppPaths.Settings);
        var settings = AppSettings.Load(AppPaths.Settings, out var warning);
        Log.Verbose = settings.VerboseLogging;
        Log.Info($"RichForever {Version} starting{(startedByWindows ? " with Windows" : updated ? " after an update" : "")}.");
        if (warning is not null) Log.Warn(warning);

        _controller = new CompanionController(settings);
        _pauseItem = new ToolStripMenuItem("Pause presence", null, (_, _) => _controller.SetPaused(!_controller.Settings.PausePresence));

        var menu = _menu;
        menu.Items.Add(new ToolStripMenuItem($"RichForever {Version}") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.AddRange([_gameItem, _discordItem, _presenceItem, new ToolStripSeparator()]);
        menu.Items.Add(new ToolStripMenuItem("Open RichForever…", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("Connect to Discord", null, (_, _) => _controller.ConnectDiscordNow()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Check for updates…", null, async (_, _) => await CheckForUpdatesAsync().ConfigureAwait(true)));
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => Shell.Open(AppPaths.LogDirectory)));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, async (_, _) => await ExitAsync()));
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { Icon = AppIcon, Text = "RichForever", ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowWindow();
        };

        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_balloonLink is not null) Shell.Open(_balloonLink.ToString());
        };

        _controller.Changed += RefreshTooltip;
        _controller.ExitRequested += async () => await ExitAsync();
        _controller.RestartRequested += async () =>
        {
            // An install that finishes while the user exits must not start the app again.
            if (_exiting) return;
            RestartRequested = true;
            await ExitAsync().ConfigureAwait(true);
        };
        _controller.UpdateFailed += update => ShowBalloon($"RichForever {update.Version} is available but could not be installed. Click to download it.", update.Page);
        _controller.Start();
        if (updated) ShowBalloon($"Updated to RichForever {Version}.", null);

        if (firstRun)
        {
            // Show the window once so new users see the status and settings; nothing is required.
            try
            {
                settings.Save(AppPaths.Settings);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not save settings: {e.Message}");
            }
            if (!startedByWindows) ShowWindow();
        }
    }

    /// <summary>Set when the app exits to start a newly installed version.</summary>
    public bool RestartRequested { get; private set; }

    private async Task CheckForUpdatesAsync()
    {
        AvailableUpdate? offered = null;
        var accepted = false;
        var installed = await _controller.UpdateAsync(update =>
        {
            offered = update;
            var question = $"RichForever {update.Version} is available (you have {Version}).{Environment.NewLine}{Environment.NewLine}Install it and restart RichForever now?";
            return accepted = MessageBox.Show(question, "RichForever", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }, restartNow: true).ConfigureAwait(true);
        if (installed || (offered is not null && !accepted)) return;
        if (offered is null)
        {
            // Up to date, check failed, busy, or already installed: the status says which.
            MessageBox.Show(_controller.UpdateStatus, "RichForever");
            return;
        }
        var failed = $"{_controller.UpdateStatus}{Environment.NewLine}{Environment.NewLine}Open the download page?";
        if (MessageBox.Show(failed, "RichForever", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) Shell.Open(offered.Page.ToString());
    }

    private void ShowBalloon(string text, Uri? link)
    {
        _balloonLink = link;
        _tray.ShowBalloonTip(5000, "RichForever", text, ToolTipIcon.Info);
    }

    private void ShowWindow()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new MainForm(_controller);
            _window.FormClosed += (_, _) => _window = null;
        }
        _window.Show();
        _window.Activate();
    }

    private void RefreshMenu()
    {
        _gameItem.Text = $"WoW: {_controller.GameStatus}";
        var discord = _controller.Discord;
        _discordItem.Text = $"Discord: {discord.Message}";
        var activity = _controller.CurrentActivity;
        _presenceItem.Text = _controller.Settings.PausePresence ? "Presence: paused"
            : activity is null ? "Presence: nothing shown" : $"Presence: {activity.Details} - {activity.State ?? ""}".TrimEnd(' ', '-');
        _pauseItem.Checked = _controller.Settings.PausePresence;
    }

    private void RefreshTooltip()
    {
        var text = _controller.Settings.PausePresence ? "RichForever (paused)"
            : _controller.Snapshot is not null ? $"RichForever - {_controller.CurrentActivity?.State ?? "playing"}"
            : _controller.GameRunning ? "RichForever - WoW Forever running" : "RichForever - waiting for WoW Forever";
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Exiting.");
        _window?.Close();
        _tray.Visible = false;
        await _controller.DisposeAsync();
        _tray.Dispose();
        _menu.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            _tray.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }

    private static ToolStripMenuItem Status() => new() { Enabled = false };
}
