<p align="center">
  <img src="companion/assets/discord/logo.png" alt="RichForever logo" width="128">
</p>

<h1 align="center">RichForever</h1>

<p align="center"><b>Your WoW Forever adventure, shared on Discord.</b><br>
Lightweight Discord Rich Presence for World of Warcraft: Forever.</p>

<p align="center">
  <a href="https://github.com/clintgalendez/richforever/actions/workflows/ci.yml"><img src="https://github.com/clintgalendez/richforever/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/clintgalendez/richforever/actions/workflows/codeql.yml"><img src="https://github.com/clintgalendez/richforever/actions/workflows/codeql.yml/badge.svg" alt="CodeQL"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT license"></a>
</p>

RichForever is a free, open-source WoW Forever addon plus a small Windows tray app. Your Discord
profile shows what you are doing in the game:

```
Playing World of Warcraft Forever
Archer · Level 60 Human Paladin
Inside Stratholme  (3 of 5)
02:35:12 elapsed
[Stratholme art]  [Paladin icon]
```

> **Project status: beta (0.1.0-beta.3).** The companion, the protocol and the Discord integration are
> implemented and tested on Windows, and the full pipeline has been run on a real screen against the
> real Discord client, which displays the presence with the bundled artwork. The addon has **not yet
> been run inside the live WoW Forever client**; that check is the next milestone. See [Known limitations](#known-limitations).

## Features

- Character name, level, race, class and faction, with a class icon.
- Current zone and subzone with zone artwork; known dungeons, raids and battlegrounds by name with
  their own artwork; a neutral "Inside an instance" when the client gives no name.
- Session timer (time since you logged in this character, not lifetime `/played`).
- Group size ("3 of 5"), resting, dead and away states; optional combat state.
- Optional health, power and experience percentages.
- Privacy switches: pause, hide name, hide session time, hide zone and map, hide instance details.
- Tray app with a waiting mode: when WoW Forever is not running it checks for the game every few
  seconds and does nothing else. No Discord connection until the game starts (configurable).
- Start with Windows (per user, no administrator rights), exit when the game closes, reconnects to
  Discord automatically.
- Keeps itself up to date from GitHub Releases (can be turned off).
- No accounts, no tokens, no telemetry. The update check is its only network request of its own.

## Screenshots

Planned: the Discord profile card and the companion window will be added once the addon has been
verified in the live WoW Forever client. Until then, the [artwork](companion/assets/discord) shows
what the images on the profile look like.

## How it works

```
WoW Forever ─ RichForever addon ── draws a 192×30 px colour block (checksummed data)
                                          │  read from the screen once per second
Windows ──── RichForever companion ── decode → validate → build presence
                                          │  local named pipe (discord-ipc-0)
                                     Discord desktop app ── your profile
```

WoW addons cannot open sockets, write files while you play, or talk to other programs. The only
thing they can do is draw. So the addon draws its data as a tiny block of coloured cells in the
top-left corner of the game window, and the companion reads that block from the screen. The data is
checksummed and strictly validated; it is never executed. The companion never reads game memory and
never sends input to the game. Details: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Requirements

- Windows 10 or 11, 64-bit.
- World of Warcraft: Forever, in **windowed** or **windowed (fullscreen)** mode.
- The Discord desktop app, with *Settings → Activity Privacy → Share your detected activities* on.
- Nothing else: RichForever ships with its own Discord application ("World of Warcraft Forever") and
  artwork. Using your own application is optional ([docs/DISCORD_SETUP.md](docs/DISCORD_SETUP.md)).

## Installation

Full guide: [docs/INSTALLATION.md](docs/INSTALLATION.md). In short:

1. Download from [Releases](https://github.com/clintgalendez/richforever/releases):
   - `RichForever-addon-<version>.zip`
   - `RichForever-<version>-win-x64.zip` (or the small `-framework-dependent` one if you have the
     .NET 10 Desktop Runtime)
2. Extract the addon so you get `World of Warcraft\_forever_\Interface\AddOns\RichForever\RichForever.toc`.
3. Extract the companion anywhere (for example `%LOCALAPPDATA%\Programs\RichForever`) and run
   `RichForever.exe`. Windows SmartScreen may warn because the build is not code-signed yet; choose
   *More info → Run anyway* if you trust the download (verify it with `SHA256SUMS.txt`). Pick a
   folder you can write to: updates replace the files in it.
4. Start WoW Forever and log in. Your Discord profile updates within a few seconds.

## Using it

- **Tray icon**: right-click for status, *Open RichForever…*, *Connect to Discord*, *Pause presence*,
  *Check for updates…*, *Open log folder* and *Exit*. Double-click opens the window.
- **Window**: live status (game, Discord, character, presence, errors, CPU and memory) and settings.
- **In game**: `/rfor status`, `/rfor on|off`, `/rfor name on|off`, `/rfor stats on|off`, `/rfor debug`,
  or *Options → AddOns → RichForever*.

### Startup options

| Setting | Behaviour |
| --- | --- |
| Start RichForever when Windows starts | Adds `HKCU\…\Run\RichForever`; starts silently in the tray and waits for the game. |
| Connect to Discord only while WoW Forever runs (default) | No Discord connection in waiting mode. |
| Connect to Discord as soon as RichForever starts | Connects immediately (presence still appears only while you play). |
| Exit RichForever when WoW Forever closes | The companion quits when the game process ends. |
| Install updates automatically (default) | Checks GitHub Releases a minute after start and every 6 hours, installs a newer version and restarts; while you play, the restart waits until WoW Forever closes. |

"Start only when WoW launches" is not possible without administrator rights; *start with Windows +
waiting mode* is the light equivalent (about 0.03 % of one CPU core and 13 MB of memory while waiting).

## Privacy

Everything stays on your PC except what you choose to show on Discord. The addon can stop sharing
your name at the source; the companion has switches for name, session time, zone, instance details and
a pause. Full statement: [docs/PRIVACY.md](docs/PRIVACY.md). Security model:
[docs/SECURITY.md](docs/SECURITY.md).

## Performance

Measured on Windows 11 (16 logical cores), release build, see [docs/PERFORMANCE.md](docs/PERFORMANCE.md):

| State | CPU | Private memory |
| --- | --- | --- |
| Waiting for the game | ≈15 ms CPU per minute (≈0.03 % of one core) | 13 MB |
| In game, one strip read per second | ≈230 ms CPU per minute (≈0.4 % of one core) | 15–16 MB |

## Troubleshooting

See [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md). The most common causes: the game is in
exclusive fullscreen, something covers the top-left corner of the game, Discord is not running, or Discord's activity sharing is off.

## Known limitations

- **Live client verification pending.** The addon uses APIs that other WoW Forever addons use, but it
  has not been loaded in the live client yet. Confirm with `/rfor debug` and report results.
- The coloured block must stay visible in the top-left corner of the game window. Exclusive
  fullscreen, overlays drawn over that corner, extreme gamma or colour-blind filters can prevent
  reading it. Presence keeps the last state while the game is minimised or covered.
- Zone and instance artwork uses classic-era map and instance IDs, plus names as a fallback; only the
  map IDs of the open-world zones were confirmed for Forever.
- Session time is time since login on this character; lifetime `/played` is not shown.

## Development

[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) covers building, testing, the strip demo (test the
companion without WoW) and adding zones. Research notes: [docs/RESEARCH.md](docs/RESEARCH.md).
Releases: [docs/RELEASE.md](docs/RELEASE.md).

## Contributing

Issues and pull requests are welcome; read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE). All artwork in this repository is original and generated by
[`scripts/generate_assets.py`](scripts/generate_assets.py). RichForever is a fan project and is not
affiliated with or endorsed by Blizzard Entertainment or Discord. World of Warcraft is a trademark of
Blizzard Entertainment, Inc.
