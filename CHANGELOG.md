# Changelog

All notable changes are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- The Discord artwork is now official World of Warcraft art instead of generated cards: the WoW
  Forever logo while in menus, WoW Forever and classic loading screens for instances, battlegrounds,
  Zephras Isle and activity badges, the game's own class icons, and official art and screenshots for
  every zone. `scripts/build_assets.py` replaces `scripts/generate_assets.py` and credits each image in
  `companion/assets/SOURCES.md`.

## [0.1.0-beta.3] - 2026-11-16

The companion now keeps itself up to date. Coming from beta.1 or beta.2, download this version by
hand once; later versions install themselves.

### Added

- Automatic updates for the companion (#32): it checks GitHub Releases a minute after start and
  every 6 hours, verifies the download against `SHA256SUMS.txt`, replaces its files and restarts,
  waiting until WoW Forever closes if you are playing. *Install updates automatically* (on by default)
  turns it off; tray → *Check for updates…* checks on demand. Prerelease builds also receive
  prereleases. This is RichForever's first network request of its own; see
  [docs/PRIVACY.md](https://github.com/clintgalendez/richforever/blob/main/docs/PRIVACY.md#update-checks).

## [0.1.0-beta.2] - 2026-11-16

No Discord setup needed any more.

### Changed

- RichForever now uses its own Discord application, "World of Warcraft Forever"
  (`1571656707078103191`), with all artwork uploaded. No Discord setup is needed; a custom
  application ID is optional (#18).
- The window opens once on first start instead of asking for an application ID.

### Fixed

- `SHA256SUMS.txt` is written with LF line endings so `sha256sum -c` works (#28).

## [0.1.0-beta.1] - 2026-11-15

First public beta. The companion and protocol are tested on Windows and against the real Discord
client; the addon still needs verification in the live WoW Forever client.

### Added

- WoW Forever addon (interface 16001): character, zone, map, instance, group, status flags and session
  start, drawn as a checksummed pixel strip; secret-value safe; slash commands and options panel;
  sharing, name and stats switches.
- Windows companion (.NET 10): tray app with status and settings window, waiting mode, game detection,
  strip capture and validation, Discord Rich Presence over the local IPC pipe with rate limiting,
  reconnect and clear-on-exit.
- Presence for open world, dungeons, raids, battlegrounds, arenas and unknown instances; resting,
  dead, away and optional combat states; group size; optional health, power and XP.
- Privacy settings: pause, hide name, hide session time, hide zone, hide instance details.
- Startup options: start with Windows (per user), connect to Discord immediately or only while
  playing, exit when the game closes; `--uninstall` cleanup.
- Original artwork: logo, 9 class icons, 4 activity badges, 49 zone and 29 instance cards, with an
  asset catalog and user override file.
- Tests: Lua addon tests with a WoW API mock; C# unit, integration (addon → pixels → presence),
  Discord IPC (fake pipe server), real screen capture and performance tests.
- CI (Lua lint and tests, .NET format/build/test/package, link check, workflow security, dependency
  review), CodeQL, Dependabot, and a tag-driven release workflow with checksums and provenance
  attestations.

[Unreleased]: https://github.com/clintgalendez/richforever/compare/v0.1.0-beta.3...HEAD
[0.1.0-beta.3]: https://github.com/clintgalendez/richforever/compare/v0.1.0-beta.2...v0.1.0-beta.3
[0.1.0-beta.2]: https://github.com/clintgalendez/richforever/compare/v0.1.0-beta.1...v0.1.0-beta.2
[0.1.0-beta.1]: https://github.com/clintgalendez/richforever/releases/tag/v0.1.0-beta.1
