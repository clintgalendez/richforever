# Architecture

RichForever has two parts that never talk over a network:

| Part | Where | Job |
| --- | --- | --- |
| Addon (`addon/RichForever`) | Inside WoW Forever, Lua 5.1 | Collect character state, draw it as a pixel strip |
| Companion (`companion/`) | Windows tray app, .NET 10 | Read the strip, validate it, keep Discord's presence in sync |

```
┌──────────────────────── WoW Forever ────────────────────────┐
│ events ─► State.Collect ─► Protocol.Serialize ─► EncodeCells │
│                                         │                    │
│                              Strip.Render (top-left corner)  │
└─────────────────────────────────────────┼────────────────────┘
                                          │ screen pixels (one-way)
┌──────────────────────── Companion ──────┼────────────────────┐
│ GameWatcher ─► ScreenCapture ─► StripDecoder ─► PayloadParser│
│                                                   │          │
│              PresenceEngine (game state) ◄────────┘          │
│                    │                                         │
│              PresenceBuilder + AssetCatalog ─► Activity      │
│                    │                                         │
│              DiscordPresenceSync ─► DiscordIpcClient ────────┼─► \\.\pipe\discord-ipc-N
└──────────────────────────────────────────────────────────────┘
```

## Why a pixel strip

WoW addons run in a sandbox: no sockets, no files except SavedVariables (written only on logout or
`/reload`), no access to other processes. Every alternative was evaluated in
[RESEARCH.md](RESEARCH.md#addon-to-companion-transport). Drawing pixels is the only real-time,
supported way out. It is the same idea as other WoW Forever tools (for example
[witchcraft](https://github.com/Geraldlol/witchcraft)) and older WoW presence tools, but the protocol
here is RichForever's own.

The channel is one-way: the companion cannot send anything to the game, and the game cannot send
commands to the companion. The strip carries data that is checksummed, size-limited and validated
field by field.

## Strip protocol v1

Defined in [`addon/RichForever/Protocol.lua`](../addon/RichForever/Protocol.lua) and
[`StripDecoder.cs`](../companion/src/RichForever.Core/StripDecoder.cs).

- Grid: 64 cells per row, up to 10 rows, each cell 3×3 physical pixels (192×30 px maximum).
- Each cell carries 6 bits: 2 bits per colour channel, levels 0, 85, 170, 255.
- The addon frame has no parent (Alt+Z does not hide it), sits at TOOLTIP strata in the top-left
  corner, and scales itself so one UI unit equals one physical pixel (`768 / physical height`).

| Cells | Content |
| --- | --- |
| 1–7 | Magic and calibration: red, green, blue, white, light grey, dark grey, black (48, 12, 3, 63, 42, 21, 0) |
| 8 | Protocol version (1) |
| 9–10 | Payload length in bytes (12 bits, at most 468) |
| 11–13 | Fletcher-16 checksum of the payload |
| 14… | Payload: every 3 bytes become 4 cells, zero padded |

The decoder learns the four colour levels per channel from the grey calibration cells, so moderate
gamma or brightness differences still decode. A strip is accepted only if the calibration levels are
strictly increasing, all seven magic cells match, the version is known, the length is in range and the
checksum matches.

### Payload

UTF-8 text, one `key=value` per line, in this order. Every field is optional except `v`.

| Key | Meaning | Addon limit | Companion validation |
| --- | --- | --- | --- |
| `v` | Protocol version | int ≤ 63 | must be `1` |
| `n` | Character name | 36 bytes | ≤ 36 chars, no control chars, no outer spaces |
| `l` | Level | int ≤ 999 | 1–999 |
| `c` / `cn` | Class token (`PALADIN`) / localized name | 16 / 24 bytes | token `[A-Za-z][A-Za-z_]{1,15}` / text ≤ 24 |
| `r` / `rn` | Race token / localized name | 16 / 24 bytes | token / text ≤ 24 |
| `f` | Faction | 12 bytes | `Alliance`, `Horde`, `Neutral` |
| `g` | Sex (1 unknown, 2 male, 3 female) | int ≤ 9 | 1–3 |
| `z` / `sz` | Zone / subzone | 48 bytes each | text ≤ 48 |
| `m` | uiMapID from `C_Map.GetBestMapForUnit` | int ≤ 999999 | 1–999999 |
| `it` | Instance type from `IsInInstance` | 10 bytes | `none party raid pvp arena scenario unknown` |
| `in` / `ii` | Instance name / instance ID from `GetInstanceInfo` | 48 bytes / int | text / 1–999999 |
| `d` / `mp` | Difficulty name / max players | 24 bytes / int ≤ 99 | text / 1–99 |
| `gs` | Group size (only when > 1) | int ≤ 99 | 1–99 |
| `hp` / `pp` / `xp` | Health / power / XP percent (opt-in) | int ≤ 100 | 0–100 |
| `pt` | Power token (`MANA`, `RAGE`…) | 12 bytes | token |
| `fl` | Flags: `c` combat, `d` dead, `r` resting, `a` away | 12 bytes | known letters only |
| `ss` | Session start, Unix seconds | int | 2000–2100; used only if within the last 3 days |

Structural errors (invalid UTF-8, malformed or duplicate lines, control characters, oversize) reject
the whole payload. An invalid value in a known field drops just that field. Unknown keys are ignored
so a newer addon still works with an older companion. Adding a field is backwards compatible; changing
the meaning of a field or the cell layout requires a new protocol version.

## Addon

| File | Responsibility |
| --- | --- |
| `Protocol.lua` | Pure: field list, serialisation, UTF-8-safe truncation, checksum, cell encoding |
| `State.lua` | Reads the WoW API. Every call is `pcall`-protected; values that `issecretvalue` marks secret are dropped before any arithmetic or string work |
| `Strip.lua` | Creates the frame and textures once and recolours them only when the payload changes |
| `Core.lua` | Settings (`RichForeverDB`), session start (`RichForeverCharDB`), event handling, slash commands |
| `Options.lua` | Settings panel through `Settings.RegisterCanvasLayoutCategory`, optional |

Event handling is coalesced: zone, level, group, combat and similar events refresh after 0.25 s;
health and power events (only when stats sharing is on) refresh at most every 5 s. Nothing runs on a
timer while nothing changes, and textures are not touched when the payload is unchanged.

Session start is `time()` at the first `PLAYER_ENTERING_WORLD` with `isInitialLogin`; a `/reload`
reuses the stored value (if under 12 hours old), so reloading the UI does not reset the timer.

## Companion

`RichForever.Core` (no Windows dependencies, fully unit tested):

| Type | Responsibility |
| --- | --- |
| `StripDecoder` | Pixels → payload bytes |
| `PayloadParser`, `GameSnapshot` | Bytes → validated, immutable snapshot |
| `PresenceEngine` | Game running or not, latest snapshot, missing-strip timeout (20 s), session start |
| `PresenceBuilder`, `AssetCatalog` | Snapshot + settings → Discord activity, with privacy rules and image fallbacks |
| `DiscordIpcClient` | Discord's local RPC pipe: handshake, `SET_ACTIVITY`, ping/pong, close |
| `DiscordPresenceSync` | One background loop: connect, send only changes, ≤ 1 update per 5 s, reconnect with backoff (5 s → 60 s), clear on exit |
| `AppSettings` | JSON settings with validation and atomic save |

`RichForever.App` (Windows):

| Type | Responsibility |
| --- | --- |
| `CompanionController` | Lifecycle: waiting mode timer, capture timer, wiring |
| `GameWatcher` | Finds a visible `Wow.exe`/`WowB.exe` window under a `_forever` folder, locates the client area, checks that the game window is topmost at the strip |
| `ScreenCapture` | GDI `BitBlt` of the strip region into one reusable DIB |
| `TrayApplication`, `MainForm` | Tray icon and menu, status and settings window |
| `StartupRegistration`, `Log` | HKCU Run entry, size-capped log file |

### Lifecycle

```
          ┌──────────────┐  process found   ┌──────────────┐
 start ──►│   Waiting    │─────────────────►│   In game    │
          │ poll every 5s│◄─────────────────│ read every 1s│
          └──────────────┘   process exits  └──────────────┘
                 ▲            (or exit app if                │
                 │             "exit when WoW closes")       │ strip visible?
                 │                                           ├─ yes: decode → engine
                 │                                           └─ no (minimised/covered): keep last state
```

- Waiting mode: every 5 s (configurable 2–60 s) the companion lists the owners of visible top-level
  windows and checks their executable names. This is about 1/10 of the cost of a process snapshot.
  No capture, no Discord connection unless *connect immediately* is chosen.
- In game: the strip is read only if the game window is the topmost window at both ends of the strip
  region, so other windows are never captured. If the strip is visible but unreadable for 20 s (for
  example at the character screen), the presence becomes "In menus".
- Discord: the desired activity is pushed to the sync loop, which wakes only on change. Duplicate
  activities are not resent. If Discord is closed or restarts, the loop reconnects with backoff and
  resends the current activity. On exit it clears the activity (Discord also clears it when the
  process ends).

## Discord presence mapping

| Discord field | Content |
| --- | --- |
| Application name | "World of Warcraft Forever": RichForever's Discord application `1571656707078103191` (or your own) |
| `details` | `Archer · Level 60 Human Paladin` (name optional) |
| `state` | `Exploring Eastern Plaguelands`, `Resting in Goldshire`, `Inside Stratholme`, `In Warsong Gulch`, `Inside an instance`, `Dead in …`, `Away in …`, optional `In combat in …` |
| `timestamps.start` | Session start → Discord shows "elapsed" |
| `assets.large_image` / `large_text` | Instance art → zone art → type badge → logo; `Dungeon: Stratholme (Normal)` or `Zone · Subzone` |
| `assets.small_image` / `small_text` | Class icon; `Paladin · Alliance` plus optional `HP 85% · Mana 40% · XP 42%` |
| `party` | Group size, e.g. 3 of 5 (optional) |
| `buttons` | Up to two https links from settings |

Text is trimmed to Discord's 128-character limit and fields shorter than 2 characters are omitted.

## Assets

[`companion/assets/catalog.json`](../companion/assets/catalog.json) maps class tokens, instance types,
zones (by uiMapID, then by name) and instances (by instance ID, then by name) to Discord asset keys.
The catalog only lists keys for which art exists, generated by
[`scripts/generate_assets.py`](../scripts/generate_assets.py) into `companion/assets/discord/`. Anything
not in the catalog falls back to a type badge or the logo, so new Forever zones still get a sensible
picture. Users can override the catalog with `%APPDATA%\RichForever\assets.json`, where values may also
be https image URLs. Nothing is downloaded at runtime. See [DISCORD_SETUP.md](DISCORD_SETUP.md).
