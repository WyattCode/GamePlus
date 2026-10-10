# 游戏规则库（远程更新）

Game+ 内置的游戏规则库（`SampleGames.Create()`）随安装包发布，只有更新应用才能改变。为了让规则库可以独立更新，Game+ 支持从一个可配置的网址下载**带版本号的规则文件**，并在本地缓存；下载失败或版本更旧时继续使用内置/缓存副本。

The built-in game rule library ships with the app. To let it change without a release, Game+ can download a **versioned rules file** from a configurable URL and cache it locally; on failure or an older version it keeps using the bundled/cached copy.

## 配置 / Configuration

- 设置项 `rulesUrl`（`settings.set` / `settings.get`）。
- 默认为空字符串 = **功能关闭**，此时不会产生任何网络请求。
- 非空时必须是 `https://` 开头的绝对网址（仅回环地址允许 `http://`，便于本地测试）。
- 缓存文件：`%APPDATA%\GamePlus\game-rules.json`（与 `custom-games.json` 同目录）。

## 文件格式 / File format

```json
{
  "schema": 1,
  "version": 20261010,
  "updatedAt": "2026-10-10T00:00:00Z",
  "games": [
    {
      "Id": "valorant",
      "Name": "无畏契约",
      "EnglishName": "Valorant",
      "Category": "Hot",
      "Regions": ["国际服", "亚服"],
      "Platforms": ["PC"],
      "ProxyProcesses": ["VALORANT-Win64-Shipping.exe"],
      "ProxyDomains": ["riotgames.com"],
      "ProxyIps": [],
      "Aliases": [],
      "Icon": "🎯",
      "AccentColor": "#FF4655"
    }
  ]
}
```

- 属性名使用 PascalCase，与 `custom-games.json` 完全一致；`Category` 可写字符串（`Hot` / `Pc` / `Console` / `Platform`）或数字。
- 文件中的 `IsCustom` / `IsFavorite` 会被忽略并强制为 `false`。
- `version` 必须为正整数，且**大于**当前生效版本才会被采用。

## 校验规则 / Validation

| 规则 | 说明 |
| --- | --- |
| `schema` | 必须为 `1` |
| `version` | 必须 > 0 |
| `games` | 1 ~ 2000 个 |
| `Id` | 匹配 `^[a-z0-9][a-z0-9._-]{0,63}$`，且不能以 `custom-` 开头，不能重复 |
| `Name` | 非空，≤ 64 字符 |
| 各列表字段 | 每项 ≤ 500 条 |
| `ProxyProcesses` | 必须以 `.exe` 结尾，且不能包含 `\` 或 `/` |
| `ProxyIps` | 必须是合法 IP 或 CIDR |
| `ProxyDomains` | 非空且不含空白字符 |

任何一条不满足都会导致整个文件被拒绝，错误信息为中文（例如 `第 3 个游戏的 Id 无效：…`），当前生效的规则库保持不变。

## 更新流程 / Update flow

1. `rules.update` 读取设置中的 `rulesUrl`（为空则报错 `未配置规则更新地址`）。
2. GET 请求，15 秒超时，响应体上限 5 MB。
3. 解析 + 校验；版本 ≤ 当前版本 → `upToDate`。
4. 版本更新 → 原子写入缓存（临时文件 + 移动），切换生效规则库 → `updated`。
5. 任何失败 → `failed`，错误信息写入 `lastError`，生效规则库不变。

启动时若已配置 `rulesUrl`，会在启动 10 秒后自动在后台执行一次同样的更新（失败仅记录日志）。

## RPC

- `rules.status` → `{ version, source, gameCount, lastCheck, lastError, lastResult, url }`，`source` 为 `bundled` / `cached` / `remote`。
- `rules.update` → `{ status: 'updated' | 'upToDate' | 'failed', message, version, games }`。
