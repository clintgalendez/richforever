# Development

## Layout

```
addon/RichForever/      the addon (what players install)
addon/tests/            Lua tests with a WoW API mock; also writes strip fixtures
companion/              .NET solution (RichForever.slnx)
  src/RichForever.Core  protocol, presence, Discord IPC (no Windows dependencies)
  src/RichForever.App   tray app, screen capture, startup (Windows)
  tests/                xUnit tests, including addon → companion integration tests
  assets/               catalog.json and generated Discord artwork
tests/fixtures/         strip images and payloads produced by the addon tests
scripts/                packaging, artwork, measurement and the strip demo
docs/                   documentation
```

## Prerequisites

- Windows 10/11 for the companion; any OS for the addon tests.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Lua 5.1 and [luacheck](https://github.com/lunarmodules/luacheck) (Ubuntu:
  `sudo apt-get install lua5.1 lua-check`; Windows: LuaBinaries 5.1 and LuaRocks, or WSL).
- Python 3 with Pillow, only to rebuild artwork (`python -m pip install pillow`).

## Addon

```sh
luacheck addon
lua5.1 addon/tests/run.lua                          # tests
lua5.1 addon/tests/run.lua --fixtures tests/fixtures # tests + regenerate fixtures
```

The tests load the addon files in TOC order against [`wowmock.lua`](../addon/tests/wowmock.lua),
fire events, and rasterise the strip frame exactly as the client would draw it. The resulting PPM
images in `tests/fixtures/` are decoded by the companion tests, so the two halves are tested
together. CI fails if the committed fixtures differ from what the addon produces.

To try the addon in the game, link the folder into the client:

```powershell
New-Item -ItemType Junction -Path "C:\Program Files (x86)\World of Warcraft\_forever_\Interface\AddOns\RichForever" -Target "$PWD\addon\RichForever"
```

`/rfor debug` prints the payload; `/reload` reloads changed Lua.

## Companion

```sh
cd companion
dotnet restore --locked-mode
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/RichForever.App
```

Analyzers run at `latest-recommended` with warnings as errors. If you change package references,
run `dotnet restore` (without `--locked-mode`) to update the lock files and commit them.

### Testing the companion without WoW

[`scripts/strip_demo.py`](../scripts/strip_demo.py) shows fixture strips in a topmost window and can
cycle through them to simulate zone changes. Point the companion at it in
`%APPDATA%\RichForever\settings.json`:

```json
{ "GameProcessNames": ["python"], "RequireForeverFolder": false, "VerboseLogging": true }
```

```sh
python scripts/strip_demo.py tests/fixtures/world.ppm tests/fixtures/dungeon.ppm --interval 15
```

With verbose logging the log shows every decoded strip and the presence JSON. Closing the demo window
simulates the game exiting. Remember to remove these settings afterwards.

### Measuring

```powershell
./scripts/measure-companion.ps1 -Seconds 60
```

samples CPU and memory of the running companion. Results are recorded in
[PERFORMANCE.md](PERFORMANCE.md).

## Artwork

```sh
python scripts/build_assets.py
```

downloads official World of Warcraft artwork and rebuilds `companion/assets/discord/*.png` (512×512)
and the credits in `companion/assets/SOURCES.md`. The `ART` table in the script names each key's
source and crop: a WoW Forever client file by file data ID (served by
[wago.tools](https://wago.tools/files), which tracks the `wowf` build) or an image on
[warcraft.wiki.gg](https://warcraft.wiki.gg). To add a zone or instance: add a catalog entry with a new
`key`, `names` and IDs (from `/rfor debug`), add the key to `ART`, run the script, and commit all
three. The script refuses to run while a catalog key has no source, and a test checks that every key
has an image. The app icon (`richforever.ico`) and `docs/logo.png` are the original RichForever
emblem and are not generated.

## Packaging

```powershell
pwsh ./scripts/package.ps1 -Version 0.1.0-beta.3
```

writes the release ZIPs and `SHA256SUMS.txt` to `dist/`.

## Changing the protocol

The strip format is shared by `Protocol.lua` and `StripDecoder.cs`/`PayloadParser.cs`:

- New optional payload fields are backwards compatible: add them to the Lua `FIELDS` list, the
  parser, `GameSnapshot`, the docs table in [ARCHITECTURE.md](ARCHITECTURE.md), and tests.
- Anything else (cell layout, meaning of a field) needs a new protocol version, and the companion
  must keep decoding the old one for a while.

## Conventions

- Conventional commits (`feat(addon): …`, `fix(companion): …`, `ci: …`, `docs: …`).
- Branches: `feature/*`, `fix/*`, `chore/*`, `docs/*`; pull requests into `main`.
- Keep the addon allocation-free on hot paths and event-driven; keep the companion idle when nothing
  changes.
