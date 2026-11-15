# RichForever companion

The Windows half of [RichForever](../README.md): a .NET 10 tray app that reads the addon's pixel
strip and keeps your Discord Rich Presence in sync.

| Project | Contents |
| --- | --- |
| `src/RichForever.Core` | Strip decoder, payload parser, presence builder, asset catalog, settings, Discord IPC client and sync loop. No Windows dependencies. |
| `src/RichForever.App` | Tray icon, status and settings window, game detection, GDI screen capture, startup registration, logging. Builds `RichForever.exe`. |
| `tests/RichForever.Tests` | xUnit tests, including integration tests that decode the addon's fixture images. |
| `assets/` | `catalog.json` and the Discord artwork. |

```sh
dotnet test -c Release
dotnet run --project src/RichForever.App
```

Command-line options: `--startup` (start silently in the tray; used by the Windows startup entry),
`--uninstall [--quiet]` (remove the startup entry, settings and logs).

See [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md) and [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).
