# Native components

`NetchModern/native/` holds the native binaries Game+ stages into the published `bin/` (see
[`../build.ps1`](../build.ps1)). **They are not committed to this repository** — the `.gitignore`
ignores `native/*.exe`, `native/*.dll`, `native/*.dat`, `native/*.bin` and `native/*.sys`, so only
this README is tracked. You must populate the folder before packaging.

`build.ps1` copies everything from this folder into the staged `bin/`, then verifies the components
below. A missing **required** component fails the build (pass `-AllowMissingNative` to downgrade that
to a warning for dev/CI packaging tests); a missing **optional** component only warns. The staged
`bin/NATIVE_COMPONENTS.txt` records, per file, whether it is present plus its size and SHA-256.

## Required

The app cannot accelerate without these.

| File | Purpose | Needed by | Upstream project | Licence | Download |
| --- | --- | --- | --- | --- | --- |
| `xray.exe` | Proxy core: ss / trojan / vmess / vless / socks | all modes | [XTLS/Xray-core](https://github.com/XTLS/Xray-core) | `MPL-2.0` | `TODO(owner): exact version + URL` |
| `sing-box.exe` | Proxy core: hysteria2, Shadowsocks plugins | all modes | [SagerNet/sing-box](https://github.com/SagerNet/sing-box) | `GPL-3.0-or-later` | `TODO(owner): exact version + URL` |
| `geoip.dat` | IP-based routing data | all modes | e.g. [Loyalsoldier/v2ray-rules-dat](https://github.com/Loyalsoldier/v2ray-rules-dat) | `TODO(owner): confirm dataset + licence` | `TODO(owner): exact version + URL` |
| `geosite.dat` | Domain-based routing data | all modes | e.g. [Loyalsoldier/v2ray-rules-dat](https://github.com/Loyalsoldier/v2ray-rules-dat) | `TODO(owner): confirm dataset + licence` | `TODO(owner): exact version + URL` |
| `wintun.dll` | WinTUN adapter driver DLL | TUN mode | [wintun.net](https://www.wintun.net/) (source: [git.zx2c4.com/wintun](https://git.zx2c4.com/wintun)) | Prebuilt-binaries licence (unmodified DLL may be redistributed) | `TODO(owner): exact version + URL` |
| `tun2socks.bin` | TUN-mode traffic capture helper | TUN mode | Netch project — `TODO(owner): confirm exact repo` | `TODO(owner): confirm (GPL-3.0 per Netch)` | `TODO(owner): exact version + URL` |
| `RouteHelper.bin` | Route-table helper for TUN mode | TUN mode | Netch project — `TODO(owner): confirm exact repo` | `TODO(owner): confirm (GPL-3.0 per Netch)` | `TODO(owner): exact version + URL` |

## Optional

Process mode only. NetFilter SDK is **commercially licensed** — redistribution requires a valid
licence from NetFilterSDK.com.

| File | Purpose | Needed by | Upstream project | Licence | Download |
| --- | --- | --- | --- | --- | --- |
| `Redirector.bin` | Process-mode traffic capture helper (netfilter2) | process mode | Netch project — `TODO(owner): confirm exact repo` | `TODO(owner): confirm (GPL-3.0 per Netch)` | `TODO(owner): exact version + URL` |
| `nfapi.dll` | NetFilter SDK API | process mode | [netfiltersdk.com](https://netfiltersdk.com/) | **Commercial** — `TODO(owner): confirm licence` | `TODO(owner): exact version + URL` |
| `nfdriver.sys` | NetFilter SDK kernel driver | process mode | [netfiltersdk.com](https://netfiltersdk.com/) | **Commercial** — `TODO(owner): confirm licence` | `TODO(owner): exact version + URL` |

If the optional components are absent, Game+ falls back to TUN mode or the system proxy.

## How to populate `native/`

1. Download each component from its upstream project (see the tables above).
2. Place the files directly in `NetchModern/native/` with the exact file names listed above.
3. Run `NetchModern/build.ps1` — it copies them into the staged `bin/`, verifies the required set,
   and writes `bin/NATIVE_COMPONENTS.txt` with the presence, size and SHA-256 of each file.

For a packaging smoke test without the binaries, run `./build.ps1 -AllowMissingNative`; the build
then warns instead of failing.

## Licensing

See [`../../THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md) for the full attribution and the
list of items the owner must still verify (exact versions, dataset sources, NetFilter SDK licence).
