# Privacy

RichForever is built to keep your data on your PC.

## What is collected

The addon reads only what it needs for presence: character name, level, race, class, faction, sex,
zone, subzone, map ID, instance name/type/ID/difficulty/size, group size, combat/dead/resting/away
flags and the time you logged in. Health, power and experience percentages are read only if you turn
on *Share health, power and experience*.

It does not read your account name, realm, guild, chat, friends, inventory, gold or anything else.

## Where it goes

1. The addon draws the data as coloured pixels in the corner of your game window.
2. The companion reads those pixels from your screen, on your PC.
3. The companion sends the presence to the Discord desktop app through a local named pipe.
4. Discord shows it to people who can see your activity, according to your Discord settings.

Only step 4 leaves your computer, and only the fields shown on your profile. None of your game data
is part of the update check below.

Note that the coloured block is part of your screen, so screenshots and streams include it. It
encodes the same information that the presence shows, in a form that is easy to decode.

## What is never done

- No telemetry, analytics or crash reporting. No servers of RichForever's own, no accounts.
- No Discord password, token or OAuth login; only a public application ID is used. The presence is
  published under RichForever's Discord application, which only determines the name and artwork Discord
  displays; the application owner receives no data from it.
- No reading of game memory, other windows (the companion reads the game's corner only when the game
  window is the topmost window there) or files outside its own settings and log.
- Local file paths and process details are never sent to Discord.

## Update checks

With *Install updates automatically* on (the default), the companion asks GitHub for the list of
RichForever releases a minute after it starts and every 6 hours
(`https://api.github.com/repos/clintgalendez/richforever/releases`), and downloads a newer release
from `github.com`. That request carries your IP address and a `RichForever/<version>` user agent,
like any download from GitHub, and nothing else: no identifiers, settings or game data. GitHub's
[privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)
applies. Turn the setting off and the companion makes no network requests unless you choose
*Check for updates…*.

## Your controls

| Control | Effect |
| --- | --- |
| `/rfor off` (in game) | The addon stops drawing; nothing reaches the companion |
| `/rfor name off` (in game) | Your name never leaves the game |
| `/rfor stats off` (default) | Health, power and XP are not read |
| Pause presence | Discord presence is cleared |
| Hide character name | Name not shown on Discord |
| Hide session time | No elapsed timer |
| Hide zone and map | Shows "Exploring the world" and generic art |
| Hide dungeon and raid details | Shows "Inside an instance" and generic art |
| Show health, power and XP | Off by default |
| Show group size / combat | Group size on by default, combat off |
| Install updates automatically | On by default; off means no network requests of RichForever's own |

## Stored data

| Location | Content |
| --- | --- |
| `%APPDATA%\RichForever\settings.json` | Your settings |
| `%LOCALAPPDATA%\RichForever\richforever.log` | Status changes and errors, at most 2 × 1 MB. With verbose logging it also contains the presence text. |
| WoW `SavedVariables\RichForever.lua` | Addon settings and the session start time |

`RichForever.exe --uninstall` removes the first two and the startup entry.
