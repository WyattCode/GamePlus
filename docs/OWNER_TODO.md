# 合并 / 发布前 Owner 待办清单（Wyatt）

> 说明：以下每一项都是**必须由 owner 填写或决定**的内容。方括号内为需要填入的值。
> 链接格式为 `[文件:行](../文件#L行)`，相对本文件（`docs/`）解析。
> 合并规则：同一个事实（如 xray 版本）在多个文件出现时，只列一个勾选项，但给出全部位置。

## 1. 原生组件（必须）

- [x] 必需组件已固定在 [`NetchModern/native/native-manifest.json`](../NetchModern/native/native-manifest.json)（官方 release URL + 版本 + sha256），由 [`NetchModern/fetch-native.ps1`](../NetchModern/fetch-native.ps1) 下载、校验并放入 `NetchModern/native/`：Xray-core v25.12.8、sing-box v1.14.3、Loyalsoldier/v2ray-rules-dat 202610102207（geoip.dat / geosite.dat）、WinTUN 0.14.1、Netch 1.9.7（tun2socks.bin / RouteHelper.bin）。详见 [native/README.md](../NetchModern/native/README.md)。
- [ ] 确认上述版本选择（尤其：Xray 固定在 v25.12.8，因为 v26.x 移除了 `allowInsecure`；如需升级到 v26.x，需先把代码迁移到 `pinnedPeerCertSha256`）。
- [ ] （进程模式，可选）`Redirector.bin`、`nfapi.dll`、`nfdriver.sys` **未下载、未打包** —— 取决于第 2 节 NetFilter SDK 决策：[native/README.md:45](../NetchModern/native/README.md#L45)。

## 2. 许可证与第三方声明（发布前必须）

- [x] Xray-core、sing-box、WinTUN、geo 数据集、Netch 辅助程序的版本/来源/许可证已记录：[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)。
- [ ] **决定 NetFilter SDK（`nfapi.dll` / `nfdriver.sys`）**：持有有效的商业再分发许可证，或继续不打包进程模式：[THIRD_PARTY_NOTICES.md:67](../THIRD_PARTY_NOTICES.md#L67)。
- [ ] 确认 sing-box 的**附加许可条款 / 命名条件**已满足：[THIRD_PARTY_NOTICES.md:74](../THIRD_PARTY_NOTICES.md#L74)。
- [ ] 确认 GPL 组件的源码提供方式（链接上游 tag 是否足够，或自行镜像源码）：[THIRD_PARTY_NOTICES.md:77](../THIRD_PARTY_NOTICES.md#L77)。
- [ ] 确认 geoip.dat 中 MaxMind GeoLite2 数据的署名满足 GeoLite2 EULA：[THIRD_PARTY_NOTICES.md:38](../THIRD_PARTY_NOTICES.md#L38)。

## 3. 发布渠道

- [ ] 创建公开仓库 `WyattCode/GamePlus-releases`（尚未创建，见 [RELEASE_CHANNEL.md](RELEASE_CHANNEL.md)）。
- [ ] 为该账号/仓库启用 **2FA**。
- [ ] 发布首个 release，并上传 `latest.json` 与 `rules/game-rules.json`。

## 4. CI

- [ ] （可选）设置仓库变量 `ALLOW_INCOMPLETE_RELEASE` —— 仅用于测试 tag。
- [x] 原生组件来源已决定：CI 从官方 release URL 下载固定版本并校验 sha256（`Fetch native components` 步骤）。
- [x] 为节省 Actions 存储配额：安装包 artifact 仅在 push（分支与 v* tag）时上传，保留 5 天；PR 运行不上传。
- [ ] Actions 存储配额已满时上传会失败 —— 在仓库/账户 Settings → Actions → 删除旧 artifact，或等待配额重新计算。

## 5. Windows 实机验证

- [ ] 合并前在 Windows 实机运行 [WINDOWS_TEST_CHECKLIST.md](WINDOWS_TEST_CHECKLIST.md) 并全部通过。
