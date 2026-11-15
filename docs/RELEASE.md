# Releasing

Releases are built and published by [`.github/workflows/release.yml`](../.github/workflows/release.yml)
when a `v*` tag is pushed. Versions follow [Semantic Versioning](https://semver.org). Use prerelease
versions (`0.2.0-beta.1`) until the project is ready for a stable release; anything with a `-` is
published as a GitHub prerelease. Do not tag `v1.0.0` before the addon has been verified in the live
WoW Forever client and has had user testing.

## Steps

1. Update the version in both places:
   - `addon/RichForever/RichForever.toc` → `## Version: X.Y.Z`
   - `companion/Directory.Build.props` → `<Version>X.Y.Z</Version>`
2. Add a `## [X.Y.Z] - YYYY-MM-DD` section to [CHANGELOG.md](../CHANGELOG.md). Its text becomes the
   release notes.
3. Merge to `main` through a pull request with green CI.
4. Tag and push:

   ```sh
   git tag -a vX.Y.Z -m "RichForever vX.Y.Z"
   git push origin vX.Y.Z
   ```

## What the workflow does

1. Checks the tag is `v<semver>` and matches the TOC version, the companion version and a CHANGELOG
   section.
2. Runs luacheck and the addon tests (Ubuntu) and the companion build and tests (Windows).
3. Runs [`scripts/package.ps1`](../scripts/package.ps1): addon ZIP, self-contained and
   framework-dependent companion ZIPs, Discord artwork ZIP, `SHA256SUMS.txt`.
4. Creates build provenance attestations for the ZIPs with `actions/attest`.
5. Publishes the GitHub release with the CHANGELOG notes.

The asset names are part of the update contract: installed companions look for a `v<semver>` tag
with `RichForever-<version>-win-x64.zip` or `RichForever-<version>-win-x64-framework-dependent.zip`
plus `SHA256SUMS.txt`, and expect each ZIP to contain `RichForever.exe` at the top level. Renaming
them stops existing installs from updating.

Only the final publish job has write permissions (`contents`, `id-token`, `attestations`,
`artifact-metadata`).

## Verifying a release

```sh
sha256sum -c SHA256SUMS.txt
gh attestation verify RichForever-X.Y.Z-win-x64.zip --repo clintgalendez/richforever
```

Builds are not claimed to be bit-for-bit reproducible.
