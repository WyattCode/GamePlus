# Game+

Game+ is a **Windows game accelerator**: it routes a game's (or any app's) traffic through a proxy
server you configure, using the bundled **Xray** / **sing-box** cores, with TUN or system-proxy capture.

> **Process mode is not included in the installer.** It needs the proprietary NetFilter SDK
> (`nfapi.dll` / `nfdriver.sys`), which Game+ is not licensed to redistribute. It stays out of the
> installer until a NetFilter SDK redistribution licence is obtained (see `docs/OWNER_TODO.md`).
> Game+ is not affiliated with or endorsed by the bundled third-party projects; see
> [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

![Game+ dashboard](NetchModern/docs/screenshot.png)

## How acceleration works (TUN, per process)

TUN mode is implemented by a second **sing-box** instance (v1.14.3) that owns a WinTUN interface named
`GamePlus` (172.19.0.1/30, gvisor stack, `auto_route`). It routes **by process** (the game's process
rules plus any extra apps from 分应用分流; names match with or without `.exe`, case-insensitive, by
executable path), **by sniffed domain** (TLS/HTTP/QUIC on ports 80/443 only) and **by IP/CIDR**.
Matched traffic — TCP and UDP — is forwarded to the local SOCKS5 inbound of the node core
(Xray / sing-box); everything else goes direct. Administrator rights are required.

- No NetFilter is needed; process mode stays unavailable/hidden. 自动 picks: process mode (only if
  NetFilter files are present) → TUN (the game has process or IP rules) → system proxy (domain-only
  games). Library-scan games (process rules only) are acceleratable via TUN.
- DNS: the TUN's `dns_mode` is `disabled`. Windows resolves DNS in the DNS Client service (svchost),
  so lookups cannot be attributed to a game process; hijacking or fakeip would push every app's DNS
  through sing-box and leave fake IPs in the Windows DNS cache after stopping. Game DNS lookups
  therefore behave exactly as without acceleration (system resolver, not proxied). Proxied
  connections use the IP the system resolved; domain rules still match via SNI/Host sniffing.
- Loop prevention: `xray.exe`, `sing-box.exe` and `gameplus-host.exe` are always direct, and the node
  server's IPv4 is excluded from the TUN routes; `auto_detect_interface` is on.
- Steam download split: download (CDN) domains are forced direct before the process rules, unless
  「游戏下载走代理」 is on. LAN share is unchanged (consoles talk to the core's LAN inbound; the
  core's own traffic is excluded from the TUN).
- Not captured: IPv6 (the TUN has no IPv6 address, so IPv6 traffic goes direct) and ICMP/ping.
- The legacy tun2socks path (`tun2socks.bin`, `RouteHelper.bin`, `wintun.dll` from Netch 1.9.7) is
  no longer used by TUN mode, but is still downloaded and packaged.
- Known risks that only a real Windows test can confirm: process lookup for UDP-heavy games;
  child/launcher processes missing from the rules (anti-cheat, crash reporter, launcher → game
  handoff); games that resolve via poisoned or geo DNS; anti-cheat reacting to a TUN adapter; the
  adapter and routes disappearing after Stop and after a crash/kill; coexistence with other VPN/TUN
  software; the performance overhead of routing ALL traffic through the gvisor stack (non-game
  traffic goes direct but is still processed in user space). See
  [`docs/WINDOWS_TEST_CHECKLIST.md`](docs/WINDOWS_TEST_CHECKLIST.md).

## Repository layout

```
.
├── electron-app/     # Electron + Vue 3 + TypeScript frontend (the only UI)
├── NetchModern/      # .NET 10 backend (Netch.Core + Netch.Host → gameplus-host.exe)
│   ├── src/          #   Netch.Core (domain/services) + Netch.Host (headless JSON-RPC server)
│   ├── native/       #   native binaries (git-ignored; see native/README.md)
│   └── build.ps1     #   publishes + stages the backend for electron-builder
├── LICENSE           # GPL-3.0-only
├── THIRD_PARTY_NOTICES.md
└── licenses/         # verbatim third-party licence texts (shipped in the installer)
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

## 更新与规则源 / Update & rules sources

自动更新和游戏规则库默认都是**空的**，对应功能处于关闭状态；两者都在 **设置 → 软件更新** 和
**设置 → 游戏规则库** 中配置，URL 完全由用户决定。公开发布通道（更新清单与规则库的托管方式、
发布流程、信任模型）见 [`docs/RELEASE_CHANNEL.md`](docs/RELEASE_CHANNEL.md)；在设置中点击
「使用默认发布源」按钮可以一键填入默认地址（只填入输入框，仍需点击保存）。

- **自动更新 / Auto update**：支持两种来源 —— 更新清单 JSON，或 GitHub Releases。清单格式为
  `{ "version", "url", "sha256", "notes" }`，其中 `sha256` 为必填（用于校验下载的安装包）。
- **游戏规则库 / Game rules**：规则库 URL 指向一份规则文件，格式见
  [`NetchModern/docs/game-rules.md`](NetchModern/docs/game-rules.md)。

Auto update and the game rules library are **empty by default** (the features are disabled). Both are
configured in **Settings → Software update** and **Settings → Game rules**; the URLs are entirely
user-supplied. The public release channel (how the manifest and rules are hosted, the release
procedure and the trust model) is documented in
[`docs/RELEASE_CHANNEL.md`](docs/RELEASE_CHANNEL.md); the 「使用默认发布源」 button in Settings fills
in the default URLs (it only fills the input — the save button still has to be clicked). Auto update
accepts either a manifest JSON or GitHub Releases; the manifest format is
`{ "version", "url", "sha256", "notes" }` with `sha256` required. The rules file format is documented
in [`NetchModern/docs/game-rules.md`](NetchModern/docs/game-rules.md).

## 安装位置与开机启动 / Install location & launch at login

安装程序会为**所有用户**安装，默认目录是 `C:\Program Files\Game+`，安装过程需要一次管理员权限
（UAC 提示）。安装时可以在安装向导中更改安装目录，但「开机启动」依赖一个以**最高权限**在登录时
运行的 Windows **计划任务**（`\Game+\AutoStart`），它直接运行 `Game+.exe`。只有把程序放在
Program Files 这类**仅管理员可写**的目录里，才能保证普通用户进程无法替换该 exe 并借此在下次登录
时获得管理员权限；因此后端只在这种情况下才允许创建/保留该计划任务。如果你在安装时选择了
Program Files 之外的目录，或者使用解压版/便携版运行，**开机启动**开关会被禁用并给出说明。从旧的
按用户安装（`%LOCALAPPDATA%\Programs\Game+`）升级时，安装程序会自动卸载旧版本并删除其旧的自动
启动任务，升级完成后请在 **设置** 中重新开启开机启动。程序启动时也会自动删除指向其他路径或用户
可写路径的自动启动任务；卸载时会一并删除该任务。

The installer installs for **all users** into `C:\Program Files\Game+` and requires administrator
rights (one UAC prompt). 开机启动 (launch at login) uses a Windows **scheduled task** that runs with
highest privileges at logon — so there is no UAC prompt at each login — and is therefore only allowed
when Game+ is installed in an admin-only-writable folder under Program Files. If you choose another
folder during install, or run the unpacked/portable build, the 开机启动 switch is disabled with an
explanation. When upgrading from an older per-user install (`%LOCALAPPDATA%\Programs\Game+`), the
installer automatically uninstalls the old copy and removes its old autostart task — re-enable
开机启动 in Settings afterwards. At startup the app also removes any autostart task that points at a
different or user-writable path, and uninstalling removes the task.

## 安装包未签名 / Unsigned installer

安装包**没有代码签名**。首次运行时 Windows SmartScreen 会提示「Windows 已保护你的电脑」：

1. 点击「更多信息」；
2. 点击「仍要运行」。

可选：下载后校验安装包的 SHA-256，与发布页给出的值比对：

```powershell
certutil -hashfile "Game+-x.y.z-x64.exe" SHA256
# 或
Get-FileHash "Game+-x.y.z-x64.exe" -Algorithm SHA256
```

加速功能会使用 WinTUN 驱动，部分杀毒软件可能误报，请自行判断是否信任。（安装包不含 NetFilter 驱动 / 进程模式。）

The installer is **not code-signed**. On first run Windows SmartScreen shows
"Windows 已保护你的电脑" — click 「更多信息」 → 「仍要运行」. Optionally verify the SHA-256 of the
downloaded installer (`certutil -hashfile "Game+-x.y.z-x64.exe" SHA256` or PowerShell
`Get-FileHash`) against the published value. Antivirus software may flag the WinTUN
driver bundled for acceleration (the NetFilter driver / process mode is not bundled).

## 隐私 / Privacy

Game+ 是**个人使用**版本：没有账号系统，也不会上传任何数据。

- 可选的**本地诊断记录**：只在本机记录加速成功/失败、使用的加速模式和失败原因（已去除 IP、域名、
  路径和用户名），可在设置中随时查看和清除。
- **崩溃报告**只保存在本机，不会上传；可在提交问题时手动附上。

本地文件位置：

- `%AppData%\Game+\crashes` —— 后端（host）崩溃报告；
- Electron userData 目录下的 `crashes` 和 `telemetry` 文件夹 —— 前端崩溃报告与本地诊断日志。

Game+ is a **personal-use** build: there are no accounts and nothing is ever uploaded.

- Optional **local diagnostics log**: records acceleration success/failure, the acceleration mode
  used and the failure reason (with IP, domain, path and user name removed) on this machine only.
  It can be viewed and cleared at any time in Settings.
- **Crash reports** are stored locally only and never uploaded; you can attach them manually when
  filing an issue.

Local file locations:

- `%AppData%\Game+\crashes` — backend (host) crash reports;
- the `crashes` and `telemetry` folders under the Electron userData directory — renderer crash
  reports and the local diagnostics log.

## License

Game+ is licensed under the **GNU General Public License v3.0** (`GPL-3.0-only`). See
[`LICENSE`](LICENSE) and [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for the full licence text
and third-party attributions.
