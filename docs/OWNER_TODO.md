# 合并 / 发布前 Owner 待办清单（Wyatt）

> 说明：以下每一项都是**必须由 owner 填写或决定**的内容。方括号内为需要填入的值。
> 链接格式为 `[文件:行](../文件#L行)`，相对本文件（`docs/`）解析。
> 合并规则：同一个事实（如 xray 版本）在多个文件出现时，只列一个勾选项，但给出全部位置。

## 1. 原生组件（必须）

- [ ] 将以下二进制放入 `NetchModern/native/`（必需）：`xray.exe`、`sing-box.exe`、`geoip.dat`、`geosite.dat`、`wintun.dll`、`tun2socks.bin`、`RouteHelper.bin`。
- [ ] 可选（进程模式）：`Redirector.bin`、`nfapi.dll`、`nfdriver.sys` —— 仅在决定保留进程模式时提供（见第 2 节 NetFilter SDK 决策）。
- [ ] 填写 `xray.exe` 的**确切版本 + 下载 URL**：[native/README.md:19](../NetchModern/native/README.md#L19)。
- [ ] 填写 `sing-box.exe` 的**确切版本 + 下载 URL**：[native/README.md:20](../NetchModern/native/README.md#L20)。
- [ ] 确认 `geoip.dat` / `geosite.dat` 的**数据集来源 + 许可证 + 版本 + URL**：[native/README.md:21](../NetchModern/native/README.md#L21)、[native/README.md:22](../NetchModern/native/README.md#L22)。
- [ ] 填写 `wintun.dll` 的**确切版本 + 下载 URL**：[native/README.md:23](../NetchModern/native/README.md#L23)。
- [ ] 确认 `tun2socks.bin` 的**上游仓库 + 许可证 + 版本 + URL**：[native/README.md:24](../NetchModern/native/README.md#L24)。
- [ ] 确认 `RouteHelper.bin` 的**上游仓库 + 许可证 + 版本 + URL**：[native/README.md:25](../NetchModern/native/README.md#L25)。
- [ ] （进程模式）确认 `Redirector.bin` 的**上游仓库 + 许可证 + 版本 + URL**：[native/README.md:34](../NetchModern/native/README.md#L34)。
- [ ] （进程模式）确认 `nfapi.dll` 的**许可证 + 版本 + URL**：[native/README.md:35](../NetchModern/native/README.md#L35)。
- [ ] （进程模式）确认 `nfdriver.sys` 的**许可证 + 版本 + URL**：[native/README.md:36](../NetchModern/native/README.md#L36)。

## 2. 许可证与第三方声明（发布前必须）

- [ ] 记录 `xray.exe`（Xray-core）**版本/tag 与确切 release 资源链接**：[THIRD_PARTY_NOTICES.md:29](../THIRD_PARTY_NOTICES.md#L29)、[THIRD_PARTY_NOTICES.md:83](../THIRD_PARTY_NOTICES.md#L83)。
- [ ] 记录 `sing-box.exe` **版本/tag**，并确认其**附加许可条款 / 命名条件**已满足：[THIRD_PARTY_NOTICES.md:30](../THIRD_PARTY_NOTICES.md#L30)、[THIRD_PARTY_NOTICES.md:80](../THIRD_PARTY_NOTICES.md#L80)。
- [ ] 记录 `wintun.dll`（WinTUN）**版本**：[THIRD_PARTY_NOTICES.md:31](../THIRD_PARTY_NOTICES.md#L31)、[THIRD_PARTY_NOTICES.md:86](../THIRD_PARTY_NOTICES.md#L86)。
- [ ] 记录 `geoip.dat` / `geosite.dat` 的**实际数据集来源、版本/日期与许可证**（如 v2fly/geoip — CC-BY-SA-4.0；v2fly/domain-list-community — MIT；Loyalsoldier/v2ray-rules-dat — GPL-3.0），并加入所需署名：[THIRD_PARTY_NOTICES.md:36](../THIRD_PARTY_NOTICES.md#L36)、[THIRD_PARTY_NOTICES.md:37](../THIRD_PARTY_NOTICES.md#L37)、[THIRD_PARTY_NOTICES.md:77](../THIRD_PARTY_NOTICES.md#L77)。
- [ ] 记录 Netch 原生辅助程序（`tun2socks.bin`、`RouteHelper.bin`、`Redirector.bin`）的**确切上游仓库、commit/tag 与构建来源**：[THIRD_PARTY_NOTICES.md:32](../THIRD_PARTY_NOTICES.md#L32)、[THIRD_PARTY_NOTICES.md:33](../THIRD_PARTY_NOTICES.md#L33)、[THIRD_PARTY_NOTICES.md:34](../THIRD_PARTY_NOTICES.md#L34)、[THIRD_PARTY_NOTICES.md:74](../THIRD_PARTY_NOTICES.md#L74)。
- [ ] **决定 NetFilter SDK（`nfapi.dll` / `nfdriver.sys`）**：持有有效的商业再分发许可证，或从安装包中移除 `nfdriver.sys` / 进程模式：[THIRD_PARTY_NOTICES.md:70](../THIRD_PARTY_NOTICES.md#L70)。

## 3. 发布渠道

- [ ] 创建公开仓库 `WyattCode/GamePlus-releases`（尚未创建，见 [RELEASE_CHANNEL.md](RELEASE_CHANNEL.md)）。
- [ ] 为该账号/仓库启用 **2FA**。
- [ ] 发布首个 release，并上传 `latest.json` 与 `rules/game-rules.json`。

## 4. CI

- [ ] （可选）设置仓库变量 `ALLOW_INCOMPLETE_RELEASE` —— 仅用于测试 tag。
- [ ] **决定原生组件的来源**（仓库中不含 natives，tag 构建会失败）：三选一 —— 通过 Git LFS 提交、在 CI 中从固定 URL 下载并校验 sha256、或使用私有 artifact。

## 5. Windows 实机验证

- [ ] 合并前在 Windows 实机运行 [WINDOWS_TEST_CHECKLIST.md](WINDOWS_TEST_CHECKLIST.md) 并全部通过。
