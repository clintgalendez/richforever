# Troubleshooting

Start with the RichForever window (double-click the tray icon). Its **Status** section says which
part is not working. The log is at `%LOCALAPPDATA%\RichForever\richforever.log` (tray → *Open log
folder*). For more detail set `"VerboseLogging": true` in `%APPDATA%\RichForever\settings.json` and
restart RichForever.

## WoW Forever

| Status shows | Cause and fix |
| --- | --- |
| Waiting for WoW Forever | The game is not running, or its executable is not `Wow.exe`/`WowB.exe` under a `_forever` folder. If you moved the game, check `GameProcessNames` and `RequireForeverFolder` in settings.json. |
| Running - game window hidden or covered | The game is minimised or another window covers its top-left corner. Presence keeps the last known state meanwhile. |
| Running - no RichForever strip | You are at the login/character screen, the addon is disabled or out of date, sharing is off (`/rfor on`), or the coloured block is not on screen. Check *AddOns* on the character screen and run `/rfor status`. |
| Running - strip unreadable (BadChecksum) | Something changes the block's colours: exclusive fullscreen, HDR, a colour-blind filter, an overlay (Discord, NVIDIA, Steam, recording software) drawn over the top-left corner, or extreme gamma/brightness. Use windowed (fullscreen) mode, move overlays elsewhere, reset gamma. |
| Running - strip unreadable (UnsupportedVersion) | The addon is newer than the companion. Update the companion. |
| Last error: Addon data rejected | The addon sent something the companion refused. Update both to the same version and report it with `/rfor debug` output. |

The block appears in the top-left corner and is about 192×12 pixels. It is drawn above the game UI
and stays visible when the UI is hidden with Alt+Z.

## Discord

| Status shows | Cause and fix |
| --- | --- |
| The Discord application ID in settings is not valid | Clear the *Discord app* field in the window to use RichForever's application, or enter a valid ID ([DISCORD_SETUP.md](DISCORD_SETUP.md)). |
| Discord is not running (no discord-ipc pipe found) | Start the Discord desktop app. The browser version cannot show Rich Presence. Discord and RichForever must run as the same Windows user, and neither "as administrator". |
| Discord rejected the application ID | Only possible with a custom ID: it is wrong or the application was deleted. Clear the field to use the default. |
| Discord did not answer | Discord is busy or updating. RichForever retries automatically. |
| Connected, but nothing on my profile | Turn on *User Settings → Activity Privacy → Share your detected activities with others*. Check that the status line says *Presence:* something and is not paused. Discord updates presence at most every few seconds. |
| Text shows but no images | Assets not uploaded yet, uploaded under different names, or still propagating (can take minutes). |
| Shows "In menus" while playing | The strip was unreadable for 20 seconds while the game was visible. See the WoW section. |

## Startup

- *Start with Windows* writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\RichForever`. If you
  move `RichForever.exe`, open RichForever once from the new place so it updates the entry.
- Starting RichForever twice shows "already running"; look for the tray icon (it may be in the
  hidden icons area).

## Updates

The **Updates** line in the window's status shows the last check. Common results:

- *Update check failed*: no internet connection or GitHub unreachable; RichForever tries again in
  6 hours, or use tray icon → **Check for updates…**.
- *Could not install … access denied*: the companion folder is not writable (for example under
  `Program Files`). Move the folder to `%LOCALAPPDATA%\Programs\RichForever`, or download the new
  version from [Releases](https://github.com/clintgalendez/richforever/releases) by hand.
- *Installed; it takes over when WoW Forever closes*: the new version is in place and starts when you
  stop playing.
- Files ending in `.update-old` next to `RichForever.exe` are the previous version; RichForever
  deletes them a minute after the next start.

## Still stuck

Open an issue with the companion status, the last lines of the log and the output of `/rfor debug`
(remove anything you consider private).
