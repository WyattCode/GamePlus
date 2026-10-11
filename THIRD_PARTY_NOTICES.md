# Third-Party Notices

Game+ (this repository, "Game+", "the project") is licensed under the **GNU General Public License
version 3.0** (SPDX: `GPL-3.0-only`). See [`LICENSE`](LICENSE) for the full text.

Portions of NetchModern are derived from Netch (https://github.com/NetchX/Netch),
Copyright © Netch contributors, GPL-3.0.

This file lists the third-party components that are used by, and (where noted) redistributed with,
the Game+ installer. It is provided to satisfy the attribution and source-availability obligations
of the licences of those components. If you believe a component is missing or mis-attributed, please
open an issue.

> **Note on GPL-3.0 components:** the Game+ installer bundles GPL-3.0 software (sing-box, the Netch
> native helpers, and — if applicable — the geo datasets). The complete corresponding source of the
> Game+ project itself is available at the repository this file ships with. Corresponding source for
> the bundled GPL components is available from their upstream projects (links below); where a
> component is a modified build, the exact source used must be recorded (see the Unresolved section).

---

## Bundled native components

These binaries are downloaded from the official release URLs pinned (with SHA-256) in
[`NetchModern/native/native-manifest.json`](NetchModern/native/native-manifest.json) by
`NetchModern/fetch-native.ps1`, staged by `NetchModern/build.ps1` into `resources/backend/bin` and
redistributed inside the installer. The manifest is the source of truth for versions and hashes.

| Component | Used for | Upstream | Licence (SPDX) | Bundled | Notes / obligations |
| --- | --- | --- | --- | --- | --- |
| `xray.exe` (Xray-core **v25.12.8**) | Proxy core: ss / trojan / vmess / vless / socks | https://github.com/XTLS/Xray-core — asset https://github.com/XTLS/Xray-core/releases/download/v25.12.8/Xray-windows-64.zip | `MPL-2.0` | Yes | Unmodified official binary. Source of the exact build: https://github.com/XTLS/Xray-core/tree/v25.12.8. |
| `sing-box.exe` (sing-box **v1.14.3**) | Proxy core: hysteria2, Shadowsocks plugins | https://github.com/SagerNet/sing-box — asset https://github.com/SagerNet/sing-box/releases/download/v1.14.3/sing-box-1.14.3-windows-amd64.zip | `GPL-3.0-or-later` | Yes | Unmodified official binary; source https://github.com/SagerNet/sing-box/tree/v1.14.3. sing-box has **additional terms / naming conditions** in its licence — `TODO(owner): confirm they are satisfied.` |
| `wintun.dll` (WinTUN **0.14.1**, amd64) | TUN adapter driver DLL | https://www.wintun.net/ — asset https://www.wintun.net/builds/wintun-0.14.1.zip (source: https://git.zx2c4.com/wintun) | Prebuilt-binaries licence (redistribution of the **unmodified** DLL permitted); source is `GPL-2.0` / `MIT` | Yes | Unmodified signed DLL (`wintun/bin/amd64/wintun.dll`). Do not modify the DLL. |
| `tun2socks.bin` (Netch **1.9.7**) | TUN-mode traffic capture helper | https://github.com/netchx/netch — official release asset https://github.com/netchx/netch/releases/download/1.9.7/Netch.7z (`Netch/bin/tun2socks.bin`) | `GPL-3.0` | Yes | Unmodified binary from the official Netch 1.9.7 release. Corresponding source: https://github.com/netchx/netch/tree/1.9.7 (Netch's own cgo build; based on xjasonlyu/tun2socks per Netch's bin/README). |
| `RouteHelper.bin` (Netch **1.9.7**) | Route-table helper for TUN mode | https://github.com/netchx/netch — same `Netch.7z` asset (`Netch/bin/RouteHelper.bin`) | `GPL-3.0` | Yes | Unmodified binary from the official Netch 1.9.7 release. Corresponding source: https://github.com/netchx/netch/tree/1.9.7. |
| `Redirector.bin` | Process-mode traffic capture helper (netfilter2) | https://github.com/netchx/netch | `GPL-3.0` | **No** (not fetched) | Only useful together with the NetFilter SDK (`nfapi.dll`), which is not shipped. Process mode stays optional / missing — see Unresolved. |
| `nfapi.dll` / `nfdriver.sys` (NetFilter SDK 2) | Process mode (netfilter2) | https://netfiltersdk.com/ | **Commercial** (proprietary) | **No** | Not downloaded and not shipped. **Redistribution requires a valid commercial licence from NetFilterSDK.com.** See Unresolved. |
| `geoip.dat` (Loyalsoldier/v2ray-rules-dat **202610102207**) | IP-based routing data | https://github.com/Loyalsoldier/v2ray-rules-dat — asset https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610102207/geoip.dat | `GPL-3.0`; the GeoIP data is generated from MaxMind GeoLite2 Country (GeoLite2 EULA, attribution: "This product includes GeoLite2 data created by MaxMind, available from https://www.maxmind.com") | Yes | Unmodified release file. `TODO(owner): confirm the GeoLite2 attribution above satisfies the GeoLite2 EULA for redistribution.` |
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

## ⚠ Unresolved / owner must verify

The following items are **not yet resolved** and must be confirmed by the project owner before
distribution.

1. **NetFilter SDK 2 commercial licence (`nfapi.dll`, `nfdriver.sys`, hence `Redirector.bin`).**
   Process mode depends on the NetFilter SDK 2 kernel driver, which is **commercial** software.
   Redistribution requires a valid licence from NetFilterSDK.com. Netch's own builds may have been
   signed under their licence — this does **not** grant Game+ the right to redistribute. These files
   are therefore **not** in the manifest and are not fetched; process mode stays optional/missing.
   `TODO(owner): decide — obtain a NetFilter SDK redistribution licence, or keep process mode out of the installer.`

2. **sing-box licence terms.**
   `TODO(owner): confirm sing-box's additional licence terms / naming conditions are satisfied.`

3. **GPL source offer for bundled GPL binaries** (sing-box, Netch helpers, geo datasets): the exact
   upstream tags are linked above. `TODO(owner): confirm linking the upstream tags is sufficient for your distribution, or mirror the sources.`

---

## Attribution

Portions of NetchModern are derived from Netch (https://github.com/NetchX/Netch),
Copyright © Netch contributors, GPL-3.0.
