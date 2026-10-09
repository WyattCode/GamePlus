# Game+

Game+ is a **Windows game accelerator**: it routes a game's (or any app's) traffic through a proxy
server you configure, using the bundled **Xray** / **sing-box** cores, with process-level, TUN or
system-proxy capture.

![Game+ dashboard](NetchModern/docs/screenshot.png)

## Repository layout

```
.
├── electron-app/     # Electron + Vue 3 + TypeScript frontend (the only UI)
├── NetchModern/      # .NET 10 backend (Netch.Core + Netch.Host → gameplus-host.exe)
│   ├── src/          #   Netch.Core (domain/services) + Netch.Host (headless JSON-RPC server)
│   ├── native/       #   native binaries (git-ignored; see native/README.md)
│   └── build.ps1     #   publishes + stages the backend for electron-builder
├── LICENSE           # GPL-3.0-only
└── THIRD_PARTY_NOTICES.md
```

The Electron main process spawns `gameplus-host.exe` as a sidecar and talks to it over a loopback
WebSocket (JSON-RPC, SHA-256 token handshake). See [`NetchModern/README.md`](NetchModern/README.md)
for the backend architecture and [`electron-app/README.md`](electron-app/README.md) for the frontend.

## Quick start (development)

Requirements: Windows 10 build 17763+ / Windows 11, [Node.js](https://nodejs.org) **22.12 or newer**
(with npm) and the [.NET 10 SDK](https://dotnet.microsoft.com).

```powershell
# 1) Backend
cd NetchModern
dotnet build NetchModern.slnx
dotnet test  NetchModern.slnx      # unit tests (also run on Linux)

# 2) Frontend
cd ../electron-app
npm ci
npm run dev                        # electron-vite dev (Vite HMR + Electron)
```

`npm run dev` spawns the debug backend from
`NetchModern/src/Netch.Host/bin/Debug/net10.0-windows/gameplus-host.exe`; build it first if it is
missing.

## Release build

The distributable is an NSIS installer produced in two stages — stage 1 stages the backend, stage 2
packages it:

```powershell
cd NetchModern
.\build.ps1                        # → electron-app/resources/backend

cd ../electron-app
npm run dist                       # → dist/Game+-<version>-<arch>.exe
```

The full release flow (code signing, publishing, update checks) is documented in
[`electron-app/README.md`](electron-app/README.md).

## Native binaries

The proxy cores and capture helpers are **not** committed to this repository. Populate
`NetchModern/native/` before packaging — `build.ps1` verifies the required components and fails the
build if any are missing (`-AllowMissingNative` downgrades that to a warning for dev/CI packaging
tests). See [`NetchModern/native/README.md`](NetchModern/native/README.md) for the component list,
licences and download URLs.

## License

Game+ is licensed under the **GNU General Public License v3.0** (`GPL-3.0-only`). See
[`LICENSE`](LICENSE) and [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for the full licence text
and third-party attributions.
