namespace RichForever.Core;

/// <summary>
/// Game-side state: whether WoW Forever runs, the latest valid snapshot, and the session start.
/// Pure logic with explicit timestamps so it can be tested without a clock or a game.
/// </summary>
public sealed class PresenceEngine
{
    /// <summary>How long the strip may be unreadable while visible before the character is considered gone.</summary>
    public static readonly TimeSpan MissingStripTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan MaxSessionAge = TimeSpan.FromDays(3);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private byte[]? _lastPayload;
    private DateTimeOffset? _firstFrameAt;
    private DateTimeOffset? _missingSince;
    private string? _instanceKey;
    private DateTimeOffset? _instanceSince;

    public bool GameRunning { get; private set; }
    public GameSnapshot? Snapshot { get; private set; }
    public DateTimeOffset? LastFrameAt { get; private set; }
    public string? LastPayloadError { get; private set; }
    public string PartyId { get; private set; } = NewPartyId();

    public void GameStarted()
    {
        Reset();
        GameRunning = true;
        PartyId = NewPartyId();
    }

    public void GameStopped()
    {
        Reset();
        GameRunning = false;
    }

    /// <summary>Feed a decoded payload. Returns true when the snapshot changed.</summary>
    public bool OnPayload(ReadOnlySpan<byte> payload, DateTimeOffset now)
    {
        LastFrameAt = now;
        _missingSince = null;
        if (_lastPayload is not null && payload.SequenceEqual(_lastPayload)) return false;

        var snapshot = PayloadParser.Parse(payload, out var error);
        if (snapshot is null)
        {
            LastPayloadError = error;
            return false;
        }
        _lastPayload = payload.ToArray();
        LastPayloadError = null;
        _firstFrameAt ??= now;
        var instanceKey = snapshot.Instance == InstanceKind.None ? null : $"{snapshot.Instance}|{snapshot.InstanceId}|{snapshot.InstanceName}";
        if (instanceKey != _instanceKey)
        {
            _instanceKey = instanceKey;
            _instanceSince = instanceKey is null ? null : now;
        }
        var changed = snapshot != Snapshot;
        Snapshot = snapshot;
        return changed;
    }

    /// <summary>The strip area was visible but held no valid strip. Returns true when the snapshot was dropped.</summary>
    public bool OnStripMissing(DateTimeOffset now)
    {
        _missingSince ??= now;
        if (Snapshot is null || now - _missingSince < MissingStripTimeout) return false;
        Snapshot = null;
        _lastPayload = null;
        return true;
    }

    /// <summary>Session start reported by the addon if plausible, else the first valid frame.</summary>
    public DateTimeOffset? SessionStart(DateTimeOffset now)
    {
        var reported = Snapshot?.SessionStart;
        if (reported is DateTimeOffset start && start <= now + ClockSkew && now - start <= MaxSessionAge) return start;
        return _firstFrameAt;
    }

    /// <summary>When the elapsed timer starts: the current instance run if chosen, else the session.</summary>
    public DateTimeOffset? TimerStart(AppSettings settings, DateTimeOffset now) =>
        settings.TimeInstanceRuns && Snapshot is { Instance: not InstanceKind.None } && _instanceSince is DateTimeOffset since ? since : SessionStart(now);

    public bool WantsDiscord(AppSettings settings) => GameRunning || settings.DiscordMode == DiscordConnectMode.Always;

    /// <summary>The activity to show, or null to clear it.</summary>
    public Activity? DesiredActivity(AppSettings settings, AssetCatalog catalog, DateTimeOffset now) =>
        !GameRunning || settings.PausePresence || (settings.HideWhenAway && Snapshot?.IsAway == true) ? null
            : PresenceBuilder.Build(Snapshot, settings, catalog, TimerStart(settings, now), PartyId);

    private void Reset()
    {
        Snapshot = null;
        LastFrameAt = null;
        LastPayloadError = null;
        _lastPayload = null;
        _firstFrameAt = null;
        _missingSince = null;
        _instanceKey = null;
        _instanceSince = null;
    }

    private static string NewPartyId() => "rf-" + Guid.NewGuid().ToString("N");
}

public static class GameProcess
{
    /// <summary>
    /// True when the executable lives in a WoW Forever folder (_forever_, _forever_ptr_, ...).
    /// An unreadable path is accepted because the process name already matched.
    /// </summary>
    public static bool IsForeverPath(string? path) =>
        path is null
        || path.Contains("\\_forever", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/_forever", StringComparison.OrdinalIgnoreCase);
}
