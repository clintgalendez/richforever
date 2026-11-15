using RichForever.Core;

namespace RichForever.Tests;

public class DiscordIpcClientTests
{
    [Fact]
    public async Task Handshake_reads_user_and_sets_activity()
    {
        await using var discord = new FakeDiscord();
        await using var client = await DiscordIpcClient.ConnectAsync("123456789012345678", CancellationToken.None, discord.Open);

        Assert.Equal("tester", client.UserName);
        await client.SetActivityAsync(new Activity { State = "Inside Stratholme" }, CancellationToken.None);
        await client.SetActivityAsync(null, CancellationToken.None);

        Assert.Equal(["Inside Stratholme", null], discord.ActivityStates());
    }

    [Fact]
    public async Task Rejected_handshake_reports_discord_message()
    {
        await using var discord = new FakeDiscord { RejectHandshake = "Invalid Client ID" };

        var error = await Assert.ThrowsAsync<DiscordRpcException>(() => DiscordIpcClient.ConnectAsync("1", CancellationToken.None, discord.Open));
        Assert.Equal("Invalid Client ID", error.Message);
        Assert.Equal(4000, error.Code);
    }

    [Fact]
    public async Task Rejected_activity_throws_with_reason()
    {
        await using var discord = new FakeDiscord { RejectActivity = "child \"activity\" fails" };
        await using var client = await DiscordIpcClient.ConnectAsync("1", CancellationToken.None, discord.Open);

        var error = await Assert.ThrowsAsync<DiscordRpcException>(() => client.SetActivityAsync(new Activity { State = "x" }, CancellationToken.None));
        Assert.Contains("fails", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closed_completes_when_discord_goes_away()
    {
        await using var discord = new FakeDiscord();
        await using var client = await DiscordIpcClient.ConnectAsync("1", CancellationToken.None, discord.Open);

        discord.DropConnections();

        await client.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<DiscordRpcException>(() => client.SetActivityAsync(new Activity(), CancellationToken.None));
    }
}

public class DiscordPresenceSyncTests
{
    private const string AppId = "123456789012345678";

    private static DiscordPresenceSync Create(FakeDiscord discord, int minIntervalMs = 0) =>
        new(discord.Connector, TimeSpan.FromMilliseconds(minIntervalMs), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200));

    [Fact]
    public async Task Sends_only_changes()
    {
        await using var discord = new FakeDiscord();
        await using var sync = Create(discord);
        sync.Start();

        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => discord.ActivityStates().Count == 1);
        sync.Update(AppId, true, new Activity { State = "A" });
        sync.Update(AppId, true, new Activity { State = "B" });
        await Wait.For(() => discord.ActivityStates().Count == 2);
        await Task.Delay(100);

        Assert.Equal(["A", "B"], discord.ActivityStates());
        Assert.Equal(DiscordStatus.Connected, sync.State.Status);
        Assert.Equal("tester", sync.State.UserName);
        Assert.Equal(2, sync.UpdatesSent);
    }

    [Fact]
    public async Task Rate_limit_coalesces_to_the_latest_presence()
    {
        await using var discord = new FakeDiscord();
        await using var sync = Create(discord, minIntervalMs: 400);
        sync.Start();

        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => discord.ActivityStates().Count == 1);
        var first = Environment.TickCount64;
        sync.Update(AppId, true, new Activity { State = "B" });
        sync.Update(AppId, true, new Activity { State = "C" });
        await Wait.For(() => discord.ActivityStates().Count == 2);

        Assert.True(Environment.TickCount64 - first >= 300, "second update waited for the rate limit");
        await Task.Delay(500);
        Assert.Equal(["A", "C"], discord.ActivityStates());
    }

    [Fact]
    public async Task Reconnects_and_resends_after_discord_restarts()
    {
        await using var discord = new FakeDiscord();
        await using var sync = Create(discord);
        sync.Start();
        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => discord.ActivityStates().Count == 1);

        discord.DropConnections();

        await Wait.For(() => discord.Handshakes == 2 && discord.ActivityStates().Count == 2);
        Assert.Equal(["A", "A"], discord.ActivityStates());
        await Wait.For(() => sync.State.Status == DiscordStatus.Connected);
    }

    [Fact]
    public async Task Clears_activity_when_no_longer_wanted()
    {
        await using var discord = new FakeDiscord();
        await using var sync = Create(discord);
        sync.Start();
        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => discord.ActivityStates().Count == 1);

        sync.Update(AppId, false, null);

        await Wait.For(() => sync.State.Status == DiscordStatus.Off);
        Assert.Equal(["A", null], discord.ActivityStates());
    }

    [Fact]
    public async Task Dispose_clears_activity()
    {
        await using var discord = new FakeDiscord();
        var sync = Create(discord);
        sync.Start();
        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => discord.ActivityStates().Count == 1);

        await sync.DisposeAsync();

        Assert.Equal(["A", null], discord.ActivityStates());
    }

    [Fact]
    public async Task Missing_application_id_stays_offline()
    {
        await using var discord = new FakeDiscord();
        await using var sync = Create(discord);
        sync.Start();

        sync.Update(null, true, new Activity { State = "A" });

        await Wait.For(() => sync.State.Message == "No Discord application ID set");
        Assert.Equal(0, discord.Handshakes);
    }

    [Fact]
    public async Task Discord_not_running_retries_with_backoff()
    {
        var attempts = 0;
        await using var sync = new DiscordPresenceSync((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new DiscordRpcException("Discord is not running.");
        }, TimeSpan.Zero, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40));
        sync.Start();

        sync.Update(AppId, true, new Activity());

        await Wait.For(() => Volatile.Read(ref attempts) >= 3);
        Assert.Equal(DiscordStatus.Error, sync.State.Status);
        Assert.StartsWith("Discord is not running.", sync.State.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Presence_changes_do_not_cut_the_backoff_short()
    {
        var attempts = 0;
        await using var sync = new DiscordPresenceSync((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new DiscordRpcException("Discord is not running.");
        }, TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        sync.Start();
        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => Volatile.Read(ref attempts) == 1);

        for (var i = 0; i < 20; i++)
        {
            sync.Update(AppId, true, new Activity { State = "S" + i });
            await Task.Delay(5);
        }
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref attempts));

        sync.Reconnect();
        await Wait.For(() => Volatile.Read(ref attempts) == 2);
    }

    [Fact]
    public async Task Rejected_handshake_surfaces_as_error()
    {
        await using var discord = new FakeDiscord { RejectHandshake = "Invalid Client ID" };
        await using var sync = Create(discord);
        sync.Start();

        sync.Update(AppId, true, new Activity());

        await Wait.For(() => sync.State.Status == DiscordStatus.Error);
        Assert.Contains("rejected the application ID", sync.State.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_activity_is_reported_without_reconnect_loop()
    {
        await using var discord = new FakeDiscord { RejectActivity = "bad asset" };
        await using var sync = Create(discord);
        sync.Start();

        sync.Update(AppId, true, new Activity { State = "A" });

        await Wait.For(() => sync.State.Message.Contains("bad asset", StringComparison.Ordinal));
        await Task.Delay(200);
        Assert.Equal(1, discord.Handshakes);
    }

    [Fact]
    public async Task Changing_application_id_reconnects()
    {
        await using var discord = new FakeDiscord();
        await using var sync = Create(discord);
        sync.Start();
        sync.Update(AppId, true, new Activity { State = "A" });
        await Wait.For(() => discord.ActivityStates().Count == 1);

        sync.Update("876543210987654321", true, new Activity { State = "A" });

        await Wait.For(() => discord.Handshakes == 2 && discord.ActivityStates().Count == 3);
        Assert.Equal(["A", null, "A"], discord.ActivityStates());
    }
}
