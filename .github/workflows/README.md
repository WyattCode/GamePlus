# CI workflows

## `build.yml`

Runs on every push to any branch, on pull requests, on `v*` tags and on manual dispatch.
One run per ref; runs on non-tag refs are cancelled when a newer commit arrives, tag runs are never
cancelled. The workflow only uses the default `GITHUB_TOKEN` (no other secrets) and requests
`contents: read` by default.

### Job `build` (windows-latest)

1. Checks out the repo, installs .NET 10 and Node.js 22 (npm cache keyed on
   `electron-app/package-lock.json`).
2. Restores, builds and tests `NetchModern/NetchModern.slnx` in Release.
3. In `electron-app`: `npm ci`, `npm run typecheck`, `npx vitest run`.
4. Checks the native proxy cores, then runs `NetchModern/build.ps1 -AllowMissingNative` to publish the
   backend into `electron-app/resources/backend`.
5. Builds the frontend (`npm run build`) and packages the NSIS installer with
   `npx electron-builder --win nsis --publish never`. The installer is **unsigned**
   (`CSC_IDENTITY_AUTO_DISCOVERY=false`, `CSC_LINK` unset), so Windows SmartScreen warns on first run.
6. Writes a `<installer>.exe.sha256` file next to each installer in `sha256sum` format
   (`<lowercase hex>  <file name>`) and prints the hashes to the job summary.
7. Uploads the artifact `GamePlus-installer-<sha>` (or `GamePlus-installer-<tag>` on tags) containing
   the installer(s) and their `.sha256` files, retained for 14 days.

### Native binaries

The native components (`xray.exe`, `sing-box.exe`, `geoip.dat`, `geosite.dat`, `wintun.dll`,
`tun2socks.bin`, `RouteHelper.bin`) are **not committed** to the repository (TODO(owner)). The
required list is read from `NetchModern/build.ps1` itself, so it stays in sync.

* On a normal push or pull request a missing native set does **not** fail the job. A `::warning::` is
  emitted and the resulting installer is an incomplete dev/CI package that cannot accelerate.
  Its artifact name gets the `-incomplete` suffix.
* On a `v*` tag a missing native set **fails** the job with a `::error::`, because a release must be
  complete. Set the repository variable `ALLOW_INCOMPLETE_RELEASE=true` to override (not recommended).

### Job `release` (ubuntu-latest, tags only)

Runs only for `v*` tags, after `build` succeeded, with `permissions: contents: write`. It downloads
the installer artifact and creates a **draft** release with the `gh` CLI. The release notes state that
the installer is unsigned (SmartScreen: 更多信息 → 仍要运行) and list the SHA-256 values. If a release
for the tag already exists, the assets are replaced with `gh release upload --clobber` instead.

The release is **never published automatically** — always `--draft`. A human reviews the notes and
checksums and publishes it manually.
