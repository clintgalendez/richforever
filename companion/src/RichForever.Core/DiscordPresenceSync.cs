using System.Threading.Channels;

namespace RichForever.Core;

public enum DiscordStatus
{
    Off,
    Connecting,
    Connected,
    Error,
}

public sealed record DiscordState(DiscordStatus Status, string Message, string? UserName = null);

/// <summary>
/// Keeps Discord in line with the desired presence. Callers push the desired state with
/// <see cref="Update"/>; a single background loop connects, sends only when the activity changes,
/// respects Discord's rate limit (5 updates per 20 s), reconnects with backoff and clears the
/// activity on shutdown. It sleeps until something changes: no polling.
/// </summary>
public sealed class DiscordPresenceSync : IAsyncDisposable
{
    public delegate Task<DiscordIpcClient> Connector(string clientId, CancellationToken cancellationToken);

    private readonly Connector _connect;
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private (string? ClientId, bool Connect, Activity? Activity) _desired;
    private DiscordIpcClient? _client;
    private string? _clientId;
    private string? _sentJson;
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private Task _loop = Task.CompletedTask;

    public DiscordPresenceSync(Connector? connect = null, TimeSpan? minInterval = null, TimeSpan? initialBackoff = null, TimeSpan? maxBackoff = null)
    {
        _connect = connect ?? ((id, ct) => DiscordIpcClient.ConnectAsync(id, ct));
        _minInterval = minInterval ?? TimeSpan.FromSeconds(5);
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(5);
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(60);
    }

    public DiscordState State { get; private set; } = new(DiscordStatus.Off, "Not connected");
    public int UpdatesSent { get; private set; }

    /// <summary>Raised on the background loop; marshal to the UI thread before touching controls.</summary>
    public event Action<DiscordState>? StateChanged;

    public void Start() => _loop = Task.Run(RunAsync, CancellationToken.None);

    public void Update(string? clientId, bool connect, Activity? activity)
    {
        lock (_lock) _desired = (clientId, connect, activity);
        _signal.Writer.TryWrite(true);
    }

    /// <summary>Drop the connection and connect again now (used by "Reconnect" in the tray).</summary>
    public void Reconnect()
    {
        lock (_lock)
        {
            _clientId = "";
            _retryAt = DateTimeOffset.MinValue;
        }
        _signal.Writer.TryWrite(true);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        var backoff = _initialBackoff;
        try
        {
            while (!token.IsCancellationRequested)
            {
                _signal.Reader.TryRead(out _);
                (string? ClientId, bool Connect, Activity? Activity) desired;
                string? currentId;
                DateTimeOffset retryAt;
                lock (_lock)
                {
                    desired = _desired;
                    currentId = _clientId;
                    retryAt = _retryAt;
                }

                if (!desired.Connect || string.IsNullOrEmpty(desired.ClientId))
                {
                    await DisconnectAsync(clear: true).ConfigureAwait(false);
                    SetState(new(DiscordStatus.Off, string.IsNullOrEmpty(desired.ClientId) && desired.Connect ? "The Discord application ID in settings is not valid" : "Not connected"));
                    await WaitAsync(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    continue;
                }

                if (_client is not null && currentId != desired.ClientId)
                {
                    await DisconnectAsync(clear: true).ConfigureAwait(false);
                }

                if (_client is null)
                {
                    // Presence changes wake the loop, but never cut a backoff short.
                    var pause = retryAt - DateTimeOffset.UtcNow;
                    if (pause > TimeSpan.Zero)
                    {
                        await WaitAsync(pause, token).ConfigureAwait(false);
                        continue;
                    }
                    SetState(new(DiscordStatus.Connecting, "Connecting to Discord…"));
                    try
                    {
                        var client = await _connect(desired.ClientId!, token).ConfigureAwait(false);
                        lock (_lock)
                        {
                            _client = client;
                            _clientId = desired.ClientId;
                        }
                        _sentJson = null;
                        backoff = _initialBackoff;
                        SetState(new(DiscordStatus.Connected, "Connected", client.UserName));
                    }
                    catch (Exception e) when (!token.IsCancellationRequested)
                    {
                        SetState(new(DiscordStatus.Error, $"{Describe(e)} Retrying in {backoff.TotalSeconds:0} s."));
                        backoff = RetryLater(backoff);
                        continue;
                    }
                }

                var json = desired.Activity?.ToJson() ?? "null";
                if (json != _sentJson)
                {
                    var wait = _lastSent + _minInterval - DateTimeOffset.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await WaitAsync(wait, token, _client.Closed).ConfigureAwait(false);
                        continue;
                    }
                    try
                    {
                        await _client.SetActivityAsync(desired.Activity, token).ConfigureAwait(false);
                        _sentJson = json;
                        _lastSent = DateTimeOffset.UtcNow;
                        UpdatesSent++;
                        if (State.Status != DiscordStatus.Connected) SetState(new(DiscordStatus.Connected, "Connected", _client.UserName));
                    }
                    catch (DiscordRpcException e) when (!_client.Closed.IsCompleted)
                    {
                        // Discord rejected this activity but the connection is fine; don't resend it.
                        _sentJson = json;
                        SetState(new(DiscordStatus.Error, $"Discord rejected the presence: {e.Message}", _client.UserName));
                    }
                    catch (Exception e) when (!token.IsCancellationRequested)
                    {
                        await DisconnectAsync(clear: false).ConfigureAwait(false);
                        SetState(new(DiscordStatus.Error, $"Lost connection to Discord: {Describe(e)} Retrying in {backoff.TotalSeconds:0} s."));
                        backoff = RetryLater(backoff);
                        continue;
                    }
                }

                var closed = _client.Closed;
                await WaitAsync(Timeout.InfiniteTimeSpan, token, closed).ConfigureAwait(false);
                if (closed.IsCompleted)
                {
                    var reason = await closed.ConfigureAwait(false);
                    await DisconnectAsync(clear: false).ConfigureAwait(false);
                    SetState(new(DiscordStatus.Error, $"Discord disconnected ({reason}). Retrying in {backoff.TotalSeconds:0} s."));
                    backoff = RetryLater(backoff);
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Shutting down: whatever was in flight (a connect attempt, a send) no longer matters.
        }
        finally
        {
            await DisconnectAsync(clear: true).ConfigureAwait(false);
            SetState(new(DiscordStatus.Off, "Not connected"));
        }
    }

    private async Task WaitAsync(TimeSpan timeout, CancellationToken token, Task? alsoWakeOn = null)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
        var tasks = new List<Task> { _signal.Reader.WaitToReadAsync(wake.Token).AsTask() };
        if (timeout != Timeout.InfiniteTimeSpan) tasks.Add(Task.Delay(timeout, wake.Token));
        if (alsoWakeOn is not null) tasks.Add(alsoWakeOn);
        await Task.WhenAny(tasks).ConfigureAwait(false);
        await wake.CancelAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    private async Task DisconnectAsync(bool clear)
    {
        DiscordIpcClient? client;
        lock (_lock)
        {
            client = _client;
            _client = null;
            _clientId = null;
        }
        if (client is null) return;
        if (clear && !client.Closed.IsCompleted && _sentJson is not null && _sentJson != "null")
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await client.SetActivityAsync(null, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is DiscordRpcException or IOException or OperationCanceledException or TimeoutException or ObjectDisposedException)
            {
            }
        }
        _sentJson = null;
        await client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Schedule the next connection attempt and return the following backoff.</summary>
    private TimeSpan RetryLater(TimeSpan backoff)
    {
        lock (_lock) _retryAt = DateTimeOffset.UtcNow + backoff;
        return TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _maxBackoff.Ticks));
    }

    private static string Describe(Exception e) => e switch
    {
        OperationCanceledException or TimeoutException => "Discord did not answer.",
        DiscordRpcException { Code: 4000 } => "Discord rejected the application ID; check it in Settings.",
        _ => e.Message.EndsWith('.') ? e.Message : e.Message + ".",
    };

    private void SetState(DiscordState state)
    {
        if (state == State) return;
        State = state;
        StateChanged?.Invoke(state);
    }
}
