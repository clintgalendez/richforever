using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using RichForever.Core;

namespace RichForever.Tests;

/// <summary>Strip images produced by the addon test suite (addon/tests/run.lua --fixtures).</summary>
internal static class Fixture
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>Load a binary PPM (RGB) and convert it to the BGRA layout the decoder expects.</summary>
    public static byte[] Bgra(string name)
    {
        var data = File.ReadAllBytes(Path.Combine(Directory, name + ".ppm"));
        var header = Encoding.ASCII.GetString(data, 0, 32).Split((char[])['\n', ' '], 5);
        Assert.Equal("P6", header[0]);
        Assert.Equal(StripDecoder.Width, int.Parse(header[1], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(StripDecoder.Height, int.Parse(header[2], System.Globalization.CultureInfo.InvariantCulture));
        var offset = data.Length - StripDecoder.Width * StripDecoder.Height * 3;
        var bgra = new byte[StripDecoder.Width * StripDecoder.Height * 4];
        for (int i = 0, o = offset; i < bgra.Length; i += 4, o += 3)
        {
            bgra[i] = data[o + 2];
            bgra[i + 1] = data[o + 1];
            bgra[i + 2] = data[o];
            bgra[i + 3] = 255;
        }
        return bgra;
    }

    public static string Payload(string name) => File.ReadAllText(Path.Combine(Directory, name + ".txt")).TrimEnd('\n');

    public static int Stride => StripDecoder.Width * 4;
}

internal static class Wait
{
    public static async Task For(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }
}

/// <summary>A stand-in for the Discord desktop client's IPC pipe.</summary>
internal sealed class FakeDiscord : IAsyncDisposable
{
    private readonly string _pipeName = "richforever-test-" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();
    private readonly List<NamedPipeServerStream> _connections = [];
    private readonly Task _acceptLoop;

    public FakeDiscord() => _acceptLoop = Task.Run(AcceptLoop);

    public List<JsonNode> Activities { get; } = [];
    public int Handshakes { get; private set; }
    public string? RejectHandshake { get; set; }
    public string? RejectActivity { get; set; }

    public DiscordPresenceSync.Connector Connector => (clientId, ct) => DiscordIpcClient.ConnectAsync(clientId, ct, Open);

    public async Task<Stream> Open(CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000, cancellationToken);
        return client;
    }

    public void DropConnections()
    {
        lock (_connections)
        {
            foreach (var connection in _connections) connection.Dispose();
            _connections.Clear();
        }
    }

    public List<string?> ActivityStates()
    {
        lock (Activities) return Activities.Select(a => a?["state"]?.GetValue<string>()).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        DropConnections();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }
            lock (_connections) _connections.Add(server);
            _ = Task.Run(() => Serve(server));
        }
    }

    private async Task Serve(Stream stream)
    {
        try
        {
            var (_, handshake) = await Read(stream);
            Handshakes++;
            if (RejectHandshake is not null)
            {
                await Write(stream, 2, new JsonObject { ["code"] = 4000, ["message"] = RejectHandshake });
                stream.Dispose();
                return;
            }
            Assert.Equal(1, handshake!["v"]!.GetValue<int>());
            await Write(stream, 1, new JsonObject
            {
                ["cmd"] = "DISPATCH",
                ["evt"] = "READY",
                ["data"] = new JsonObject { ["user"] = new JsonObject { ["username"] = "tester" } },
            });
            while (true)
            {
                var (op, body) = await Read(stream);
                if (op != 1 || body is null) continue;
                Assert.Equal("SET_ACTIVITY", body["cmd"]!.GetValue<string>());
                var nonce = body["nonce"]!.GetValue<string>();
                if (RejectActivity is not null)
                {
                    await Write(stream, 1, new JsonObject { ["cmd"] = "SET_ACTIVITY", ["evt"] = "ERROR", ["nonce"] = nonce, ["data"] = new JsonObject { ["code"] = 4000, ["message"] = RejectActivity } });
                    continue;
                }
                lock (Activities) Activities.Add(body["args"]!["activity"]!);
                await Write(stream, 1, new JsonObject { ["cmd"] = "SET_ACTIVITY", ["evt"] = null, ["nonce"] = nonce, ["data"] = new JsonObject() });
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException)
        {
        }
    }

    private static async Task<(int Op, JsonNode? Body)> Read(Stream stream)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
        await stream.ReadExactlyAsync(body);
        return (BinaryPrimitives.ReadInt32LittleEndian(header), body.Length == 0 ? null : JsonNode.Parse(body));
    }

    private static async Task Write(Stream stream, int op, JsonNode body)
    {
        var json = Encoding.UTF8.GetBytes(body.ToJsonString());
        var frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }
}
