using Netch.Core.Models.Games;

namespace Netch.Core.Services;

/// <summary>
///     Seed acceleration library — the "game traffic rule DB". Domains are the routes sent through a node;
///     everything else stays direct. Approximate but representative of what each title actually talks to.
/// </summary>
public static partial class SampleGames
{
    public static IEnumerable<Game> Create()
    {
        // ---- Platforms ----
        yield return new Game
        {
            Id = "steam", Name = "Steam 平台", EnglishName = "Steam", Icon = "🚂", AccentColor = "#1387B8", Category = GameCategory.Platform,
            Aliases = { "steam", "蒸汽", "zhengqi" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "steam.exe", "steamwebhelper.exe" },
            ProxyDomains = { "steampowered.com", "steamcommunity.com", "steamstatic.com", "steamcontent.com", "steamserver.net", "steam-chat.com", "valvesoftware.com" }
        };
        yield return new Game
        {
            Id = "epic", Name = "Epic 商城", EnglishName = "Epic Games", Icon = "🎁", AccentColor = "#57606A", Category = GameCategory.Platform,
            Aliases = { "epic", "e宝" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "EpicGamesLauncher.exe" },
            ProxyDomains = { "epicgames.com", "unrealengine.com", "helpshift.com", "akamaihd.net" }
        };
        yield return new Game
        {
            Id = "battlenet", Name = "战网", EnglishName = "Battle.net", Icon = "🛡️", AccentColor = "#00AEFF", Category = GameCategory.Platform,
            Aliases = { "battlenet", "暴雪", "baoxue" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "Battle.net.exe" },
            ProxyDomains = { "battle.net", "blizzard.com", "blzstatic.cn" }
        };
        yield return new Game
        {
            Id = "xbox", Name = "Xbox / PC Game Pass", EnglishName = "Xbox", Icon = "🟩", AccentColor = "#107C10", Category = GameCategory.Platform,
            Aliases = { "xbox", "gamepass" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyDomains = { "xbox.com", "xboxlive.com", "xboxservices.com", "gamepass.com" }
        };

        // ---- Hot ----
        yield return new Game
        {
            Id = "genshin", Name = "原神", EnglishName = "Genshin Impact", Icon = "🌟", AccentColor = "#4A90D9", Category = GameCategory.Hot,
            Aliases = { "ys", "yuanshen", "原神" }, Regions = { "国服", "国际服" }, Platforms = { "PC", "手游", "主机" },
            ProxyProcesses = { "GenshinImpact.exe", "YuanShen.exe" },
            ProxyDomains = { "hoyoverse.com", "mihoyo.com", "yuanshen.com", "hoyoversegdk.com" }
        };
        yield return new Game
        {
            Id = "starrail", Name = "崩坏：星穹铁道", EnglishName = "Honkai: Star Rail", Icon = "🚄", AccentColor = "#7B61FF", Category = GameCategory.Hot,
            Aliases = { "星铁", "xt", "starrail" }, Regions = { "国服", "国际服" }, Platforms = { "PC", "手游" },
            ProxyProcesses = { "StarRail.exe" },
            ProxyDomains = { "hoyoverse.com", "mihoyo.com", "starrails.com" }
        };
        yield return new Game
        {
            Id = "valorant", Name = "无畏契约", EnglishName = "Valorant", Icon = "🎯", AccentColor = "#FF4655", Category = GameCategory.Hot,
            Aliases = { "valorant", "瓦", "wf" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "VALORANT.exe", "RiotClientServices.exe" },
            ProxyDomains = { "riotgames.com", "playvalorant.com", "riotcdn.net" }
        };
        yield return new Game
        {
            Id = "lol-global", Name = "英雄联盟（国际服）", EnglishName = "League of Legends", Icon = "⚔️", AccentColor = "#C89B3C", Category = GameCategory.Hot,
            Aliases = { "lol", "英雄联盟", "yxlm" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "LeagueofLegends.exe", "RiotClientServices.exe" },
            ProxyDomains = { "riotgames.com", "leagueoflegends.com", "riotcdn.net" }
        };
        yield return new Game
        {
            Id = "cs2", Name = "反恐精英 2", EnglishName = "Counter-Strike 2", Icon = "💣", AccentColor = "#F0A500", Category = GameCategory.Hot,
            Aliases = { "cs2", "csgo", "cs" }, Regions = { "国际服", "国服" }, Platforms = { "PC" },
            ProxyProcesses = { "cs2.exe" },
            ProxyDomains = { "steampowered.com", "steamserver.net", "valvesoftware.com", "steamcontent.com" }
        };
        yield return new Game
        {
            Id = "apex", Name = "Apex 英雄", EnglishName = "Apex Legends", Icon = "🔺", AccentColor = "#DA292A", Category = GameCategory.Hot,
            Aliases = { "apex", "阿佩克斯" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "r5apex.exe" },
            ProxyDomains = { "ea.com", "easports.com", "respawn.com" }
        };

        // ---- PC ----
        yield return new Game
        {
            Id = "pubg", Name = "绝地求生", EnglishName = "PUBG: Battlegrounds", Icon = "🪂", AccentColor = "#F2A900", Category = GameCategory.Pc,
            Aliases = { "pubg", "吃鸡", "chiji" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "TslGame.exe" },
            ProxyDomains = { "pubg.com", "krafton.com", "playbattlegrounds.com" }
        };
        yield return new Game
        {
            Id = "warzone", Name = "使命召唤：战区", EnglishName = "Call of Duty: Warzone", Icon = "🎖️", AccentColor = "#6E7F1E", Category = GameCategory.Pc,
            Aliases = { "cod", "使命召唤", "warzone" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "cod.exe" },
            ProxyDomains = { "callofduty.com", "activision.com", "demonware.net", "battle.net" }
        };
        yield return new Game
        {
            Id = "diablo4", Name = "暗黑破坏神 IV", EnglishName = "Diablo IV", Icon = "😈", AccentColor = "#B02A2A", Category = GameCategory.Pc,
            Aliases = { "diablo", "暗黑", "d4" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Diablo IV.exe" },
            ProxyDomains = { "blizzard.com", "battle.net" }
        };
        yield return new Game
        {
            Id = "dota2", Name = "DOTA 2", EnglishName = "Dota 2", Icon = "🐉", AccentColor = "#C23C2A", Category = GameCategory.Pc,
            Aliases = { "dota", "刀塔", "dt2" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "dota2.exe" },
            ProxyDomains = { "steampowered.com", "dota2.com", "steamserver.net" }
        };
        yield return new Game
        {
            Id = "eldenring", Name = "艾尔登法环", EnglishName = "Elden Ring", Icon = "💍", AccentColor = "#C0A062", Category = GameCategory.Pc,
            Aliases = { "eldenring", "老头环", "法环" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "eldenring.exe" },
            ProxyDomains = { "bandainamcoent.com", "fromsoftware.jp" }
        };
        yield return new Game
        {
            Id = "r6", Name = "彩虹六号：围攻", EnglishName = "Rainbow Six Siege", Icon = "🌈", AccentColor = "#0098DB", Category = GameCategory.Pc,
            Aliases = { "r6", "彩六", "cl" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "RainbowSix.exe" },
            ProxyDomains = { "ubisoft.com", "ubi.com", "ubisoftconnect.com" }
        };
        yield return new Game
        {
            Id = "gtao", Name = "GTA 线上模式", EnglishName = "GTA Online", Icon = "🚗", AccentColor = "#F5A623", Category = GameCategory.Pc,
            Aliases = { "gta", "gta5", "侠盗猎车" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "GTA5.exe" },
            ProxyDomains = { "rockstargames.com", "rockstarnorth.com" }
        };
        yield return new Game
        {
            Id = "eft", Name = "逃离塔科夫", EnglishName = "Escape from Tarkov", Icon = "🔫", AccentColor = "#7A7A52", Category = GameCategory.Pc,
            Aliases = { "eft", "塔科夫", "tkf" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "EscapeFromTarkov.exe" },
            ProxyDomains = { "escapefromtarkov.com", "battlestate.com" }
        };
        yield return new Game
        {
            Id = "destiny2", Name = "命运 2", EnglishName = "Destiny 2", Icon = "🌌", AccentColor = "#6C5CE7", Category = GameCategory.Pc,
            Aliases = { "destiny", "命运", "my2" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "destiny2.exe" },
            ProxyDomains = { "bungie.net" }
        };

        // ---- Console / cross-platform ----
        yield return new Game
        {
            Id = "minecraft", Name = "我的世界", EnglishName = "Minecraft", Icon = "🧱", AccentColor = "#5D9C3C", Category = GameCategory.Console,
            Aliases = { "mc", "minecraft", "我的世界" }, Regions = { "国际服" }, Platforms = { "PC", "主机", "手游" },
            ProxyProcesses = { "javaw.exe", "Minecraft.exe" },
            ProxyDomains = { "minecraft.net", "mojang.com", "minecraftservices.com", "xboxlive.com" }
        };
        yield return new Game
        {
            Id = "fortnite", Name = "堡垒之夜", EnglishName = "Fortnite", Icon = "🏰", AccentColor = "#7B2FF7", Category = GameCategory.Console,
            Aliases = { "fortnite", "堡垒", "bl" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "FortniteClient-Win64-Shipping.exe" },
            ProxyDomains = { "epicgames.com", "unrealengine.com" }
        };
        yield return new Game
        {
            Id = "roblox", Name = "Roblox", EnglishName = "Roblox", Icon = "🟥", AccentColor = "#E2231A", Category = GameCategory.Console,
            Aliases = { "roblox", "rb" }, Regions = { "国际服" }, Platforms = { "PC", "手游" },
            ProxyProcesses = { "RobloxPlayerBeta.exe" },
            ProxyDomains = { "roblox.com", "rbxcdn.com" }
        };
        yield return new Game
        {
            Id = "naraka", Name = "永劫无间", EnglishName = "Naraka: Bladepoint", Icon = "🗡️", AccentColor = "#D14343", Category = GameCategory.Console,
            Aliases = { "naraka", "永劫", "yj" }, Regions = { "国服", "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "NarakaBladepoint.exe" },
            ProxyDomains = { "narakathegame.com", "yjwujian.cn", "netease.com" }
        };

        // ---- More platforms ----
        yield return new Game
        {
            Id = "uplay", Name = "育碧 Connect", EnglishName = "Ubisoft Connect", Icon = "🌀", AccentColor = "#0070FF", Category = GameCategory.Platform,
            Aliases = { "uplay", "ubisoft", "育碧", "yubi" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "UbisoftConnect.exe", "upc.exe" },
            ProxyDomains = { "ubisoft.com", "ubi.com", "ubisoftconnect.com", "ubidemo.com" }
        };
        yield return new Game
        {
            Id = "eaapp", Name = "EA App", EnglishName = "EA App", Icon = "🟠", AccentColor = "#FF4747", Category = GameCategory.Platform,
            Aliases = { "ea", "origin", "橘子", "eaapp" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "EADesktop.exe", "EABackgroundService.exe" },
            ProxyDomains = { "ea.com", "origin.com", "easports.com", "dm.origin.com" }
        };
        yield return new Game
        {
            Id = "rockstar", Name = "Rockstar 平台", EnglishName = "Rockstar Games Launcher", Icon = "⭐", AccentColor = "#FCAF17", Category = GameCategory.Platform,
            Aliases = { "rockstar", "r星", "rsg" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "RockstarService.exe", "SocialClubHelper.exe" },
            ProxyDomains = { "rockstargames.com", "rockstarnorth.com", "socialclub.rockstargames.com" }
        };

        // ---- More hot ----
        yield return new Game
        {
            Id = "zzz", Name = "绝区零", EnglishName = "Zenless Zone Zero", Icon = "🌆", AccentColor = "#F5C518", Category = GameCategory.Hot,
            Aliases = { "zzz", "绝区零", "jql" }, Regions = { "国服", "国际服" }, Platforms = { "PC", "手游", "主机" },
            ProxyProcesses = { "ZenlessZoneZero.exe" },
            ProxyDomains = { "hoyoverse.com", "mihoyo.com", "zenlesszonezero.com" }
        };
        yield return new Game
        {
            Id = "wuwa", Name = "鸣潮", EnglishName = "Wuthering Waves", Icon = "🌊", AccentColor = "#2FA4E7", Category = GameCategory.Hot,
            Aliases = { "wuwa", "鸣潮", "mingchao" }, Regions = { "国服", "国际服" }, Platforms = { "PC", "手游" },
            ProxyProcesses = { "Wuthering Waves.exe" },
            ProxyDomains = { "kurogames.com", "kurogame.com", "aki-game.net" }
        };
        yield return new Game
        {
            Id = "deltaforce", Name = "三角洲行动", EnglishName = "Delta Force", Icon = "🪖", AccentColor = "#6E7B3D", Category = GameCategory.Hot,
            Aliases = { "deltaforce", "三角洲", "sjz" }, Regions = { "国服", "国际服" }, Platforms = { "PC", "手游" },
            ProxyProcesses = { "DeltaForceClient.exe", "DeltaForce.exe" },
            ProxyDomains = { "playdeltaforce.com", "tencentgames.com", "levelinfinite.com" }
        };
        yield return new Game
        {
            Id = "ow2", Name = "守望先锋 2", EnglishName = "Overwatch 2", Icon = "🧡", AccentColor = "#F99E1A", Category = GameCategory.Hot,
            Aliases = { "ow", "守望先锋", "swxf" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Overwatch.exe" },
            ProxyDomains = { "blizzard.com", "battle.net", "playoverwatch.com" }
        };
        yield return new Game
        {
            Id = "marvelrivals", Name = "漫威争锋", EnglishName = "Marvel Rivals", Icon = "🦸", AccentColor = "#D32F2F", Category = GameCategory.Hot,
            Aliases = { "marvelrivals", "漫威争锋", "mwzf" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "MarvelRivals.exe", "MarvelRivals_Launcher.exe" },
            ProxyDomains = { "marvelrivals.com", "netease.com", "easebar.com" }
        };

        // ---- More PC ----
        yield return new Game
        {
            Id = "bf2042", Name = "战地 2042", EnglishName = "Battlefield 2042", Icon = "🚁", AccentColor = "#2E7D32", Category = GameCategory.Pc,
            Aliases = { "bf", "战地", "zd2042" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "BF2042.exe" },
            ProxyDomains = { "ea.com", "easports.com", "battlefield.com" }
        };
        yield return new Game
        {
            Id = "ffxiv", Name = "最终幻想 14", EnglishName = "Final Fantasy XIV", Icon = "💠", AccentColor = "#3F6FB0", Category = GameCategory.Pc,
            Aliases = { "ff14", "ffxiv", "最终幻想" }, Regions = { "国际服", "国服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "ffxiv_dx11.exe" },
            ProxyDomains = { "finalfantasyxiv.com", "square-enix.com", "ffxiv.com" }
        };
        yield return new Game
        {
            Id = "poe2", Name = "流放之路 2", EnglishName = "Path of Exile 2", Icon = "🔮", AccentColor = "#8E4B2E", Category = GameCategory.Pc,
            Aliases = { "poe", "流放之路", "lfzl" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "PathOfExile.exe", "PathOfExileSteam.exe" },
            ProxyDomains = { "pathofexile.com", "pathofexile2.com" }
        };
        yield return new Game
        {
            Id = "thefinals", Name = "决胜巅峰", EnglishName = "The Finals", Icon = "💥", AccentColor = "#E23B2E", Category = GameCategory.Pc,
            Aliases = { "thefinals", "决胜巅峰", "jsdf" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Discovery.exe" },
            ProxyDomains = { "reachthefinals.com", "embark.games", "embark-studios.com" }
        };
        yield return new Game
        {
            Id = "palworld", Name = "幻兽帕鲁", EnglishName = "Palworld", Icon = "🐾", AccentColor = "#4FB0C6", Category = GameCategory.Pc,
            Aliases = { "palworld", "帕鲁", "hspl" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Palworld-Win64-Shipping.exe", "Palworld.exe" },
            ProxyDomains = { "pocketpair.jp", "palworldgame.com" }
        };
        yield return new Game
        {
            Id = "warframe", Name = "星际战甲", EnglishName = "Warframe", Icon = "🛸", AccentColor = "#5C6BC0", Category = GameCategory.Pc,
            Aliases = { "warframe", "战甲", "xjzj" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Warframe.x64.exe" },
            ProxyDomains = { "warframe.com", "digitalextremes.com" }
        };
        yield return new Game
        {
            Id = "dbd", Name = "黎明杀机", EnglishName = "Dead by Daylight", Icon = "🔪", AccentColor = "#8E2B2B", Category = GameCategory.Pc,
            Aliases = { "dbd", "黎明杀机", "lmsj" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "DeadByDaylight-Win64-Shipping.exe" },
            ProxyDomains = { "deadbydaylight.com", "bhvr.com" }
        };
        yield return new Game
        {
            Id = "rust", Name = "腐蚀", EnglishName = "Rust", Icon = "🪓", AccentColor = "#B5651D", Category = GameCategory.Pc,
            Aliases = { "rust", "腐蚀", "fushi" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "RustClient.exe" },
            ProxyDomains = { "facepunch.com", "rust.facepunch.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "rocketleague", Name = "火箭联盟", EnglishName = "Rocket League", Icon = "🚀", AccentColor = "#1E88E5", Category = GameCategory.Pc,
            Aliases = { "rl", "火箭联盟", "hjlm" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "RocketLeague.exe" },
            ProxyDomains = { "rocketleague.com", "psyonix.com", "epicgames.com" }
        };

        // ---- More console / cross-platform ----
        yield return new Game
        {
            Id = "fallguys", Name = "糖豆人", EnglishName = "Fall Guys", Icon = "🎪", AccentColor = "#FF7EB6", Category = GameCategory.Console,
            Aliases = { "fallguys", "糖豆人", "tdr" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "FallGuys_client_game.exe" },
            ProxyDomains = { "fallguys.com", "epicgames.com" }
        };
        yield return new Game
        {
            Id = "seaofthieves", Name = "贼海", EnglishName = "Sea of Thieves", Icon = "☠️", AccentColor = "#2E86AB", Category = GameCategory.Console,
            Aliases = { "sot", "贼海", "haidao" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "SoTGame.exe" },
            ProxyDomains = { "seaofthieves.com", "rare.co.uk", "xboxlive.com" }
        };

        // ---- FPS / shooters ----
        yield return new Game
        {
            Id = "helldivers2", Name = "绝地潜兵 2", EnglishName = "Helldivers 2", Icon = "🪖", AccentColor = "#E9B21A", Category = GameCategory.Pc,
            Aliases = { "helldivers", "绝地潜兵", "hd2", "民主" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "helldivers2.exe" },
            ProxyDomains = { "arrowheadgamestudios.com", "playstation.net", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "l4d2", Name = "求生之路 2", EnglishName = "Left 4 Dead 2", Icon = "🧟", AccentColor = "#8A9A2B", Category = GameCategory.Pc,
            Aliases = { "l4d2", "求生之路", "qszl" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "left4dead2.exe" },
            ProxyDomains = { "steampowered.com", "steamserver.net", "steamcontent.com" }
        };
        yield return new Game
        {
            Id = "hunt", Name = "猎杀：对决", EnglishName = "Hunt: Showdown", Icon = "🕯️", AccentColor = "#7A5230", Category = GameCategory.Pc,
            Aliases = { "hunt", "猎杀对决", "lsdj" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "HuntGame.exe" },
            ProxyDomains = { "huntshowdown.com", "crytek.com" }
        };
        yield return new Game
        {
            Id = "borderlands3", Name = "无主之地 3", EnglishName = "Borderlands 3", Icon = "🔫", AccentColor = "#E27D2E", Category = GameCategory.Pc,
            Aliases = { "borderlands", "无主之地", "wzzd", "bl3" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Borderlands3.exe" },
            ProxyDomains = { "2k.com", "gearboxsoftware.com" }
        };
        yield return new Game
        {
            Id = "payday3", Name = "收获日 3", EnglishName = "Payday 3", Icon = "💰", AccentColor = "#C0392B", Category = GameCategory.Pc,
            Aliases = { "payday", "收获日", "shr" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Payday3.exe" },
            ProxyDomains = { "paydaythegame.com", "starbreeze.com" }
        };
        yield return new Game
        {
            Id = "darktide", Name = "战锤 40K：暗潮", EnglishName = "Warhammer 40,000: Darktide", Icon = "🩸", AccentColor = "#9B2D2D", Category = GameCategory.Pc,
            Aliases = { "darktide", "暗潮", "40k" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "darktide.exe" },
            ProxyDomains = { "fatshark.se", "playdarktide.com" }
        };
        yield return new Game
        {
            Id = "titanfall2", Name = "泰坦陨落 2", EnglishName = "Titanfall 2", Icon = "🤖", AccentColor = "#3A6EA5", Category = GameCategory.Pc,
            Aliases = { "titanfall", "泰坦陨落", "ttyl", "tf2" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Titanfall2.exe" },
            ProxyDomains = { "ea.com", "respawn.com" }
        };
        yield return new Game
        {
            Id = "deeprock", Name = "深岩银河", EnglishName = "Deep Rock Galactic", Icon = "⛏️", AccentColor = "#C98A2B", Category = GameCategory.Pc,
            Aliases = { "drg", "深岩银河", "sy" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "FSD-Win64-Shipping.exe" },
            ProxyDomains = { "steampowered.com", "ghostship.dk" }
        };

        // ---- Survival / co-op ----
        yield return new Game
        {
            Id = "ark", Name = "方舟：生存进化", EnglishName = "ARK: Survival", Icon = "🦖", AccentColor = "#3E8E5A", Category = GameCategory.Pc,
            Aliases = { "ark", "方舟", "fz" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "ArkAscended.exe", "ShooterGame.exe" },
            ProxyDomains = { "studiowildcard.com", "arkdedicated.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "valheim", Name = "英灵神殿", EnglishName = "Valheim", Icon = "🪶", AccentColor = "#5C7A99", Category = GameCategory.Pc,
            Aliases = { "valheim", "英灵神殿", "ylsd" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "valheim.exe" },
            ProxyDomains = { "steampowered.com", "coffeestainstudios.com" }
        };
        yield return new Game
        {
            Id = "7days", Name = "七日杀", EnglishName = "7 Days to Die", Icon = "☣️", AccentColor = "#8B5E3C", Category = GameCategory.Pc,
            Aliases = { "7d2d", "七日杀", "qrs" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "7DaysToDie.exe" },
            ProxyDomains = { "steampowered.com", "thefunpimps.com" }
        };
        yield return new Game
        {
            Id = "enshrouded", Name = "雾锁王国", EnglishName = "Enshrouded", Icon = "🌫️", AccentColor = "#6D7B8D", Category = GameCategory.Pc,
            Aliases = { "enshrouded", "雾锁王国", "wswg" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "enshrouded.exe" },
            ProxyDomains = { "steampowered.com", "keengames.com" }
        };
        yield return new Game
        {
            Id = "sonsoftheforest", Name = "森林之子", EnglishName = "Sons of the Forest", Icon = "🌲", AccentColor = "#4E7A3E", Category = GameCategory.Pc,
            Aliases = { "sotf", "森林之子", "slz" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "SonsOfTheForest.exe" },
            ProxyDomains = { "steampowered.com", "endnight.com" }
        };
        yield return new Game
        {
            Id = "phasmophobia", Name = "恐鬼症", EnglishName = "Phasmophobia", Icon = "👻", AccentColor = "#5B6C8F", Category = GameCategory.Pc,
            Aliases = { "phasmophobia", "恐鬼症", "kgz" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "Phasmophobia.exe" },
            ProxyDomains = { "steampowered.com", "kineticgames.co.uk" }
        };
        yield return new Game
        {
            Id = "lethalcompany", Name = "致命公司", EnglishName = "Lethal Company", Icon = "🏭", AccentColor = "#7E8B4C", Category = GameCategory.Pc,
            Aliases = { "lethalcompany", "致命公司", "zmgs" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "Lethal Company.exe" },
            ProxyDomains = { "steampowered.com" }
        };
        yield return new Game
        {
            Id = "oncehuman", Name = "七日世界", EnglishName = "Once Human", Icon = "🌗", AccentColor = "#8E6FB0", Category = GameCategory.Pc,
            Aliases = { "oncehuman", "七日世界", "qrsj" }, Regions = { "国服", "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "OnceHuman.exe" },
            ProxyDomains = { "oncehuman.game", "starryinnovations.com", "netease.com" }
        };
        yield return new Game
        {
            Id = "raft", Name = "木筏求生", EnglishName = "Raft", Icon = "🛟", AccentColor = "#2FA8C4", Category = GameCategory.Pc,
            Aliases = { "raft", "木筏求生", "mfqs" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "Raft.exe" },
            ProxyDomains = { "steampowered.com", "redbeetinteractive.com" }
        };

        // ---- MMO / RPG ----
        yield return new Game
        {
            Id = "bg3", Name = "博德之门 3", EnglishName = "Baldur's Gate 3", Icon = "🎲", AccentColor = "#9C3B2E", Category = GameCategory.Pc,
            Aliases = { "bg3", "博德之门", "bdzm" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "bg3.exe", "bg3_dx11.exe" },
            ProxyDomains = { "larian.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "lostark", Name = "失落的方舟", EnglishName = "Lost Ark", Icon = "🏹", AccentColor = "#C79A3B", Category = GameCategory.Pc,
            Aliases = { "lostark", "失落的方舟", "sldfz" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "LOSTARK.exe" },
            ProxyDomains = { "amazongames.com", "smilegate.com" }
        };
        yield return new Game
        {
            Id = "gw2", Name = "激战 2", EnglishName = "Guild Wars 2", Icon = "🦁", AccentColor = "#B0403A", Category = GameCategory.Pc,
            Aliases = { "gw2", "激战2", "jz2" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "Gw2-64.exe" },
            ProxyDomains = { "guildwars2.com", "arena.net" }
        };
        yield return new Game
        {
            Id = "eso", Name = "上古卷轴 OL", EnglishName = "The Elder Scrolls Online", Icon = "📜", AccentColor = "#6B8E23", Category = GameCategory.Pc,
            Aliases = { "eso", "上古卷轴", "sgjz" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "eso64.exe" },
            ProxyDomains = { "elderscrollsonline.com", "zenimax.com" }
        };
        yield return new Game
        {
            Id = "newworld", Name = "新世界", EnglishName = "New World", Icon = "🌎", AccentColor = "#2E8B57", Category = GameCategory.Pc,
            Aliases = { "newworld", "新世界", "xsj" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "NewWorld.exe" },
            ProxyDomains = { "amazongames.com", "newworld.com" }
        };
        yield return new Game
        {
            Id = "nomanssky", Name = "无人深空", EnglishName = "No Man's Sky", Icon = "🪐", AccentColor = "#5C6BC0", Category = GameCategory.Pc,
            Aliases = { "nms", "无人深空", "wrsk" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "NMS.exe" },
            ProxyDomains = { "nomanssky.com", "hellogames.co.uk", "steampowered.com" }
        };

        // ---- Sports / racing / strategy ----
        yield return new Game
        {
            Id = "fc25", Name = "EA Sports FC 25", EnglishName = "EA Sports FC 25", Icon = "⚽", AccentColor = "#1FA05A", Category = GameCategory.Pc,
            Aliases = { "fc25", "fifa", "足球" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "FC25.exe" },
            ProxyDomains = { "ea.com", "easports.com" }
        };
        yield return new Game
        {
            Id = "nba2k25", Name = "NBA 2K25", EnglishName = "NBA 2K25", Icon = "🏀", AccentColor = "#C8482E", Category = GameCategory.Pc,
            Aliases = { "nba2k", "2k25", "篮球" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "NBA2K25.exe" },
            ProxyDomains = { "2k.com", "nba2k.com" }
        };
        yield return new Game
        {
            Id = "forza5", Name = "极限竞速：地平线 5", EnglishName = "Forza Horizon 5", Icon = "🏎️", AccentColor = "#7B4BC9", Category = GameCategory.Console,
            Aliases = { "forza", "地平线", "dpx", "极限竞速" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "ForzaHorizon5.exe" },
            ProxyDomains = { "forzamotorsport.net", "xboxlive.com" }
        };
        yield return new Game
        {
            Id = "civ6", Name = "文明 6", EnglishName = "Civilization VI", Icon = "🏛️", AccentColor = "#4E7CB0", Category = GameCategory.Pc,
            Aliases = { "civ6", "文明6", "wm6" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "CivilizationVI.exe", "CivilizationVI_DX12.exe" },
            ProxyDomains = { "2k.com", "firaxis.com" }
        };
        yield return new Game
        {
            Id = "aoe4", Name = "帝国时代 4", EnglishName = "Age of Empires IV", Icon = "🏯", AccentColor = "#B8862B", Category = GameCategory.Pc,
            Aliases = { "aoe4", "帝国时代", "dgsd" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "RelicCardinal.exe" },
            ProxyDomains = { "ageofempires.com", "xboxlive.com" }
        };

        // ---- Blizzard / Valve classics ----
        yield return new Game
        {
            Id = "hearthstone", Name = "炉石传说", EnglishName = "Hearthstone", Icon = "🃏", AccentColor = "#C77E2A", Category = GameCategory.Pc,
            Aliases = { "hs", "炉石传说", "ls" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "Hearthstone.exe" },
            ProxyDomains = { "blizzard.com", "battle.net" }
        };
        yield return new Game
        {
            Id = "sc2", Name = "星际争霸 2", EnglishName = "StarCraft II", Icon = "👽", AccentColor = "#3D6EA5", Category = GameCategory.Pc,
            Aliases = { "sc2", "星际争霸", "xjzb" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "SC2_x64.exe" },
            ProxyDomains = { "blizzard.com", "battle.net" }
        };
        yield return new Game
        {
            Id = "wow", Name = "魔兽世界", EnglishName = "World of Warcraft", Icon = "🐺", AccentColor = "#2E6DA4", Category = GameCategory.Pc,
            Aliases = { "wow", "魔兽世界", "mssj" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "Wow.exe" },
            ProxyDomains = { "blizzard.com", "battle.net", "worldofwarcraft.com" }
        };

        // ---- More console / cross-platform ----
        yield return new Game
        {
            Id = "haloinfinite", Name = "光环：无限", EnglishName = "Halo Infinite", Icon = "🔵", AccentColor = "#2E7DB5", Category = GameCategory.Console,
            Aliases = { "halo", "光环", "gh" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "HaloInfinite.exe" },
            ProxyDomains = { "halowaypoint.com", "xboxlive.com", "343industries.com" }
        };

        // ---- Monster Hunter / big 2024-2025 online titles ----
        yield return new Game
        {
            Id = "mhwilds", Name = "怪物猎人：荒野", EnglishName = "Monster Hunter Wilds", Icon = "🦕", AccentColor = "#C77B3B", Category = GameCategory.Hot,
            Aliases = { "mhwilds", "怪物猎人荒野", "gwlrhy", "mhw" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "MonsterHunterWilds.exe" },
            ProxyDomains = { "capcom.com", "capcom-games.com", "monsterhunter.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "mhworld", Name = "怪物猎人：世界", EnglishName = "Monster Hunter: World", Icon = "🦴", AccentColor = "#8C6239", Category = GameCategory.Pc,
            Aliases = { "mhworld", "怪物猎人世界", "gwlrsj" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "MonsterHunterWorld.exe" },
            ProxyDomains = { "capcom.com", "capcom-games.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "firstdescendant", Name = "第一后裔", EnglishName = "The First Descendant", Icon = "🧬", AccentColor = "#3B7DA8", Category = GameCategory.Hot,
            Aliases = { "tfd", "第一后裔", "dyhy" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "M1-Win64-Shipping.exe" },
            ProxyDomains = { "thefirstdescendant.com", "nexon.com" }
        };
        yield return new Game
        {
            Id = "deadlock", Name = "Deadlock", EnglishName = "Deadlock", Icon = "🕹️", AccentColor = "#B07B3B", Category = GameCategory.Hot,
            Aliases = { "deadlock", "水路", "v社新游" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "deadlock.exe", "project8.exe" },
            ProxyDomains = { "steampowered.com", "steamserver.net", "valvesoftware.com" }
        };

        // ---- Long-running online / MMO ----
        yield return new Game
        {
            Id = "warthunder", Name = "战争雷霆", EnglishName = "War Thunder", Icon = "✈️", AccentColor = "#5A7247", Category = GameCategory.Pc,
            Aliases = { "warthunder", "战争雷霆", "zzlt" }, Regions = { "国际服", "国服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "aces.exe" },
            ProxyDomains = { "warthunder.com", "gaijin.net", "gaijinent.com" }
        };
        yield return new Game
        {
            Id = "worldoftanks", Name = "坦克世界", EnglishName = "World of Tanks", Icon = "🔩", AccentColor = "#6B5B45", Category = GameCategory.Pc,
            Aliases = { "wot", "坦克世界", "tksj" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "WorldOfTanks.exe", "wgc.exe" },
            ProxyDomains = { "wargaming.net", "worldoftanks.com", "worldoftanks.eu" }
        };
        yield return new Game
        {
            Id = "blackdesert", Name = "黑色沙漠", EnglishName = "Black Desert", Icon = "🗺️", AccentColor = "#7A5C7A", Category = GameCategory.Pc,
            Aliases = { "bdo", "黑色沙漠", "hssm" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "BlackDesert64.exe" },
            ProxyDomains = { "blackdesertonline.com", "pearlabyss.com" }
        };
        yield return new Game
        {
            Id = "starcitizen", Name = "星际公民", EnglishName = "Star Citizen", Icon = "🛰️", AccentColor = "#3D5A80", Category = GameCategory.Pc,
            Aliases = { "starcitizen", "星际公民", "xjgm", "sc" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "StarCitizen.exe", "RSI Launcher.exe" },
            ProxyDomains = { "robertsspaceindustries.com", "cloudimperiumgames.com" }
        };
        yield return new Game
        {
            Id = "throneliberty", Name = "王权与自由", EnglishName = "Throne and Liberty", Icon = "👑", AccentColor = "#B8912B", Category = GameCategory.Pc,
            Aliases = { "tl", "王权与自由", "wqzy" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "TL.exe" },
            ProxyDomains = { "playthroneandliberty.com", "amazongames.com", "ncsoft.com" }
        };

        // ---- Co-op / party / classics ----
        yield return new Game
        {
            Id = "terraria", Name = "泰拉瑞亚", EnglishName = "Terraria", Icon = "🌴", AccentColor = "#4E8A5B", Category = GameCategory.Pc,
            Aliases = { "terraria", "泰拉瑞亚", "tlry" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Terraria.exe" },
            ProxyDomains = { "re-logic.com", "terraria.org", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "stardew", Name = "星露谷物语", EnglishName = "Stardew Valley", Icon = "🌻", AccentColor = "#C79A2B", Category = GameCategory.Pc,
            Aliases = { "stardew", "星露谷", "xlg" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Stardew Valley.exe" },
            ProxyDomains = { "stardewvalley.net", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "dst", Name = "饥荒：联机版", EnglishName = "Don't Starve Together", Icon = "🐷", AccentColor = "#8A6B4F", Category = GameCategory.Pc,
            Aliases = { "dst", "饥荒", "jh" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "dontstarve_steam_x64.exe" },
            ProxyDomains = { "klei.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "amongus", Name = "在我们之中", EnglishName = "Among Us", Icon = "👾", AccentColor = "#C0392B", Category = GameCategory.Pc,
            Aliases = { "amongus", "太空狼人杀", "tkl" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Among Us.exe" },
            ProxyDomains = { "innersloth.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "ittakestwo", Name = "双人成行", EnglishName = "It Takes Two", Icon = "❤️", AccentColor = "#C0485E", Category = GameCategory.Pc,
            Aliases = { "ittakestwo", "双人成行", "srcx" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "ItTakesTwo.exe" },
            ProxyDomains = { "ea.com", "hazelight.se" }
        };
        yield return new Game
        {
            Id = "twwarhammer3", Name = "全面战争：战锤 3", EnglishName = "Total War: Warhammer III", Icon = "🐲", AccentColor = "#8E3B2E", Category = GameCategory.Pc,
            Aliases = { "twwh3", "战锤全战", "qmzz" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "Warhammer3.exe" },
            ProxyDomains = { "totalwar.com", "sega.com", "creative-assembly.com" }
        };
        yield return new Game
        {
            Id = "dyinglight2", Name = "消逝的光芒 2", EnglishName = "Dying Light 2", Icon = "🌇", AccentColor = "#C0692B", Category = GameCategory.Pc,
            Aliases = { "dyinglight", "消逝的光芒", "xsdgm" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "DyingLightGame.exe" },
            ProxyDomains = { "techlandgames.com", "dyinglightgame.com" }
        };

        // ---- Call of Duty / Rockstar / EA online ----
        yield return new Game
        {
            Id = "codbo6", Name = "使命召唤：黑色行动 6", EnglishName = "Call of Duty: Black Ops 6", Icon = "🎗️", AccentColor = "#5A6B2E", Category = GameCategory.Hot,
            Aliases = { "bo6", "黑色行动", "使命召唤本体" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "cod.exe" },
            ProxyDomains = { "callofduty.com", "activision.com", "demonware.net", "battle.net" }
        };
        yield return new Game
        {
            Id = "rdr2", Name = "荒野大镖客 2", EnglishName = "Red Dead Redemption 2", Icon = "🤠", AccentColor = "#9C3B2E", Category = GameCategory.Pc,
            Aliases = { "rdr2", "荒野大镖客", "dbk", "大镖客" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "RDR2.exe" },
            ProxyDomains = { "rockstargames.com", "rockstarnorth.com" }
        };
        yield return new Game
        {
            Id = "nfsunbound", Name = "极品飞车：不羁", EnglishName = "Need for Speed Unbound", Icon = "🏁", AccentColor = "#C0492B", Category = GameCategory.Pc,
            Aliases = { "nfs", "极品飞车", "jpfc" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "NeedForSpeedUnbound.exe" },
            ProxyDomains = { "ea.com", "needforspeed.com" }
        };

        // ---- Fighting games ----
        yield return new Game
        {
            Id = "sf6", Name = "街霸 6", EnglishName = "Street Fighter 6", Icon = "🥊", AccentColor = "#C79A2B", Category = GameCategory.Pc,
            Aliases = { "sf6", "街霸6", "街头霸王" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "StreetFighter6.exe" },
            ProxyDomains = { "capcom.com", "streetfighter.com", "capcom-games.com" }
        };
        yield return new Game
        {
            Id = "tekken8", Name = "铁拳 8", EnglishName = "Tekken 8", Icon = "👊", AccentColor = "#8E3B4E", Category = GameCategory.Pc,
            Aliases = { "tekken8", "铁拳8", "tq8" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Polaris-Win64-Shipping.exe" },
            ProxyDomains = { "bandainamcoent.com", "tekken.com" }
        };
        yield return new Game
        {
            Id = "mk1", Name = "真人快打 1", EnglishName = "Mortal Kombat 1", Icon = "🥋", AccentColor = "#B0402A", Category = GameCategory.Pc,
            Aliases = { "mk1", "真人快打", "zrkd" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "MK1.exe" },
            ProxyDomains = { "mortalkombat.com", "wbgames.com", "netherrealm.com" }
        };
        yield return new Game
        {
            Id = "strinova", Name = "卡拉彼丘", EnglishName = "Strinova", Icon = "🌸", AccentColor = "#D46A9C", Category = GameCategory.Hot,
            Aliases = { "strinova", "卡拉彼丘", "klbq" }, Regions = { "国服", "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "Strinova.exe" },
            ProxyDomains = { "strinova.com", "idreamsky.com" }
        };

        // ---- Ubisoft online ----
        yield return new Game
        {
            Id = "division2", Name = "全境封锁 2", EnglishName = "The Division 2", Icon = "🏙️", AccentColor = "#C46A1E", Category = GameCategory.Pc,
            Aliases = { "division", "全境封锁", "qjfs" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "TheDivision2.exe" },
            ProxyDomains = { "ubisoft.com", "ubi.com", "tomclancy-thedivision.ubisoft.com" }
        };
        yield return new Game
        {
            Id = "acshadows", Name = "刺客信条：影", EnglishName = "Assassin's Creed Shadows", Icon = "🥷", AccentColor = "#7A2E2E", Category = GameCategory.Pc,
            Aliases = { "acshadows", "刺客信条影", "cxxt" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "ACShadows.exe" },
            ProxyDomains = { "ubisoft.com", "assassinscreed.com", "ubi.com" }
        };
        yield return new Game
        {
            Id = "farcry6", Name = "孤岛惊魂 6", EnglishName = "Far Cry 6", Icon = "🌺", AccentColor = "#C0492B", Category = GameCategory.Pc,
            Aliases = { "farcry", "孤岛惊魂", "gdjh" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "FarCry6.exe" },
            ProxyDomains = { "ubisoft.com", "farcry.com", "ubi.com" }
        };

        // ---- Blizzard classics / survival / co-op ----
        yield return new Game
        {
            Id = "d2r", Name = "暗黑破坏神 2：重制版", EnglishName = "Diablo II: Resurrected", Icon = "🦇", AccentColor = "#8B2E2E", Category = GameCategory.Pc,
            Aliases = { "d2r", "暗黑2", "ah2" }, Regions = { "国际服", "亚服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "D2R.exe" },
            ProxyDomains = { "blizzard.com", "battle.net" }
        };
        yield return new Game
        {
            Id = "hots", Name = "风暴英雄", EnglishName = "Heroes of the Storm", Icon = "🌩️", AccentColor = "#2E6DA4", Category = GameCategory.Pc,
            Aliases = { "hots", "风暴英雄", "fbyx" }, Regions = { "国际服", "亚服" }, Platforms = { "PC" },
            ProxyProcesses = { "HeroesOfTheStorm_x64.exe" },
            ProxyDomains = { "blizzard.com", "battle.net", "heroesofthestorm.com" }
        };
        yield return new Game
        {
            Id = "mtga", Name = "万智牌：竞技场", EnglishName = "MTG Arena", Icon = "🧙", AccentColor = "#7A5C2E", Category = GameCategory.Pc,
            Aliases = { "mtga", "万智牌", "wzp" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "MTGA.exe" },
            ProxyDomains = { "wizards.com", "magic.wizards.com" }
        };
        yield return new Game
        {
            Id = "vrising", Name = "夜族崛起", EnglishName = "V Rising", Icon = "🧛", AccentColor = "#8E2E4E", Category = GameCategory.Pc,
            Aliases = { "vrising", "夜族崛起", "yzjq" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "VRising.exe" },
            ProxyDomains = { "playvrising.com", "stunlock.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "theforest", Name = "森林", EnglishName = "The Forest", Icon = "🪵", AccentColor = "#4E6A3E", Category = GameCategory.Pc,
            Aliases = { "theforest", "森林", "sl" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "TheForest.exe" },
            ProxyDomains = { "endnight.com", "steampowered.com" }
        };
        yield return new Game
        {
            Id = "grounded", Name = "禁闭求生", EnglishName = "Grounded", Icon = "🐜", AccentColor = "#7E8B3C", Category = GameCategory.Pc,
            Aliases = { "grounded", "禁闭求生", "jbqs" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Maine-Win64-Shipping.exe" },
            ProxyDomains = { "obsidian.net", "xbox.com", "xboxlive.com" }
        };
        yield return new Game
        {
            Id = "overcooked2", Name = "胡闹厨房 2", EnglishName = "Overcooked! 2", Icon = "🍳", AccentColor = "#E0862B", Category = GameCategory.Pc,
            Aliases = { "overcooked", "胡闹厨房", "分手厨房", "hncf" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "Overcooked2.exe" },
            ProxyDomains = { "team17.com", "ghosttowngames.com", "steampowered.com" }
        };

        // ---- Console / cross-platform ----
        yield return new Game
        {
            Id = "flightsim", Name = "微软飞行模拟", EnglishName = "Microsoft Flight Simulator", Icon = "🛫", AccentColor = "#2E7DB5", Category = GameCategory.Console,
            Aliases = { "flightsim", "飞行模拟", "fxmn", "msfs" }, Regions = { "国际服" }, Platforms = { "PC", "主机" },
            ProxyProcesses = { "FlightSimulator.exe" },
            ProxyDomains = { "flightsimulator.com", "xboxlive.com" }
        };
        yield return new Game
        {
            Id = "aoe2de", Name = "帝国时代 2：决定版", EnglishName = "Age of Empires II: DE", Icon = "🛖", AccentColor = "#A8762B", Category = GameCategory.Pc,
            Aliases = { "aoe2", "帝国时代2", "dg2" }, Regions = { "国际服" }, Platforms = { "PC" },
            ProxyProcesses = { "AoE2DE_s.exe" },
            ProxyDomains = { "ageofempires.com", "xboxlive.com" }
        };
    }
}
