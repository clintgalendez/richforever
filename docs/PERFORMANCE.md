# Performance

## Measured

Environment: Windows 11 Home (build 26300), 16 logical processors, .NET 10.0.10 runtime, release
build of 0.1.0-beta.1, 15 November 2026. Measured with
[`scripts/measure-companion.ps1`](../scripts/measure-companion.ps1) over 120 seconds.

| State | CPU time | ≈ share of one core | Private memory | Working set | Threads |
| --- | --- | --- | --- | --- | --- |
| Waiting for the game (5 s check) | 31 ms in 120 s | 0.03 % | 13.2 MB (max 13.5) | 53.9 MB | 7 |
| In game: strip read every second, presence change every 15 s | 453 ms in 120 s | 0.38 % | 15.4 MB (max 16.4) | 61.4 MB | 15 |

Waiting mode was measured after a 60 s warm-up, in game after 30 s.

"In game" used the [strip demo](DEVELOPMENT.md#testing-the-companion-without-wow) as the game window,
cycling four presences, with Discord running and an unregistered application ID (so the
companion also retried the Discord handshake every 60 s).

The update check (a minute after start, then every 6 hours) loads .NET's HTTPS stack once, which
adds about 3.5 MB of private memory for the rest of the run (measured with the same runtime in a
console test). Turning off *Install updates automatically* avoids it.

Startup: 150–330 ms from process start to the first log line (self-contained build; the higher value
was the first, cold start).

### What changed during tuning

| Change | Before | After |
| --- | --- | --- |
| Single-file bundle compression off | 54 MB private | 13 MB private |
| Game detection by visible windows instead of a process snapshot (a snapshot made the kernel gather every thread on the system, ~14 ms CPU per check) | 180–195 ms CPU per minute while waiting | 15.5 ms per minute |

Per-read cost, from [`PerformanceTests`](../companion/tests/RichForever.Tests/PerformanceTests.cs):

| Step | Average |
| --- | --- |
| GDI capture of the 192×30 strip region | 8.3 ms wall time (mostly waiting on the desktop compositor, not CPU) |
| Decode + parse + build presence JSON | 0.12 ms |

Discord updates: at most one per 5 seconds (Discord allows 5 per 20 seconds), and only when the
presence text, images or settings change. In the run above, 4 presence changes per minute produced 4
updates.

Packaging comparison (idle memory): see [RESEARCH.md](RESEARCH.md#companion-technology). Bundle
compression costs about 40 MB of private memory, so release builds are uncompressed.

## Not yet measured

- The addon's cost inside WoW Forever. By design it does no work between events, collects about 25
  values after a relevant event (coalesced to at most one refresh per 0.25 s, or per 5 s for stat
  events), and recolours at most 640 textures (typically 256) only when the payload changes. Live measurement with the
  client's addon profiler is part of the live-client verification issue.
- Long-running memory over hours of play.

## Targets

| Metric | Target |
| --- | --- |
| Waiting mode CPU | < 0.1 % of one core averaged over a minute (measured 0.03 %) |
| In-game CPU | < 1 % of one core (measured 0.38 %) |
| Private memory | < 25 MB (measured 13–16 MB) |
| Discord updates | Only on change, ≤ 1 per 5 s |
