# 公开发布通道 / Public release channel

本文档描述 Game+ 的**公开发布通道**：一个独立的公开仓库 `WyattCode/GamePlus-releases`，用于托管
自动更新清单和游戏规则库。应用**默认不启用**任何发布源（更新清单地址和规则库地址都是空的），
用户需要在 **设置** 中点击「使用默认发布源」按钮才会填入下面的默认地址。

This document describes Game+'s **public release channel**: a separate public repository
`WyattCode/GamePlus-releases` that hosts the auto-update manifest and the game rules library. The app
ships with **both sources empty** (features off); the user must click 「使用默认发布源」 in
**Settings** to fill in the default URLs below.

> **注意 / Note**：`WyattCode/GamePlus-releases` 仓库**尚未创建**，本文档只是约定。在仓库创建并
> 发布第一个版本之前，下面的默认地址会返回 404。
>
> The `WyattCode/GamePlus-releases` repository **does not exist yet**; this document is a
> specification. Until the repo is created and the first release is published, the default URLs below
> return 404.

## 仓库结构 / Repository layout

- **GitHub Releases**（每个 tag `vX.Y.Z`）：
  - `Game+-X.Y.Z-x64.exe` —— NSIS 安装包；
  - `Game+-X.Y.Z-x64.exe.sha256` —— 安装包的 SHA-256 校验文件；
  - `latest.json` —— 更新清单（应用自动更新读取）。
- **`main` 分支**：
  - `rules/game-rules.json` —— 游戏规则库；
  - `rules/game-rules.json.sha256` —— 规则库的 SHA-256 校验文件。

- **GitHub Releases** (one per tag `vX.Y.Z`): the NSIS installer `Game+-X.Y.Z-x64.exe`, its
  `.sha256` sidecar, and `latest.json` (the update manifest).
- **`main` branch**: `rules/game-rules.json` and its `.sha256` sidecar.

## 默认地址 / Default URLs

- 更新清单 / Manifest：
  `https://github.com/WyattCode/GamePlus-releases/releases/latest/download/latest.json`
- 游戏规则库 / Rules：
  `https://raw.githubusercontent.com/WyattCode/GamePlus-releases/main/rules/game-rules.json`

应用**默认不填写**这两个地址，对应功能处于关闭状态。用户可以在 **设置 → 软件更新** 和
**设置 → 游戏规则库** 中点击「使用默认发布源」一键填入上面的地址（只填入输入框，仍需点击保存）。

The app ships with **both URLs empty** (features off). In **Settings → Software update** and
**Settings → Game rules** the user can click 「使用默认发布源」 to fill in the default URLs above
(this only fills the input; the existing save button still has to be clicked).

## 发布流程 / Release procedure

1. 在 Game+ 主仓库中修改 `electron-app/package.json` 的 `version`（例如 `1.2.3`）。
2. 提交并打 tag：`git tag v1.2.3 && git push origin v1.2.3`。
3. CI 构建安装包并创建**草稿** release，上传 `Game+-1.2.3-x64.exe` 和 `.sha256`。
4. 下载安装包，校验 SHA-256 与 CI 输出一致。
5. 运行 `node scripts/release/make-manifest.mjs --installer "Game+-1.2.3-x64.exe" --version 1.2.3`
   生成 `latest.json`。
6. 在 `WyattCode/GamePlus-releases` 中创建 release（tag `v1.2.3`），上传安装包、`.sha256` 和
   `latest.json`。
7. 发布 release。应用下次检查更新时会读取 `releases/latest/download/latest.json`。

1. Bump `version` in `electron-app/package.json` in the main Game+ repo.
2. Commit and tag: `git tag v1.2.3 && git push origin v1.2.3`.
3. CI builds the installer and creates a **draft** release with the installer and its `.sha256`.
4. Download the installer and verify its SHA-256 matches CI output.
5. Run `node scripts/release/make-manifest.mjs --installer "Game+-1.2.3-x64.exe" --version 1.2.3`
   to produce `latest.json`.
6. In `WyattCode/GamePlus-releases`, create a release (tag `v1.2.3`) and upload the installer, its
   `.sha256`, and `latest.json`.
7. Publish the release. The app reads `releases/latest/download/latest.json` on its next check.

## 规则库流程 / Rules procedure

1. 生成规则文件：
   - 从内置规则库导出：`gameplus-host.exe --export-rules out.json`；
   - 或手工编辑一份符合 [`NetchModern/docs/game-rules.md`](../NetchModern/docs/game-rules.md) 的
     JSON。
2. 运行 `node scripts/release/make-rules.mjs --in out.json --out rules/game-rules.json`，脚本会
   重新校验并写入 `version` / `updatedAt`，同时生成 `.sha256`。
3. 把 `rules/game-rules.json` 和 `rules/game-rules.json.sha256` 提交到
   `WyattCode/GamePlus-releases` 的 `main` 分支。
4. **`version` 必须比当前生效版本更大**，否则应用会忽略这次更新。

1. Produce the rules file: `gameplus-host.exe --export-rules out.json`, or hand-edit a JSON file
   matching [`NetchModern/docs/game-rules.md`](../NetchModern/docs/game-rules.md).
2. Run `node scripts/release/make-rules.mjs --in out.json --out rules/game-rules.json`; it
   re-validates, sets `version` / `updatedAt`, and writes a `.sha256` sidecar.
3. Commit `rules/game-rules.json` and `rules/game-rules.json.sha256` to the `main` branch of
   `WyattCode/GamePlus-releases`.
4. **`version` must be greater than the currently active version**, otherwise the app ignores the
   update.

## 信任模型 / Trust model

- 更新清单和规则库都通过 **HTTPS** 下载。
- 安装包的 **SHA-256** 写在清单里，下载后校验；规则库的 SHA-256 由发布方提供，供人工核对。
- 因此，**GitHub 账号的安全就是更新的安全**：请为发布账号启用 **2FA**，并使用强密码 / 硬件密钥。
- **已知限制**：清单本身**没有签名**。如果攻击者能控制 `WyattCode/GamePlus-releases` 仓库或
  GitHub 账号，就能发布任意安装包。安装包也**没有代码签名**（见根 README）。

- Both the manifest and the rules file are downloaded over **HTTPS**.
- The installer's **SHA-256** is embedded in the manifest and verified after download; the rules
  file's SHA-256 is published for manual verification.
- Therefore **the security of the GitHub account is the security of updates**: enable **2FA** on the
  publishing account and use a strong password / hardware key.
- **Known limitation**: the manifest is **not signed**. An attacker who controls the
  `WyattCode/GamePlus-releases` repo or the GitHub account can publish an arbitrary installer. The
  installer is also **not code-signed** (see the root README).

## 状态 / Status

`WyattCode/GamePlus-releases` **尚未创建**，上述流程尚未执行过。

The `WyattCode/GamePlus-releases` repository **has not been created yet**; none of the above has been
executed.
