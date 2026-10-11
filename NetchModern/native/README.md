# Native components

`NetchModern/native/` holds the native binaries Game+ stages into the published `bin/` (see
[`../build.ps1`](../build.ps1)). **They are not committed to this repository** — the `.gitignore`
ignores `native/*.exe`, `native/*.dll`, `native/*.dat`, `native/*.bin` and `native/*.sys`. Only this
README and the pinned manifest [`native-manifest.json`](native-manifest.json) are tracked.

The binaries are downloaded from their **official upstream release URLs** by
[`../fetch-native.ps1`](../fetch-native.ps1), which reads `native-manifest.json`, verifies the SHA-256
of every downloaded asset **and** of every extracted file (any mismatch fails hard), and places the
files here. CI runs it before the native check; you can run it locally too.

`build.ps1` copies everything from this folder (except this README and the manifest) into the staged
`bin/`, then verifies the components below. A missing **required** component fails the build (pass
`-AllowMissingNative` to downgrade that to a warning); a missing **optional** component only warns. The
staged `bin/NATIVE_COMPONENTS.txt` records, per file, whether it is present plus its size and SHA-256.

## Required (pinned in `native-manifest.json`)

| File | Purpose | Version | Official download | SHA-256 of the file | Licence |
| --- | --- | --- | --- | --- | --- |
| `xray.exe` | Proxy core: ss / trojan / vmess / vless / socks | Xray-core v25.12.8 | [Xray-windows-64.zip](https://github.com/XTLS/Xray-core/releases/download/v25.12.8/Xray-windows-64.zip) → `xray.exe` | `525a4b7d396769dc02beaedce2ee378968b510676b7a46e2b1c6348165b8a0d4` | `MPL-2.0` |
| `sing-box.exe` | Proxy core: hysteria2, Shadowsocks plugins | sing-box v1.14.3 | [sing-box-1.14.3-windows-amd64.zip](https://github.com/SagerNet/sing-box/releases/download/v1.14.3/sing-box-1.14.3-windows-amd64.zip) → `sing-box-1.14.3-windows-amd64/sing-box.exe` | `5c19bb3f8c12d4e85b3d47fe63123a7624b314b58dd7a8e32f51004f1c4d2faf` | `GPL-3.0-or-later` (+ naming terms) |
| `geoip.dat` | IP-based routing data | Loyalsoldier/v2ray-rules-dat 202610102207 | [geoip.dat](https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610102207/geoip.dat) | `116cc0f03d48991962f7f9cdbbcf53d45d89777f9c3cd1a663210853e5df6093` | `GPL-3.0` (GeoLite2 data) |
| `geosite.dat` | Domain-based routing data | Loyalsoldier/v2ray-rules-dat 202610102207 | [geosite.dat](https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610102207/geosite.dat) | `e5d2232b6b0fe8eb8f4e7ebe3964fe13e5ff3d4f39065b97d77523eb376b4af9` | `GPL-3.0` |
| `wintun.dll` | WinTUN adapter DLL (amd64) | WinTUN 0.14.1 | [wintun-0.14.1.zip](https://www.wintun.net/builds/wintun-0.14.1.zip) → `wintun/bin/amd64/wintun.dll` | `e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce` | Prebuilt-binaries licence (unmodified DLL) |
| `tun2socks.bin` | TUN-mode capture (Netch cgo DLL: `tun_init`/`tun_dial`/...) | Netch 1.9.7 | [Netch.7z](https://github.com/netchx/netch/releases/download/1.9.7/Netch.7z) → `Netch/bin/tun2socks.bin` | `1eaca52e1b73491747df71403fdd1228481c7f43691af623aac29889619f0d23` | `GPL-3.0` |
| `RouteHelper.bin` | Route-table helper for TUN mode | Netch 1.9.7 | [Netch.7z](https://github.com/netchx/netch/releases/download/1.9.7/Netch.7z) → `Netch/bin/RouteHelper.bin` | `4aa021f1ef0dd4d70ec913656bddcd097619a04cbc7660dcaccb9679f4097cd3` | `GPL-3.0` |

The asset hashes (of the downloaded zip / 7z / dat) are in the manifest's `sha256` field.

Version notes:

- **Xray-core is pinned to v25.12.8, not v26.x.** Xray v26.x removed `tlsSettings.allowInsecure` and
  refuses to load a config that sets it to `true`; `V2rayConfigGenerator` still emits it for servers with
  "skip certificate verification". Migrate to `pinnedPeerCertSha256` before bumping to v26.x.
- **sing-box ≥ 1.11** is needed for the route `sniff` action. 1.14.3 was validated with `sing-box check`
  against configs shaped like `SingBoxConfigGenerator` output (hysteria2 + salamander, shadowsocks +
  obfs-local, sniff/reject actions, clash_api). `libcronet.dll` from the same zip is not shipped — sing-box
  loads it only for the naive outbound, which Game+ does not use.
- **tun2socks.bin / RouteHelper.bin** must be Netch's own DLL builds (P/Invoked by `Tun2socksInterop` /
  `RouteHelperInterop`); xjasonlyu/tun2socks executables are **not** interchangeable. Netch 1.9.7's
  RouteHelper.bin does not export `RefreshIPTable`, which is declared in `RouteHelperInterop` but never called.

## Optional (process mode) — not fetched

| File | Purpose | Upstream | Licence | Status |
| --- | --- | --- | --- | --- |
| `Redirector.bin` | Process-mode capture helper (netfilter2) | [netchx/netch](https://github.com/netchx/netch) | `GPL-3.0` | Not fetched: useless without `nfapi.dll` |
| `nfapi.dll` | NetFilter SDK API | [netfiltersdk.com](https://netfiltersdk.com/) | **Commercial** | Not fetched, not shipped (decision) |
| `nfdriver.sys` | NetFilter SDK kernel driver | [netfiltersdk.com](https://netfiltersdk.com/) | **Commercial** | Not fetched, not shipped (decision) |

**Decision (owner): process mode is kept out of the installer until a NetFilter SDK redistribution
licence is obtained.** The NetFilter SDK is proprietary; it is never downloaded from unofficial sources (Netch's release archive
contains copies, but Netch's licence does not grant Game+ redistribution rights). Without these files
Game+ falls back to TUN mode or the system proxy.

## How to populate `native/`

```powershell
# Windows, PowerShell 7 + 7-Zip (needed for Netch.7z)
pwsh ./NetchModern/fetch-native.ps1           # download, verify, place into NetchModern/native/
pwsh ./NetchModern/build.ps1                  # stage into electron-app/resources/backend/bin
```

`fetch-native.ps1` caches downloads under `%TEMP%\gameplus-native-cache` and skips files that already
match their pinned hash (`-Force` re-places them). To bump a component, edit `native-manifest.json`
(url, version, `sha256`, `fileSha256`) with hashes you computed yourself from the official asset.

## Licensing

See [`../../THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md) for the full attribution and the
source code offer, the sing-box naming condition and the MaxMind GeoLite attribution. The verbatim
licence texts shipped in the installer live in [`../../licenses/`](../../licenses/README.md).
