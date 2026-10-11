# Third-party licence texts

This folder is shipped inside the Game+ installer (`resources/licenses/`). It contains the verbatim
licence files of the third-party components that Game+ redistributes. Each file was taken from the
upstream project **at the exact tag / commit that Game+ pins** (see
`NetchModern/native/native-manifest.json`). Game+ itself is licensed under GPL-3.0 (`LICENSE`).

| File | Component (version shipped) | Licence | Taken from |
| --- | --- | --- | --- |
| `sing-box-LICENSE.txt` | sing-box v1.14.3 (`sing-box.exe`) | GPL-3.0-or-later + naming condition | https://github.com/SagerNet/sing-box/blob/v1.14.3/LICENSE (identical to the LICENSE in the release zip) |
| `Xray-core-LICENSE-MPL-2.0.txt` | Xray-core v25.12.8 (`xray.exe`) | MPL-2.0 | https://github.com/XTLS/Xray-core/blob/v25.12.8/LICENSE (identical to the LICENSE in the release zip) |
| `WinTUN-LICENSE.txt` | WinTUN 0.14.1 (`wintun.dll`) | WinTUN Prebuilt Binaries License | `wintun/LICENSE.txt` from https://www.wintun.net/builds/wintun-0.14.1.zip |
| `Netch-LICENSE-GPL-3.0.txt` | Netch 1.9.7 (`tun2socks.bin`, `RouteHelper.bin`) | GPL-3.0 | https://github.com/netchx/netch/blob/1.9.7/LICENSE |
| `v2ray-rules-dat-LICENSE-GPL-3.0.txt` | Loyalsoldier/v2ray-rules-dat 202610102207 (`geoip.dat`, `geosite.dat`) | GPL-3.0 | https://github.com/Loyalsoldier/v2ray-rules-dat/blob/202610102207/LICENSE |
| `Loyalsoldier-geoip-LICENSE-CC-BY-SA-4.0.txt` | GeoIP data inside `geoip.dat` | CC-BY-SA-4.0 | https://github.com/Loyalsoldier/geoip/blob/424733bd132427dbd505ccc3b8e3ebc427494063/LICENSE |
| `domain-list-community-LICENSE-MIT.txt` | domain data inside `geosite.dat` | MIT | https://github.com/v2fly/domain-list-community/blob/c6adacf29cc4fb05e04651fcf03e00989ef9f797/LICENSE |
| `MaxMind-GeoLite-ATTRIBUTION.txt` | MaxMind GeoLite data inside `geoip.dat` | CC-BY-SA-4.0 + GeoLite EULA | https://www.maxmind.com/en/geolite2/eula |

Corresponding source for every GPL / MPL component is listed in `THIRD_PARTY_NOTICES.md`
("Source code offer").
