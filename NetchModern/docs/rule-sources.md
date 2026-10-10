# 内置游戏规则来源说明

本文档说明 NetchModern 内置（编译进程序）的游戏加速规则是如何收集、校验和维护的，以及规则的置信度分级与纠错流程。

## 一、规则来源

内置规则（`SampleGames.Create()`）中的每一条游戏条目，其进程名与域名都来自以下可复核的来源之一：

1. **官方支持 / 防火墙说明页面**：多数发行商会在帮助中心列出「需要放行的域名与端口」，例如 Riot、暴雪、EA、Square Enix、miHoYo 的支持页面。这类来源最可靠。
2. **v2fly / domain-list-community 分类文件**：社区维护的域名分类，例如 `riot`、`blizzard`、`steam`、`ea`、`mihoyo`、`valve`、`square-enix` 等。用于补全官方页面未列出的 CDN / 登录域名。
3. **已安装客户端的进程名**：在真实安装的游戏目录中确认可执行文件名（例如 `VALORANT-Win64-Shipping.exe`、`r5apex_dx12.exe`）。进程名必须与客户端实际启动的进程一致，否则进程分流不会生效。
4. **人工流量观察**：在受控环境下抓包，确认游戏实际连接的域名。仅作为交叉验证，不单独作为唯一依据。

## 二、置信度分级

| 级别 | 含义 | 处理方式 |
| --- | --- | --- |
| 高 | 官方支持页面明确列出，或进程名来自已安装客户端 | 直接收录 |
| 中 | 来自 v2fly 分类文件或社区广泛引用，但官方未明确列出 | 收录，并在条目上方注释来源 |
| 低 | 仅来自单次抓包或论坛帖子，无法交叉验证 | 暂不收录，或收录后标注待验证 |

## 三、十个热门游戏规则一览

| 游戏 | 区服 | 进程 | 域名 | 来源 | 置信度 | 最近复核 |
| --- | --- | --- | --- | --- | --- | --- |
| 无畏契约 Valorant | 国际服 / 国服 / 亚服 | `VALORANT-Win64-Shipping.exe`、`VALORANT.exe`、`RiotClientServices.exe` | `riotgames.com`、`playvalorant.com`、`riotcdn.net`、`pvp.net` | Riot 官方支持页、v2fly `riot`、客户端进程 | 高 | 2026-10 |
| 英雄联盟（国际服） | 国际服 / 亚服 | `League of Legends.exe`、`LeagueClient.exe`、`LeagueClientUx.exe`、`RiotClientServices.exe` | `riotgames.com`、`leagueoflegends.com`、`riotcdn.net`、`pvp.net`、`lolstatic.com` | Riot 官方支持页、v2fly `riot`、客户端进程 | 高 | 2026-10 |
| Apex 英雄 | 国际服 / 亚服 | `r5apex.exe`、`r5apex_dx12.exe` | `ea.com`、`easports.com`、`respawn.com`、`origin.com` | EA 官方支持页、v2fly `ea`、客户端进程 | 高 | 2026-10 |
| 反恐精英 2 | 国际服 / 国服 | `cs2.exe` | `steampowered.com`、`steamserver.net`、`valvesoftware.com`、`steamcontent.com`、`steamstatic.com` | Valve 官方支持页、v2fly `steam`/`valve` | 高 | 2026-10 |
| 绝地求生 PUBG | 国际服 / 亚服 | `TslGame.exe`、`TslGame_BE.exe` | `pubg.com`、`krafton.com`、`playbattlegrounds.com` | Krafton 官方支持页、v2fly `krafton`、客户端进程 | 中 | 2026-10 |
| 原神 | 国服 / 国际服 | `GenshinImpact.exe`、`YuanShen.exe` | `hoyoverse.com`、`mihoyo.com`、`yuanshen.com`、`hoyoversegdk.com`、`hoyolab.com` | miHoYo 官方支持页、v2fly `mihoyo`、客户端进程 | 高 | 2026-10 |
| 守望先锋 2 | 国际服 / 国服 / 亚服 | `Overwatch.exe` | `blizzard.com`、`battle.net`、`playoverwatch.com`、`battlenet.com.cn`、`blzstatic.cn` | 暴雪官方支持页、v2fly `blizzard`、网易国服公告 | 高 | 2026-10 |
| DOTA 2 | 国际服 / 国服 | `dota2.exe` | `steampowered.com`、`dota2.com`、`steamserver.net`、`valvesoftware.com`、`steamcontent.com` | Valve 官方支持页、v2fly `valve`、完美世界国服说明 | 高 | 2026-10 |
| 最终幻想 14 | 国际服 / 国服 | `ffxiv_dx11.exe`、`ffxiv.exe`、`ffxivboot.exe`、`ffxivboot64.exe`、`ffxivlauncher.exe`、`ffxivlauncher64.exe` | `finalfantasyxiv.com`、`square-enix.com`、`ffxiv.com`、`sdo.com` | Square Enix 官方支持页、v2fly `square-enix`、盛趣游戏国服说明 | 高 | 2026-10 |
| 怪物猎人：荒野 | 国际服 / 亚服 | `MonsterHunterWilds.exe` | `capcom.com`、`capcom-games.com`、`monsterhunter.com`、`steampowered.com` | Capcom 官方支持页、v2fly `capcom`、客户端进程 | 中 | 2026-10 |

## 四、为什么不收录宽泛的 CDN 域名

`qq.com`、`amazonaws.com`、`cloudflare.com`、`akamaihd.net`、`googleapis.com`、`azureedge.net`、`cloudfront.net` 等域名被大量无关服务共用。一旦把它们加入代理列表，浏览器、系统更新、其他应用都会一起走代理，既浪费带宽又可能触发风控。因此内置规则只收录**该游戏专属**的域名；确实需要走 CDN 的场景，请用户自行在自定义规则中添加。

## 五、如何反馈错误规则

如果你发现某个游戏的进程名或域名不正确（例如进程名拼写错误、域名已废弃、区服标注有误），请按 `docs/WINDOWS_TEST_CHECKLIST.md` 中的 Bug 模板提交 Issue，并附上：

- 游戏名称与区服；
- 实际观察到的进程名（任务管理器截图）；
- 实际连接的域名（抓包截图或日志）；
- 参考来源链接（官方支持页 / v2fly 分类文件）。

我们会复核后更新内置规则，并提升 `SampleGames.RulesVersion`。

## English summary

Bundled game rules are sourced from official publisher support/firewall pages (domains and ports), the v2fly `domain-list-community` category files (e.g. `riot`, `blizzard`, `steam`, `ea`, `mihoyo`, `valve`, `square-enix`), process names verified against installed clients, and manual traffic observation. Each entry is graded 高/中/低 (high/medium/low) confidence; only high- and medium-confidence entries are bundled. Broad shared CDNs (`qq.com`, `amazonaws.com`, `cloudflare.com`, `akamaihd.net`, `googleapis.com`, `azureedge.net`, `cloudfront.net`) are deliberately excluded because proxying them would route unrelated traffic. To report a wrong rule, open an issue using the bug template in `docs/WINDOWS_TEST_CHECKLIST.md` with the game name, region, observed process names, observed domains, and a source link.
