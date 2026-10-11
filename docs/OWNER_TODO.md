# 合并 / 发布前 Owner 待办清单（Wyatt）

> 说明：以下每一项都是**必须由 owner 填写或决定**的内容。方括号内为需要填入的值。
> 链接格式为 `[文件:行](../文件#L行)`，相对本文件（`docs/`）解析。
> 合并规则：同一个事实（如 xray 版本）在多个文件出现时，只列一个勾选项，但给出全部位置。

## 1. 原生组件（必须）

- [x] 必需组件已固定在 [`NetchModern/native/native-manifest.json`](../NetchModern/native/native-manifest.json)（官方 release URL + 版本 + sha256），由 [`NetchModern/fetch-native.ps1`](../NetchModern/fetch-native.ps1) 下载、校验并放入 `NetchModern/native/`：Xray-core v25.12.8、sing-box v1.14.3、Loyalsoldier/v2ray-rules-dat 202610102207（geoip.dat / geosite.dat）、WinTUN 0.14.1、Netch 1.9.7（tun2socks.bin / RouteHelper.bin）。详见 [native/README.md](../NetchModern/native/README.md)。
- [ ] 确认上述版本选择（尤其：Xray 固定在 v25.12.8，因为 v26.x 移除了 `allowInsecure`；如需升级到 v26.x，需先把代码迁移到 `pinnedPeerCertSha256`）。
- [x] **决定：进程模式不进入安装包**（`Redirector.bin`、`nfapi.dll`、`nfdriver.sys` 不下载、不打包、不再分发），直到取得 NetFilter SDK 再分发许可证：[native/README.md](../NetchModern/native/README.md)、[README.md](../README.md)。

## 2. 许可证与第三方声明（发布前必须）

- [x] Xray-core、sing-box、WinTUN、geo 数据集、Netch 辅助程序的版本/来源/许可证已记录：[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)。
- [x] **NetFilter SDK**：决定不打包进程模式，直到取得商业再分发许可证（见上文第 1 节与 THIRD_PARTY_NOTICES.md「NetFilter SDK / process mode — not shipped」）。
- [x] **sing-box 命名条件**：已引用 v1.14.3 LICENSE 原文；产品名/安装包/UI 不使用 sing-box 名称或品牌，「关于」页声明无关联（THIRD_PARTY_NOTICES.md「sing-box naming condition」）。
- [x] **GPL 源码提供**：安装包内附 `resources/licenses/`（各组件许可证原文）；THIRD_PARTY_NOTICES.md 列出各组件固定 tag 的源码地址并附书面源码提供承诺（written offer）。
- [x] **MaxMind GeoLite 署名**：已按 GeoLite EULA 第 3 节加入署名（THIRD_PARTY_NOTICES.md、设置 → 关于、`licenses/MaxMind-GeoLite-ATTRIBUTION.txt`）。
- [ ] **首次公开发布前**：书面承诺的联系渠道指向 `WyattCode/GamePlus-releases`（尚未创建）；Game+ 源码仓库目前为私有 —— 需创建该仓库，或公开 Game+ 源码 / 提供源码镜像。
- [ ] **需律师/owner 判断**：GeoLite EULA §6.3（更新后 30 天内销毁旧版本）与 §6.1（向第三方披露的条件）对固定版本 `geoip.dat` 的影响。建议：至少每月更新一次 rules-dat 固定版本，或把唯一使用的 `geoip:private` 规则改为显式私有网段并移除 `geoip.dat`。

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
