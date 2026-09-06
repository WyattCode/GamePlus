# Game+ — Desktop

The Electron frontend for Game+, a game accelerator. It drives the headless .NET backend
([`NetchModern`](../NetchModern)) over a local WebSocket (JSON-RPC): the main process spawns
`gameplus-host.exe` as a sidecar, reads its `GAMEPLUS_READY {port,token}` handshake, and the Vue
renderer talks to it. Everything is bundled into a self-contained NSIS **installer** — users never
need a .NET runtime.

```
electron-app/
├── src/main/        # Electron main: window, tray, single-instance, sidecar lifecycle
├── src/preload/     # contextBridge (window controls, theme, backend connection)
├── src/renderer/    # Vue 3 + Pinia UI (Accelerate / Games / Servers / Subscriptions / Logs / Settings)
└── src/shared/      # TS model types mirroring the C# Netch.Core DTOs
```

## Requirements

- Windows 10 build 17763+ / Windows 11
- [Node.js](https://nodejs.org) (with npm) and the [.NET 10 SDK](https://dotnet.microsoft.com)
  (the latter only builds the backend; the packaged app needs no .NET runtime)

## Dev

```powershell
cd electron-app
npm install
npm run dev        # electron-vite dev (Vite HMR + Electron)
```

`npm run dev` spawns the debug backend from
`NetchModern/src/Netch.Host/bin/Debug/net10.0-windows/gameplus-host.exe` (see `src/main/sidecar.ts`).
If that exe is missing, build it first: `dotnet build ../NetchModern/NetchModern.slnx`.

## Typecheck / build

```powershell
npm run typecheck   # tsc (node) + vue-tsc (web)
npm run build       # electron-vite build → out/
```

## Release — installer

The distributable is an NSIS installer produced in two stages. **Run stage 1 first** — it stages
the self-contained backend that stage 2 packages.

```powershell
# 1) Backend → electron-app/resources/backend
cd ..\NetchModern
.\build.ps1                       # self-contained win-x64 (version read from ../electron-app/package.json)

# 2) Installer (electron-vite build + electron-builder --win)
cd ..\electron-app
npm run dist                      # → dist/Game+-<version>-<arch>.exe
```

`build.ps1` publishes `gameplus-host.exe` **self-contained** (including the .NET runtime), copies the
native cores (`xray.exe`, `sing-box.exe`, `wintun.dll`, … from `NetchModern/native/`) into
`resources/backend/bin`, and clears the staging dir first so no stale binaries leak into the package.
It reads the version from `electron-app/package.json`, the single source of truth.

### Code signing

Also in `electron-builder.yml` (`win` section): signing is wired to the standard electron-builder
env vars, so set them before `npm run dist` to avoid a SmartScreen "unknown publisher" warning:

```powershell
$env:CSC_LINK = 'C:\path\to\cert.pfx'        # or base64 of the cert
$env:CSC_KEY_PASSWORD = '<password>'
npm run dist
```

Leave them unset to skip signing.

### Publish to the repo

The project lives at **https://github.com/WyattCode/GamePlus** (private). After a release:

```powershell
git add -A
git commit -m "release: Game+ <version>"
git push
```

The built-in update check (in `Netch.Host`) compares the installed version against the latest GitHub
release tag of `WyattCode/GamePlus`, so create a GitHub **release** with a tag like `v3.0.0` for it to
be picked up.
