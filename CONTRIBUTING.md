# Contributing to RichForever

Thanks for helping! Bug reports, zone and instance data, artwork, documentation and code are all
welcome.

## Ground rules

- Be kind; see the [Code of Conduct](CODE_OF_CONDUCT.md).
- Privacy first: anything new shown on Discord must be opt-in or covered by an existing privacy switch.
- Keep it light: no new runtime dependencies without a strong reason, no polling where an event
  exists, no work when nothing changes.
- Never read game memory or send input to the game.

## Reporting bugs

Use the bug report template and include the companion status, the end of the log and `/rfor debug`
output. Report security problems privately (see [SECURITY.md](SECURITY.md)).

## Live-client testing

The most valuable contribution right now is testing in WoW Forever: does the addon load, does the
companion read the strip, are dungeon, raid and battleground names and IDs right? Post `/rfor debug`
output from different places in the live verification issue.

## Making changes

1. Open or pick an issue, and say you are working on it.
2. Branch from `main`: `feature/…`, `fix/…`, `docs/…` or `chore/…`.
3. Follow [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): `luacheck addon`, the addon tests (commit
   regenerated fixtures), `dotnet format`, `dotnet test`.
4. Use [conventional commits](https://www.conventionalcommits.org): `feat(addon): …`,
   `fix(companion): …`, `docs: …`, `ci: …`.
5. Open a pull request using the template and link the issue (`Closes #123`). CI must pass.

## Adding zones and instances

Add the entry to `companion/assets/catalog.json` with the IDs from `/rfor debug`, run
`python scripts/generate_assets.py`, and commit the catalog and the new image. Artwork must be your
own or openly licensed; no Blizzard art.

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
