# Third-Party Notices

Game+ (this repository, "Game+", "the project") is licensed under the **GNU General Public License
version 3.0** (SPDX: `GPL-3.0-only`). See [`LICENSE`](LICENSE) for the full text.

Portions of NetchModern are derived from Netch (https://github.com/NetchX/Netch),
Copyright © Netch contributors, GPL-3.0.

This file lists the third-party components that are used by, and (where noted) redistributed with,
the Game+ installer. It is provided to satisfy the attribution and source-availability obligations
of the licences of those components. If you believe a component is missing or mis-attributed, please
open an issue.

> **Licence texts:** the verbatim licence file of every bundled third-party component ships in the
> installer under `resources/licenses/` (source: [`licenses/`](licenses/README.md)). Game+'s own
> GPL-3.0 text is `resources/LICENSE` and is also shown by the installer. See
> [Source code offer](#source-code-offer) for where the corresponding source is available.
>
> **No affiliation:** Game+ is not affiliated with, endorsed by or associated with any of the projects
> listed below. Their names appear here only to identify the components and meet attribution duties.

---

## Bundled native components

These binaries are downloaded from the official release URLs pinned (with SHA-256) in
[`NetchModern/native/native-manifest.json`](NetchModern/native/native-manifest.json) by
`NetchModern/fetch-native.ps1`, staged by `NetchModern/build.ps1` into `resources/backend/bin` and
redistributed inside the installer. The manifest is the source of truth for versions and hashes.

| Component | Used for | Upstream | Licence (SPDX) | Bundled | Notes / obligations |
| --- | --- | --- | --- | --- | --- |
| `xray.exe` (Xray-core **v25.12.8**) | Proxy core: ss / trojan / vmess / vless / socks | https://github.com/XTLS/Xray-core — asset https://github.com/XTLS/Xray-core/releases/download/v25.12.8/Xray-windows-64.zip | `MPL-2.0` | Yes | Unmodified official binary. Source of the exact build: https://github.com/XTLS/Xray-core/tree/v25.12.8. |
| `sing-box.exe` (sing-box **v1.14.3**) | Proxy core: hysteria2, Shadowsocks plugins | https://github.com/SagerNet/sing-box — asset https://github.com/SagerNet/sing-box/releases/download/v1.14.3/sing-box-1.14.3-windows-amd64.zip | `GPL-3.0-or-later` | Yes | Unmodified official binary; source https://github.com/SagerNet/sing-box/tree/v1.14.3. sing-box's licence adds a naming condition — see [sing-box naming condition](#sing-box-naming-condition). |
| `wintun.dll` (WinTUN **0.14.1**, amd64) | TUN adapter driver DLL | https://www.wintun.net/ — asset https://www.wintun.net/builds/wintun-0.14.1.zip (source: https://git.zx2c4.com/wintun) | Prebuilt-binaries licence (redistribution of the **unmodified** DLL permitted); source is `GPL-2.0` / `MIT` | Yes | Unmodified signed DLL (`wintun/bin/amd64/wintun.dll`). Do not modify the DLL. |
| `tun2socks.bin` (Netch **1.9.7**) | TUN-mode traffic capture helper | https://github.com/netchx/netch — official release asset https://github.com/netchx/netch/releases/download/1.9.7/Netch.7z (`Netch/bin/tun2socks.bin`) | `GPL-3.0` | Yes | Unmodified binary from the official Netch 1.9.7 release. Corresponding source: https://github.com/netchx/netch/tree/1.9.7 (Netch's own cgo build; based on xjasonlyu/tun2socks per Netch's bin/README). |
| `RouteHelper.bin` (Netch **1.9.7**) | Route-table helper for TUN mode | https://github.com/netchx/netch — same `Netch.7z` asset (`Netch/bin/RouteHelper.bin`) | `GPL-3.0` | Yes | Unmodified binary from the official Netch 1.9.7 release. Corresponding source: https://github.com/netchx/netch/tree/1.9.7. |
| `Redirector.bin` | Process-mode traffic capture helper (netfilter2) | https://github.com/netchx/netch | `GPL-3.0` | **No** (not fetched) | Only useful together with the NetFilter SDK (`nfapi.dll`), which is not shipped. Process mode is not shipped — see [NetFilter SDK](#netfilter-sdk--process-mode--not-shipped). |
| `nfapi.dll` / `nfdriver.sys` (NetFilter SDK 2) | Process mode (netfilter2) | https://netfiltersdk.com/ | **Commercial** (proprietary) | **No** | Not downloaded and not shipped. **Redistribution requires a valid commercial licence from NetFilterSDK.com.** Kept out of the installer — see [NetFilter SDK](#netfilter-sdk--process-mode--not-shipped). |
| `geoip.dat` (Loyalsoldier/v2ray-rules-dat **202610102207**) | IP-based routing data | https://github.com/Loyalsoldier/v2ray-rules-dat — asset https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610102207/geoip.dat | `GPL-3.0`; the GeoIP data is generated from MaxMind GeoLite2 Country (GeoLite2 EULA, attribution required — see below) | Yes | Unmodified release file. See [MaxMind GeoLite attribution](#maxmind-geolite-attribution). |
| `geosite.dat` (Loyalsoldier/v2ray-rules-dat **202610102207**) | Domain-based routing data | https://github.com/Loyalsoldier/v2ray-rules-dat — asset https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610102207/geosite.dat | `GPL-3.0` (built on v2fly/domain-list-community, `MIT`) | Yes | Unmodified release file. |

## Managed / runtime dependencies

| Component | Used for | Upstream | Licence (SPDX) | Bundled | Notes / obligations |
| --- | --- | --- | --- | --- | --- |
| .NET runtime (self-contained publish) | Runtime for `gameplus-host.exe` | https://github.com/dotnet/runtime | `MIT` | Yes | Bundled via self-contained publish. Include the .NET third-party notices. |
| CommunityToolkit.Mvvm | MVVM source generators / observable models | https://github.com/CommunityToolkit/dotnet | `MIT` | Yes | — |
| Microsoft.Extensions.* (DI, Hosting, Logging, …) | DI container, hosting, logging | https://github.com/dotnet/runtime | `MIT` | Yes | — |

## Frontend / JavaScript dependencies

| Component | Used for | Upstream | Licence (SPDX) | Bundled | Notes / obligations |
| --- | --- | --- | --- | --- | --- |
| Electron | Desktop application shell | https://github.com/electron/electron | `MIT` | Yes | Electron bundles Chromium and other third-party components; their licences ship in Electron's `LICENSES.chromium.html`. Ensure that file is included in the distribution. |
| Vue | UI framework | https://github.com/vuejs/core | `MIT` | Yes | — |
| Pinia | State management | https://github.com/vuejs/pinia | `MIT` | Yes | — |
| vue-router | Routing | https://github.com/vuejs/router | `MIT` | Yes | — |
| @electron-toolkit/preload | Preload helpers | https://github.com/alex8088/electron-toolkit | `MIT` | Yes | — |
| @electron-toolkit/utils | Main-process utilities | https://github.com/alex8088/electron-toolkit | `MIT` | Yes | — |

---

## sing-box naming condition

The sing-box licence, as shipped in the v1.14.3 release zip and at
https://github.com/SagerNet/sing-box/blob/v1.14.3/LICENSE, reads (in full, apart from the GPL
boilerplate quoted in `licenses/sing-box-LICENSE.txt`):

> Copyright (C) 2022 by nekohasekai <contact-sagernet@sekai.icu>
>
> This program is free software: you can redistribute it and/or modify it under the terms of the GNU
> General Public License as published by the Free Software Foundation, either version 3 of the
> License, or (at your option) any later version.
> […]
> In addition, no derivative work may use the name or imply association with this application without
> prior consent.

How Game+ complies (conservative reading — treat Game+ as if it were a "derivative work"):

- Game+ ships the **unmodified** official `sing-box.exe` as a separate program it starts as a child
  process; it does not rename, rebrand or modify it.
- The product name is **Game+**; the installer (`productName: Game+`, `Game+-<version>-x64.exe`), the
  app window, the UI strings and the icons contain **no** "sing-box" name or branding (checked
  `electron-app/src`, `electron-builder.yml`, `package.json`, `build/`).
- "sing-box" appears only where a factual reference is needed to identify the component: this file,
  `licenses/`, the developer READMEs, internal logs/error messages naming the process, and the
  Settings → About disclaimer, which states that Game+ is **not affiliated with, endorsed by or
  associated with** sing-box (or the other bundled projects).
- Nothing in Game+ claims or implies association with or endorsement by sing-box / SagerNet.

## Source code offer

Game+ redistributes the following GPL / MPL licensed programs **unmodified**, as object code built
and published by their upstream projects. Their complete corresponding source is available at these
exact tags / commits:

| Component (shipped file) | Licence | Corresponding source (pinned) | Release asset we redistribute |
| --- | --- | --- | --- |
| sing-box v1.14.3 (`sing-box.exe`) | GPL-3.0-or-later | https://github.com/SagerNet/sing-box/tree/v1.14.3 | https://github.com/SagerNet/sing-box/releases/download/v1.14.3/sing-box-1.14.3-windows-amd64.zip |
| Netch 1.9.7 (`tun2socks.bin`, `RouteHelper.bin`) | GPL-3.0 | https://github.com/netchx/netch/tree/1.9.7 | https://github.com/netchx/netch/releases/download/1.9.7/Netch.7z |
| Loyalsoldier/v2ray-rules-dat 202610102207 (`geoip.dat`, `geosite.dat`) | GPL-3.0 | https://github.com/Loyalsoldier/v2ray-rules-dat/tree/202610102207 (data inputs: https://github.com/Loyalsoldier/geoip, https://github.com/v2fly/domain-list-community) | https://github.com/Loyalsoldier/v2ray-rules-dat/releases/tag/202610102207 |
| Xray-core v25.12.8 (`xray.exe`) | MPL-2.0 | https://github.com/XTLS/Xray-core/tree/v25.12.8 | https://github.com/XTLS/Xray-core/releases/download/v25.12.8/Xray-windows-64.zip |
| WinTUN 0.14.1 (`wintun.dll`) | Prebuilt Binaries License (source GPL-2.0 / MIT) | https://git.zx2c4.com/wintun/tag/?h=0.14.1 | https://www.wintun.net/builds/wintun-0.14.1.zip |

The SHA-256 of every asset and of every shipped file is pinned in
`NetchModern/native/native-manifest.json`, so the shipped binaries can be matched to those releases.

**Written offer.** For at least three years after we last distribute a given Game+ version, and for as
long as we offer spare parts or support for it, we will give anyone who asks a complete
machine-readable copy of the corresponding source code of (a) Game+ itself and (b) each GPL-licensed
component listed above, in the exact version shipped with that Game+ version, for no more than our
reasonable cost of physically performing the transfer (free when sent as a download link). To request
it, open an issue in the Game+ release channel repository (https://github.com/WyattCode/GamePlus-releases/issues)
naming the Game+ version you received. This offer is valid to anyone in possession of the object code.

## MaxMind GeoLite attribution

`geoip.dat` (Loyalsoldier/v2ray-rules-dat 202610102207) is generated from MaxMind GeoLite2 Country data
by https://github.com/Loyalsoldier/geoip (`CC-BY-SA-4.0`). As required by section 3 of the GeoLite End
User License Agreement (https://www.maxmind.com/en/geolite2/eula, "Updated on: February 12, 2026"):

> This product includes GeoLite Data created by MaxMind, available from https://www.maxmind.com.

The same sentence is shown in Settings → About and shipped as
`resources/licenses/MaxMind-GeoLite-ATTRIBUTION.txt`.

## NetFilter SDK / process mode — not shipped

Process mode needs the NetFilter SDK 2 (`nfapi.dll`, `nfdriver.sys`), which is **commercial**
software; redistribution requires a licence from NetFilterSDK.com. **Decision: process mode is kept out
of the installer** — `nfapi.dll`, `nfdriver.sys` and `Redirector.bin` are not in the manifest, are not
downloaded and are not redistributed — until a NetFilter SDK redistribution licence is obtained.

## Open points for a lawyer / the owner

- **GeoLite EULA §6.3 (destroy old versions within 30 days of an update) and §6.1 (disclosure to third
  parties only as the Creative Commons Licence permits; downstream must get "the same or substantially
  similar" duties).** A pinned `geoip.dat` inside an installer goes stale. Mitigation in place: none
  beyond attribution; recommended: bump the pinned rules-dat release at least monthly, or replace the
  only GeoIP rule used (`geoip:private`) with explicit private CIDRs and drop `geoip.dat`.
- **Written offer contact** points to `WyattCode/GamePlus-releases`, which does not exist yet; the Game+
  source repository is currently private, so the offer (or a public source mirror) must be live before
  the first public release.

---

## Attribution

Portions of NetchModern are derived from Netch (https://github.com/NetchX/Netch),
Copyright © Netch contributors, GPL-3.0.
