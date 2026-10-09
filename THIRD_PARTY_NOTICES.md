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

These binaries are staged by `NetchModern/build.ps1` from `NetchModern/native/` into
`resources/backend/bin` and are redistributed inside the installer.

| Component | Used for | Upstream | Licence (SPDX) | Bundled | Notes / obligations |
| --- | --- | --- | --- | --- | --- |
| `xray.exe` (Xray-core) | Proxy core: ss / trojan / vmess / vless / socks | https://github.com/XTLS/Xray-core | `MPL-2.0` | Yes | MPL-2.0 is file-level copyleft. We redistribute an **unmodified** binary. Obligation: make the source of the exact build available — link to the exact upstream release tag used. `TODO(owner): record the Xray-core version/tag and the download URL of the exact release asset shipped.` |
| `sing-box.exe` (sing-box) | Proxy core: hysteria2 | https://github.com/SagerNet/sing-box | `GPL-3.0-or-later` | Yes | GPL-3.0-or-later. Obligation: provide corresponding source (upstream link) and preserve notices. sing-box has **additional terms / naming conditions** in its licence — review them before redistribution. `TODO(owner): record the sing-box version/tag shipped and confirm the additional licence terms are satisfied.` |
| `wintun.dll` (WinTUN) | TUN adapter driver DLL | https://www.wintun.net/ (source: https://git.zx2c4.com/wintun) | Prebuilt-binaries licence (redistribution of the **unmodified** DLL permitted); source is `GPL-2.0` / `MIT` | Yes | We redistribute the **unmodified** prebuilt DLL, which the WinTUN prebuilt-binaries licence permits. Do not modify the DLL. `TODO(owner): record the WinTUN version shipped.` |
| `tun2socks.bin` | TUN-mode traffic capture helper | `TODO(owner): verify exact upstream repo and version.` | `GPL-3.0` (per Netch project) | Yes | Native helper from the Netch project. Obligation: provide corresponding source. `TODO(owner): confirm the exact upstream repository, commit/tag and build provenance of this binary.` |
| `RouteHelper.bin` | Route-table helper for TUN mode | `TODO(owner): verify exact upstream repo and version.` | `GPL-3.0` (per Netch project) | Yes | Native helper from the Netch project. Obligation: provide corresponding source. `TODO(owner): confirm the exact upstream repository, commit/tag and build provenance of this binary.` |
| `Redirector.bin` | Process-mode traffic capture helper (netfilter2) | `TODO(owner): verify exact upstream repo and version.` | `GPL-3.0` (per Netch project) | Yes | Native helper from the Netch project. Obligation: provide corresponding source. `TODO(owner): confirm the exact upstream repository, commit/tag and build provenance of this binary.` |
| `nfdriver.sys` (NetFilter SDK 2 driver) | Kernel driver used by Process mode (netfilter2) | https://netfiltersdk.com/ | **Commercial** (proprietary) | Yes | **Redistribution requires a valid commercial licence from NetFilterSDK.com.** See the Unresolved section — this must be verified by the owner before distribution. |
| `geoip.dat` | IP-based routing data | `TODO(owner): record the actual dataset source.` | `TODO(owner): depends on source — see Unresolved.` | Yes | `TODO(owner): record which dataset is actually shipped (e.g. v2fly/geoip — CC-BY-SA-4.0; Loyalsoldier/v2ray-rules-dat — GPL-3.0) and its version/date.` |
| `geosite.dat` | Domain-based routing data | `TODO(owner): record the actual dataset source.` | `TODO(owner): depends on source — see Unresolved.` | Yes | `TODO(owner): record which dataset is actually shipped (e.g. v2fly/domain-list-community — MIT; Loyalsoldier/v2ray-rules-dat — GPL-3.0) and its version/date.` |

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
distribution. Do not ship a release until each is either resolved or removed from the bundle.

1. **NetFilter SDK 2 commercial licence (`nfdriver.sys`, Redirector / nfapi).**
   Process mode depends on the NetFilter SDK 2 kernel driver, which is **commercial** software.
   Redistribution of `nfdriver.sys` (and the associated netfilter API) requires a valid licence from
   NetFilterSDK.com. Netch's own builds may have been signed under their licence — this does **not**
   automatically grant Game+ the right to redistribute.
   `TODO(owner): confirm a valid NetFilter SDK redistribution licence is held, or remove nfdriver.sys / Process mode from the installer.`

2. **Exact upstream + version of the Netch native helpers.**
   `tun2socks.bin`, `RouteHelper.bin`, `Redirector.bin` are native helpers from the Netch project.
   `TODO(owner): record the exact upstream repository, commit/tag and build provenance for each binary.`

3. **Which `geoip.dat` / `geosite.dat` datasets are shipped.**
   `TODO(owner): record the actual dataset source and version (v2fly/geoip — CC-BY-SA-4.0; v2fly/domain-list-community — MIT; Loyalsoldier/v2ray-rules-dat — GPL-3.0) and include the required attribution.`

4. **sing-box version and licence terms.**
   `TODO(owner): record the sing-box version shipped and confirm its additional licence terms / naming conditions are satisfied.`

5. **Xray-core version.**
   `TODO(owner): record the Xray-core version/tag shipped and link to the exact release asset source.`

6. **WinTUN version.**
   `TODO(owner): record the WinTUN version shipped.`

---

## Attribution

Portions of NetchModern are derived from Netch (https://github.com/NetchX/Netch),
Copyright © Netch contributors, GPL-3.0.
