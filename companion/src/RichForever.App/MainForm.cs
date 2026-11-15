using System.Diagnostics;
using System.Globalization;
using RichForever.Core;

namespace RichForever.App;

/// <summary>Status and settings window. Created on demand and disposed when closed.</summary>
internal sealed class MainForm : Form
{
    private const string SetupGuide = "https://github.com/clintgalendez/richforever/blob/main/docs/DISCORD_SETUP.md";

    private readonly CompanionController _controller;
    private readonly Label _game = Value(), _discord = Value(), _character = Value(), _presence = Value(), _error = Value(), _performance = Value();
    private readonly TextBox _applicationId = new() { Width = 220, PlaceholderText = "RichForever (default)" };
    private readonly CheckBox _pause = Check("Pause presence"), _hideName = Check("Hide character name"), _hideTime = Check("Hide session time"),
        _hideZone = Check("Hide zone and map"), _hideInstance = Check("Hide dungeon and raid details"), _stats = Check("Show health, power and XP (also enable in the addon)"),
        _group = Check("Show group size"), _combat = Check("Show when in combat"), _startup = Check("Start RichForever when Windows starts"),
        _exit = Check("Exit RichForever when WoW Forever closes");
    private readonly RadioButton _whenGame = new() { Text = "Connect to Discord only while WoW Forever runs", AutoSize = true },
        _always = new() { Text = "Connect to Discord as soon as RichForever starts", AutoSize = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private TimeSpan _lastCpu;
    private DateTime _lastSample;

    public MainForm(CompanionController controller)
    {
        _controller = controller;
        Text = "RichForever";
        Icon = TrayApplication.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Padding = new Padding(12);

        var status = Table();
        AddRow(status, "WoW Forever", _game);
        AddRow(status, "Discord", _discord);
        AddRow(status, "Character", _character);
        AddRow(status, "Presence", _presence);
        AddRow(status, "Last error", _error);
        AddRow(status, "Performance", _performance);

        var idRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var guide = new LinkLabel { Text = "Use your own Discord app (optional)", AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
        guide.LinkClicked += (_, _) => Open(SetupGuide);
        idRow.Controls.AddRange([_applicationId, guide]);

        var settings = Table();
        AddRow(settings, "Discord app", idRow);
        AddRow(settings, "Privacy", Stack(_pause, _hideName, _hideTime, _hideZone, _hideInstance));
        AddRow(settings, "Show", Stack(_stats, _group, _combat));
        AddRow(settings, "Startup", Stack(_startup, _whenGame, _always, _exit));

        var save = new Button { Text = "Save", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true };
        var logs = new LinkLabel { Text = "Open log folder", AutoSize = true, Margin = new Padding(0, 8, 24, 0) };
        save.Click += (_, _) => Save();
        close.Click += (_, _) => Close();
        logs.LinkClicked += (_, _) => Open(AppPaths.LogDirectory);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([close, save, logs]);
        AcceptButton = save;
        CancelButton = close;

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        root.Controls.Add(Group("Status", status));
        root.Controls.Add(Group("Settings", settings));
        root.Controls.Add(buttons);
        Controls.Add(root);

        LoadSettings(controller.Settings);
        controller.Changed += RefreshStatus;
        _refresh.Tick += (_, _) => RefreshStatus();
        _refresh.Start();
        RefreshStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.Changed -= RefreshStatus;
            _refresh.Dispose();
        }
        base.Dispose(disposing);
    }

    private void LoadSettings(AppSettings s)
    {
        _applicationId.Text = s.DiscordApplicationId;
        _pause.Checked = s.PausePresence;
        _hideName.Checked = s.HideCharacterName;
        _hideTime.Checked = s.HideSessionTime;
        _hideZone.Checked = s.HideZone;
        _hideInstance.Checked = s.HideInstance;
        _stats.Checked = s.ShowStats;
        _group.Checked = s.ShowGroupSize;
        _combat.Checked = s.ShowCombat;
        _startup.Checked = s.StartWithWindows;
        _whenGame.Checked = s.DiscordMode == DiscordConnectMode.WhenGameRunning;
        _always.Checked = s.DiscordMode == DiscordConnectMode.Always;
        _exit.Checked = s.ExitWhenGameCloses;
    }

    private void Save()
    {
        var s = _controller.Settings.Clone();
        s.DiscordApplicationId = _applicationId.Text.Trim();
        if (s.DiscordApplicationId.Length > 0 && !s.HasValidApplicationId)
        {
            MessageBox.Show(this, "Leave the field empty to use RichForever's Discord application, or enter the 17-20 digit Application ID from the General Information page of your own Discord application.",
                "RichForever", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        s.PausePresence = _pause.Checked;
        s.HideCharacterName = _hideName.Checked;
        s.HideSessionTime = _hideTime.Checked;
        s.HideZone = _hideZone.Checked;
        s.HideInstance = _hideInstance.Checked;
        s.ShowStats = _stats.Checked;
        s.ShowGroupSize = _group.Checked;
        s.ShowCombat = _combat.Checked;
        s.StartWithWindows = _startup.Checked;
        s.DiscordMode = _always.Checked ? DiscordConnectMode.Always : DiscordConnectMode.WhenGameRunning;
        s.ExitWhenGameCloses = _exit.Checked;
        _controller.ApplySettings(s);
        LoadSettings(_controller.Settings);
    }

    private void RefreshStatus()
    {
        if (IsDisposed || !Visible) return;
        var c = _controller;
        _game.Text = c.GameStatus;
        _discord.Text = c.Discord.UserName is null ? c.Discord.Message : $"{c.Discord.Message} as {c.Discord.UserName}";
        _character.Text = Describe(c.Snapshot);
        _presence.Text = c.Settings.PausePresence ? "Paused" : c.CurrentActivity is null ? "Nothing shown"
            : string.Join(Environment.NewLine, new[] { c.CurrentActivity.Details, c.CurrentActivity.State, c.CurrentActivity.Assets?.LargeText, c.CurrentActivity.Assets?.SmallText }.Where(t => t is not null));
        _error.Text = c.LastError ?? "None";

        using var process = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var cpu = process.TotalProcessorTime;
        var percent = _lastSample == default ? 0 : (cpu - _lastCpu).TotalMilliseconds / (now - _lastSample).TotalMilliseconds / Environment.ProcessorCount * 100;
        _lastCpu = cpu;
        _lastSample = now;
        var average = c.Captures == 0 ? 0 : c.CaptureMilliseconds / c.Captures;
        _performance.Text = string.Create(CultureInfo.InvariantCulture,
            $"{process.PrivateMemorySize64 / 1048576.0:0.0} MB private, {process.WorkingSet64 / 1048576.0:0.0} MB working set, CPU {percent:0.00}%{Environment.NewLine}" +
            $"{c.Captures} captures ({average:0.000} ms average), {c.PresenceUpdates} presence updates");
    }

    private static string Describe(GameSnapshot? s)
    {
        if (s is null) return "No character data yet";
        var who = string.Join(" ", new[] { s.Name, s.Level is int l ? $"level {l}" : null, s.RaceName, s.ClassName ?? s.ClassToken }.Where(t => t is not null));
        var where = s.Instance == InstanceKind.None ? s.Zone : $"{s.InstanceName ?? "unknown instance"} ({s.Instance})";
        return $"{who}{Environment.NewLine}{where} (map {s.MapId?.ToString(CultureInfo.InvariantCulture) ?? "?"})";
    }

    private static void Open(string target)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Warn($"Could not open {target}: {e.Message}");
        }
    }

    private static Label Value() => new() { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 3, 3, 6) };

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };

    private static TableLayoutPanel Table() => new()
    {
        AutoSize = true,
        ColumnCount = 2,
        Dock = DockStyle.Fill,
        ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.AutoSize) },
    };

    private static void AddRow(TableLayoutPanel table, string caption, Control value)
    {
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 3, 12, 6), Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) });
        table.Controls.Add(value);
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static GroupBox Group(string text, Control content)
    {
        var box = new GroupBox { Text = text, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        box.Controls.Add(content);
        return box;
    }
}
