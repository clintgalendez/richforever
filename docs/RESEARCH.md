# Research notes

Findings that shaped RichForever, with sources. **Confirmed** means checked on a real WoW Forever
install, a real Discord client, or official documentation. **Assumed** means inferred and still in
need of live verification.

## WoW Forever client

| Finding | Status | Evidence |
| --- | --- | --- |
| WoW Forever is a Battle.net flavour `wowf`, installed in `World of Warcraft\_forever_` with `Wow.exe` and `wow_loader.dll` | Confirmed | `.build.info` and `.flavor.info` of an installed client; product `wowf`, version `1.60.1.70938` (Nov 2026) |
| Addon interface number is `16001` | Confirmed | TOC files of Forever-specific addons installed on the same client (e.g. Polestar: "Leveling guides for World of Warcraft: Forever", `## Interface: 16001`); [Kami-Labs](https://kami-labs.fr/en/addons-wow-forever/), [Icy Veins](https://www.icy-veins.com/wow-forever/news/set-up-your-wow-forever-ui-before-launch-with-these-9-addons/) |
| The client uses the modern (Mainline/Midnight) UI engine; Classic Era addons do not load | Confirmed by community, consistent with the API below | [Kami-Labs](https://kami-labs.fr/en/wow-classic/wow-forever-addons-restreints-comme-sur-midnight/), [Gfinity](https://www.gfinityesports.com/article/world-of-warcraft-forever-ui-addons-setup-guide) |
| Modern APIs exist: `C_Map.GetBestMapForUnit`, `GetInstanceInfo`, `IsInInstance`, `GetRealZoneText`, `GetSubZoneText`, `UnitFactionGroup`, `GetPhysicalScreenSize`, `Settings.RegisterCanvasLayoutCategory`, `C_Timer`, `issecretvalue` | Confirmed in use by Forever addons; not yet called by RichForever in the live client | Usage counts across installed Forever addons (Polestar, Leatrix Maps Forever build, DialogueUI, MaxDps) |
| Some values are **secret** in combat (Midnight-style restrictions); even frame scales can be secret | Confirmed by addon code | Leatrix Maps: `if issecretvalue and issecretvalue(scale) then return end -- Frame scales can be secret in combat`; X-Perl Forever notes restricted unit info |
| Open-world uiMapIDs are classic-era (1411 Durotar … 1452 Winterspring) plus Forever-only zones 2482 Mount Hyjal, 2521 Zephras Isle, 2548 Riverglades | Confirmed from Forever addon data | Polestar `Maps/ZoneLevels.lua` (facts only; no code copied) |
| City map IDs 1453–1458, battleground maps and instance IDs (e.g. 329 Stratholme, 409 Molten Core) | **Assumed** (classic-era values) | Catalog falls back to names when IDs differ |
| The game process is `Wow.exe` under `_forever_`; some tools also expect `WowB.exe` | Partly assumed | Install folder contents; [witchcraft](https://github.com/Geraldlol/witchcraft) checks `WowB`, which beta-era crash reports also show. RichForever accepts both and filters by the `_forever` path |
| New race "Skyborne" and possibly new content exist | Observed | Polestar race guide packs |

Live-client checks still needed (tracked as an issue): the addon loads, `GetPhysicalScreenSize`
scaling puts the strip at exact pixels, `issecretvalue` handling in combat, the options panel,
`AddonCompartmentFunc`, and real instance IDs.

## Addon-to-companion transport

| Option | Verdict |
| --- | --- |
| Sockets, HTTP, named pipes from Lua | Not available to addons |
| SavedVariables files | Written only on logout and `/reload`; not real time |
| Chat log / combat log files | Written by the client; addons cannot inject their own data reliably |
| Addon messages / chat channels | Go through the game server; no local endpoint; would leak data |
| Reading game memory | Breaks the Blizzard Terms of Use and risks bans; rejected |
| **Pixel strip read from the screen** | Works in real time with supported APIs; chosen |

The pixel approach is proven on Forever by [witchcraft](https://github.com/Geraldlol/witchcraft)
(a 132×12 calibrated strip read with GDI while WoW is in the foreground). RichForever does not copy
that implementation; it differs in that it needs no calibration step (the strip is anchored to the
game's client-area origin, which the companion finds itself), it self-calibrates colour levels from
grey cells, it validates with a Fletcher-16 checksum, and it reads whenever the game window is
topmost at the strip, not only in the foreground.

## Discord

| Finding | Source |
| --- | --- |
| Local RPC uses `\\?\pipe\discord-ipc-{0..9}`; frames are int32 LE opcode + int32 LE length + JSON; opcodes 0 handshake, 1 frame, 2 close, 3 ping, 4 pong; handshake `{ "v": 1, "client_id": "…" }` answered by a `READY` dispatch | [Discord RPC docs](https://docs.discord.com/developers/topics/rpc) |
| `SET_ACTIVITY` takes `{ pid, activity }`; activity types Playing, Listening, Watching, Competing | same |
| Activity fields: `details`, `state`, `timestamps`, `assets` (large/small image and text), `party`, up to 2 `buttons` (label 1–32, url 1–512); clients may update 5 times per 20 s; asset values are application asset keys or public image URLs | [Activity object](https://docs.discord.com/developers/events/gateway-events) |
| The Discord Social SDK is recommended for **games** adding social features | RPC docs warning |
| RPC over WebSocket is deprecated; IPC is the supported transport | RPC docs |

Decision: RichForever is an external companion, not a game build, and only needs Rich Presence. It
implements the IPC `SET_ACTIVITY` flow directly (about 200 lines, no third-party library, no OAuth,
no tokens, nothing stored). The Social SDK is a native C++ SDK distributed through the developer
portal and would add a large binary for no benefit here.

Verified on a real Discord client (Windows, Discord app 1.0.9268): the companion finds the pipe,
completes the handshake framing and receives Discord's `Invalid Client ID` close for an unregistered
ID, which it reports and backs off from. A successful `SET_ACTIVITY` on the real client requires a
registered application ID and is part of the live verification issue; the success path is covered by
tests against a fake Discord pipe server that speaks the same framing.

## Companion technology

| Option | Notes |
| --- | --- |
| **C# / .NET 10 WinForms** (chosen) | SDK available, first-class Win32 interop, tray and small windows built in, easy for contributors. Measured 13 MB private memory idle |
| Rust | Smallest footprint, but toolchain not installed here and higher contribution barrier |
| Node.js | 40 MB+ runtime and native modules for the tray |
| Electron | Hundreds of MB; rejected |

Packaging measurements (same build, idle in the tray, 20 s after start):

| Variant | Download | Private memory | Working set |
| --- | --- | --- | --- |
| Self-contained single file, bundle compressed | 49 MB | 53.7 MB | 122.6 MB |
| **Self-contained single file, uncompressed** (shipped, zipped to 42 MB) | 103 MB exe | **13.3 MB** | 55.5 MB |
| **Framework-dependent single file** (shipped) | 0.56 MB exe | **12.8 MB** | 53.7 MB |
| Self-contained, compressed, ReadyToRun | 51 MB | 75.2 MB | 120.7 MB |

Compression decompresses the bundle into private memory, so it is not used.

## Lightweight Windows background apps

- Tray-only `ApplicationContext`, no hidden window loop, timers instead of threads.
- Workstation GC without the concurrent background thread (`ConcurrentGarbageCollection=false`).
- Per-user autostart through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (no admin, removed
  on opt-out and `--uninstall`). Task Scheduler process-start triggers need admin and auditing, so
  "start when the game starts" is approximated by autostart plus a 5 s waiting-mode check.

## GitHub Actions

Followed GitHub's [security hardening guide](https://docs.github.com/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions):
read-only default `permissions`, write permissions only on the release publish job, actions pinned
to full commit SHAs (resolved Nov 2026), `persist-credentials: false`, no `pull_request_target`,
untrusted values passed through `env` rather than inlined in scripts, Dependabot for actions and
NuGet with a cooldown. Workflows are checked by [actionlint](https://github.com/rhysd/actionlint) and
[zizmor](https://github.com/zizmorcore/zizmor). Release provenance uses
[`actions/attest`](https://github.com/actions/attest) v4 (the successor of
`actions/attest-build-provenance`).
