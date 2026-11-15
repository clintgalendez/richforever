using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RichForever.Core;

public sealed class DiscordRpcException(string message, int code = 0) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// Minimal client for the Discord desktop client's local RPC socket
/// (named pipe \\.\pipe\discord-ipc-N). Frames are a little-endian int32 opcode, an int32 length
/// and a UTF-8 JSON body. Only the handshake and SET_ACTIVITY are used; no OAuth, no tokens.
/// </summary>
public sealed class DiscordIpcClient : IAsyncDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;
    private const int MaxFrameBytes = 64 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode>> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private Task _readLoop = Task.CompletedTask;

    private DiscordIpcClient(Stream stream) => _stream = stream;

    /// <summary>Discord username from the READY event, shown locally in diagnostics only.</summary>
    public string? UserName { get; private set; }

    /// <summary>Completes when the connection ends, with the reason.</summary>
    public Task<string> Closed => _closedSource.Task;
    private readonly TaskCompletionSource<string> _closedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<DiscordIpcClient> ConnectAsync(string clientId, CancellationToken cancellationToken, Func<CancellationToken, Task<Stream>>? openPipe = null)
    {
        var stream = await (openPipe ?? OpenDiscordPipeAsync)(cancellationToken).ConfigureAwait(false);
        var client = new DiscordIpcClient(stream);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            await client.WriteAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId }, timeout.Token).ConfigureAwait(false);
            while (true)
            {
                var (op, body) = await client.ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                if (op == OpClose) throw new DiscordRpcException(ErrorMessage(body, "Discord refused the connection"), ErrorCode(body));
                if (op == OpPing)
                {
                    await client.WriteAsync(OpPong, body, timeout.Token).ConfigureAwait(false);
                    continue;
                }
                if (op == OpFrame && body?["evt"]?.GetValue<string>() == "READY")
                {
                    client.UserName = body["data"]?["user"]?["global_name"]?.GetValue<string>()
                        ?? body["data"]?["user"]?["username"]?.GetValue<string>();
                    break;
                }
                if (op == OpFrame && body?["evt"]?.GetValue<string>() == "ERROR")
                {
                    throw new DiscordRpcException(ErrorMessage(body["data"], "Discord handshake failed"), ErrorCode(body["data"]));
                }
            }
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        client._readLoop = Task.Run(client.ReadLoopAsync, CancellationToken.None);
        return client;
    }

    /// <summary>Set (or clear, with null) the activity for this process.</summary>
    public async Task SetActivityAsync(Activity? activity, CancellationToken cancellationToken)
    {
        if (Closed.IsCompleted) throw new DiscordRpcException("Not connected to Discord.");
        var nonce = Guid.NewGuid().ToString("N");
        var args = new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["activity"] = activity is null ? null : JsonNode.Parse(activity.ToJson()),
        };
        var request = new JsonObject { ["cmd"] = "SET_ACTIVITY", ["args"] = args, ["nonce"] = nonce };
        var response = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = response;
        try
        {
            await WriteAsync(OpFrame, request, cancellationToken).ConfigureAwait(false);
            var reply = await response.Task.WaitAsync(RequestTimeout, cancellationToken).ConfigureAwait(false);
            if (reply["evt"]?.GetValue<string>() == "ERROR")
            {
                throw new DiscordRpcException(ErrorMessage(reply["data"], "Discord rejected the activity"), ErrorCode(reply["data"]));
            }
        }
        finally
        {
            _pending.TryRemove(nonce, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_closing.IsCancellationRequested)
        {
            await _closing.CancelAsync().ConfigureAwait(false);
        }
        await _stream.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        _closedSource.TrySetResult("closed");
        _writeLock.Dispose();
        _closing.Dispose();
    }

    private static async Task<Stream> OpenDiscordPipeAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
        throw new DiscordRpcException("Discord is not running (no discord-ipc pipe found).");
    }

    private async Task ReadLoopAsync()
    {
        var reason = "Discord closed the connection";
        try
        {
            while (!_closing.IsCancellationRequested)
            {
                var (op, body) = await ReadFrameAsync(_closing.Token).ConfigureAwait(false);
                if (op == OpPing)
                {
                    await WriteAsync(OpPong, body, _closing.Token).ConfigureAwait(false);
                }
                else if (op == OpClose)
                {
                    reason = ErrorMessage(body, reason);
                    return;
                }
                else if (op == OpFrame && body is not null)
                {
                    var nonce = body["nonce"]?.GetValue<string>();
                    if (nonce is not null && _pending.TryGetValue(nonce, out var waiter)) waiter.TrySetResult(body);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or JsonException or DiscordRpcException or InvalidOperationException)
        {
            reason = _closing.IsCancellationRequested ? "closed" : e.Message;
        }
        finally
        {
            foreach (var waiter in _pending.Values) waiter.TrySetException(new DiscordRpcException(reason));
            _closedSource.TrySetResult(reason);
        }
    }

    private async Task WriteAsync(int op, JsonNode? body, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetBytes(body?.ToJsonString() ?? "{}");
        var frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<(int Op, JsonNode? Body)> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var op = BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > MaxFrameBytes) throw new DiscordRpcException($"Invalid frame length {length}.");
        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return (op, length == 0 ? null : JsonNode.Parse(body));
    }

    private static string ErrorMessage(JsonNode? node, string fallback) =>
        node?["message"]?.GetValue<string>() is { Length: > 0 } message ? message : fallback;

    private static int ErrorCode(JsonNode? node) =>
        node?["code"] is JsonValue value && value.TryGetValue<int>(out var code) ? code : 0;
}
