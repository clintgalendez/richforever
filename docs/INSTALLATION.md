# Installation

RichForever has two pieces: an **addon** inside WoW Forever and a **companion** app on Windows. You
need both. No Discord account setup is needed.

## 1. Download

From the [latest release](https://github.com/clintgalendez/richforever/releases) download:

| File | What it is |
| --- | --- |
| `RichForever-addon-<version>.zip` | The WoW Forever addon |
| `RichForever-<version>-win-x64.zip` | The companion, includes everything it needs (~42 MB) |
| `RichForever-<version>-win-x64-framework-dependent.zip` | Same companion, tiny, needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| `RichForever-discord-assets-<version>.zip` | Artwork, only if you set up your own Discord application |
| `SHA256SUMS.txt` | Checksums |

Optional: verify a download in PowerShell and compare with `SHA256SUMS.txt`:

```powershell
Get-FileHash .\RichForever-0.1.0-beta.3-win-x64.zip -Algorithm SHA256
```

or with the GitHub CLI: `gh attestation verify RichForever-0.1.0-beta.3-win-x64.zip --repo clintgalendez/richforever`.

## 2. Install the addon

The addon is also on [CurseForge](https://www.curseforge.com/wow/addons/richforever): install it
with the CurseForge app (or another addon manager) and skip to step 3. By hand:

1. Open your WoW Forever folder, usually `C:\Program Files (x86)\World of Warcraft\_forever_`.
2. Open `Interface\AddOns` (create `AddOns` if it does not exist).
3. Extract the addon ZIP there. You should end up with
   `Interface\AddOns\RichForever\RichForever.toc`.
4. In the game's character screen, click **AddOns** and make sure **RichForever** is enabled.

## 3. Install the companion

1. Extract the companion ZIP to a folder of your choice, for example
   `%LOCALAPPDATA%\Programs\RichForever`. No installer and no administrator rights are needed.
   Use a folder you can write to (not `Program Files`): updates replace the files in it.
2. Run `RichForever.exe`. If Windows SmartScreen says it protected your PC, the build is simply not
   code-signed yet: click **More info → Run anyway** (after verifying the checksum if you like).
3. A tray icon appears and, on first start, the RichForever window opens.

## 4. Configure Discord

1. Run the Discord desktop app (the browser version cannot show Rich Presence).
2. In Discord: **User Settings → Activity Privacy → Share your detected activities with others** must
   be on.
3. That is all. RichForever uses its own Discord application, so your profile shows
   *Playing World of Warcraft Forever* with RichForever's artwork. To use your own application name or
   art instead, see [DISCORD_SETUP.md](DISCORD_SETUP.md).

## 5. Launch WoW Forever

1. Use **Windowed** or **Windowed (Fullscreen)** display mode (*Options → Graphics → Display Mode*).
   Exclusive fullscreen may hide the screen contents from other programs.
2. Log in to a character. A small block of coloured squares appears in the top-left corner; this is
   how the addon talks to the companion. Keep it uncovered.

## 6. Verify

- The RichForever window shows **WoW Forever: Running - receiving data** and your character.
- **Discord** shows **Connected**.
- Your Discord profile shows *Playing World of Warcraft Forever* with your character and zone.
- In game, `/rfor status` reports what the addon is sharing.

Something wrong? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## 7. Startup behaviour

In the RichForever window, under **Startup**:

- **Start RichForever when Windows starts**: starts silently in the tray at login and waits for the
  game. Uses about 13 MB of memory and practically no CPU while waiting.
- **Connect to Discord only while WoW Forever runs** (default) or **as soon as RichForever starts**.
- **Exit RichForever when WoW Forever closes**.

Under **Updates**, **Install updates automatically** (default) checks GitHub Releases a minute after
start and every 6 hours. A newer version is downloaded, checked against the release's
`SHA256SUMS.txt` and installed; RichForever then restarts, or waits until WoW Forever closes if you
are playing. Turn it off to update by hand, or use tray icon → **Check for updates…** at any time.
The **Updates** line in the window's status shows the last result.

## 8. Turn presence off

- Temporarily: tray icon → **Pause presence**, or `/rfor off` in game.
- Hide details: use the **Privacy** switches in the RichForever window, or `/rfor name off` in game.

## 9. Uninstall

1. Right-click the tray icon → **Exit**.
2. Run `RichForever.exe --uninstall`. It removes the Windows startup entry and the settings and log
   folders (`%APPDATA%\RichForever`, `%LOCALAPPDATA%\RichForever`) and tells you what it removed.
3. Delete the companion folder.
4. Delete `Interface\AddOns\RichForever`. Optionally delete `RichForever.lua` from
   `WTF\Account\<account>\SavedVariables` and `WTF\Account\<account>\<realm>\<character>\SavedVariables`.
5. Optionally delete your Discord application in the developer portal.
