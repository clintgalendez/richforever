# Security model

How to report a vulnerability: see [SECURITY.md](../SECURITY.md) in the repository root.

## Trust boundaries

```
WoW client ──(screen pixels, one-way)──► companion ──(local named pipe)──► Discord desktop app
                                              │
                                    settings.json, assets.json (user files)
```

| Boundary | Risk | Mitigation |
| --- | --- | --- |
| Pixels on screen → companion | Malformed or hostile data in the strip | Read only while the WoW window is the topmost window at both ends of the strip region, so another program cannot place a fake strip there without covering the game. Calibration, magic cells, version, length limit (468 bytes) and Fletcher-16 checksum. Strict UTF-8, no control characters, unique keys, per-field type and range checks. Data is only displayed; nothing from the strip is executed, used as a path, or used as a URL. |
| Companion → Discord | Leaking more than intended | Only the activity object is sent; privacy settings applied before building it. Text is length-limited. Buttons accept https URLs only. |
| Discord pipe | A local process pretending to be Discord | Only processes running as the same Windows user can create that pipe; such a process already has the user's rights. Frames are size-limited (64 KB) and parsed as JSON only. |
| Settings and catalog files | Edited or corrupt files | Parsed with System.Text.Json, invalid values clamped or dropped, corrupt files ignored with a logged warning. Image values must be asset keys or https URLs. |
| Startup | Persistence | Single `HKCU\…\Run` value pointing at the executable with `--startup`. No services, scheduled tasks, drivers or admin rights. Removed when the setting is turned off or by `--uninstall`. |

There is no network listener, no inbound connection of any kind, and no outbound network traffic
other than the local Discord pipe.

## Game safety

The companion never reads or writes game memory, never injects code, and never sends keyboard or
mouse input to the game. It reads screen pixels, the same way screen recorders do. The addon uses only
the public addon API.

## Supply chain and releases

- Two runtime dependencies: the .NET runtime and Windows. Test-only packages: xUnit and the .NET test
  SDK. All versions locked with `packages.lock.json`; NuGet vulnerability audit runs on every restore.
- GitHub Actions: read-only default token, actions pinned to commit SHAs, Dependabot updates,
  actionlint and zizmor checks, CodeQL for C# and workflows, dependency review on pull requests.
- Releases are built by GitHub Actions from a tag, with SHA-256 checksums and
  [build provenance attestations](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations).
  Verify with `gh attestation verify <file> --repo clintgalendez/richforever`.
- Binaries are not code-signed yet, so Windows SmartScreen may warn on first run.
