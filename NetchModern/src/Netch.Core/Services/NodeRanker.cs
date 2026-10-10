using System.Text.RegularExpressions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Detects a node's region from its remark/hostname using a keyword table. Pure and deterministic so
///     it can be unit-tested without any network or platform dependency.
/// </summary>
public static class NodeRegions
{
    /// <summary>A region code + its Chinese display name.</summary>
    public readonly record struct Region(string Code, string Name);

    // Each rule has two patterns:
    //   Short — case-SENSITIVE, bounded by lookarounds that only exclude ASCII letters, so "HK-01" /
    //           "JP01" / "香港HK2" match but lowercase "hk node" / "us-west" / "jp-01" do not.
    //   Long  — case-INSENSITIVE, for CJK names, emoji flags and long latin words ("Japan", "Tokyo").
    // A rule matches when either pattern matches.
    //
    // NOTE: .NET's \b treats CJK characters and digits as word characters, so "JP01" / "香港HK2" would
    // never match with \b. We therefore use explicit lookarounds that only exclude ASCII letters.
    private sealed record Rule(Region Region, Regex Short, Regex Long)
    {
        public bool IsMatch(string text) => Short.IsMatch(text) || Long.IsMatch(text);
    }

    // Order matters: HK and TW are checked before CN so "香港 CN2" resolves to HK.
    private static readonly Rule[] Rules =
    {
        new(new Region("HK", "香港"),
            new Regex(@"(?<![A-Za-z])HK(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"香港|港|🇭🇰|Hong ?Kong", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("TW", "台湾"),
            new Regex(@"(?<![A-Za-z])TW(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"台湾|台北|🇹🇼|Taiwan", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("JP", "日本"),
            new Regex(@"(?<![A-Za-z])JP(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"日本|东京|大阪|🇯🇵|Japan|Tokyo|Osaka", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("KR", "韩国"),
            new Regex(@"(?<![A-Za-z])KR(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"韩国|首尔|🇰🇷|Korea|Seoul", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("SG", "新加坡"),
            new Regex(@"(?<![A-Za-z])SG(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"新加坡|狮城|🇸🇬|Singapore", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("US", "美国"),
            new Regex(@"(?<![A-Za-z])USA?(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"美国|洛杉矶|硅谷|圣何塞|西雅图|🇺🇸|United States|Los Angeles|San Jose", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("EU", "欧洲"),
            new Regex(@"(?<![A-Za-z])(?:DE|UK|GB|FR|NL)(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"德国|英国|法国|荷兰|欧洲|🇩🇪|🇬🇧|🇫🇷|🇳🇱|Germany|London|Frankfurt|Amsterdam", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        new(new Region("CN", "中国大陆"),
            new Regex(@"(?<![A-Za-z])CN(?![A-Za-z])", RegexOptions.Compiled),
            new Regex(@"中国|国内|回国|大陆|上海|北京|广州|深圳|杭州|🇨🇳|China", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    };

    // CN must not win when the text also names HK/TW (e.g. "香港 CN2"). Same short/long split.
    private static readonly Regex HkTwShort = new(@"(?<![A-Za-z])(?:HK|TW)(?![A-Za-z])", RegexOptions.Compiled);
    private static readonly Regex HkTwLong = new(
        @"香港|港|🇭🇰|Hong ?Kong|台湾|台北|🇹🇼|Taiwan",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsHkTw(string text) => HkTwShort.IsMatch(text) || HkTwLong.IsMatch(text);

    /// <summary>Detects the region of a node from its remark (then hostname). Returns null when unknown.</summary>
    public static Region? Detect(Server server) => Detect(server.Remark, server.Hostname);

    /// <summary>Detects a region from free text. Returns null when no rule matches.</summary>
    public static Region? Detect(string? remark, string? hostname)
    {
        var text = string.Join(' ', new[] { remark, hostname }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var rule in Rules)
        {
            if (rule.Region.Code == "CN" && IsHkTw(text))
                continue;
            if (rule.IsMatch(text))
                return rule.Region;
        }

        return null;
    }
}

/// <summary>Maps a game's declared regions to the node regions that best serve it.</summary>
public static class GameRegionPreference
{
    /// <summary>A preferred set of region codes plus the label to show in the UI.</summary>
    public readonly record struct Preference(IReadOnlyList<string> Regions, string Label);

    /// <summary>
    ///     Derives the preferred node regions for a game. Returns null when there is no single preference
    ///     (no regions declared, only "国际服", or an ambiguous mix of 国服 + 国际服).
    /// </summary>
    public static Preference? For(Game? game)
    {
        if (game is null || game.Regions.Count == 0)
            return null;

        var regions = game.Regions;
        var hasCn = regions.Any(r => r.Contains("国服", StringComparison.Ordinal));
        var hasIntl = regions.Any(r => r.Contains("国际服", StringComparison.Ordinal));

        // 国服 + 国际服 together: we can't know which server the user actually plays on.
        if (hasCn && hasIntl)
            return null;

        var codes = new List<string>();
        string? label = null;

        void Add(string code)
        {
            if (!codes.Contains(code))
                codes.Add(code);
        }

        foreach (var raw in regions)
        {
            var r = raw.Trim();
            if (r.Length == 0)
                continue;

            if (r.Contains("亚服", StringComparison.Ordinal))
            {
                Add("HK");
                Add("TW");
                Add("JP");
                Add("KR");
                Add("SG");
                label ??= "亚服";
            }
            else if (r.Contains("日服", StringComparison.Ordinal))
            {
                Add("JP");
                label ??= "日服";
            }
            else if (r.Contains("韩服", StringComparison.Ordinal))
            {
                Add("KR");
                label ??= "韩服";
            }
            else if (r.Contains("港服", StringComparison.Ordinal))
            {
                Add("HK");
                label ??= "港服";
            }
            else if (r.Contains("台服", StringComparison.Ordinal))
            {
                Add("TW");
                label ??= "台服";
            }
            else if (r.Contains("北美服", StringComparison.Ordinal) || r.Contains("美服", StringComparison.Ordinal))
            {
                Add("US");
                label ??= "美服";
            }
            else if (r.Contains("欧服", StringComparison.Ordinal))
            {
                Add("EU");
                label ??= "欧服";
            }
            else if (r.Contains("国服", StringComparison.Ordinal))
            {
                Add("CN");
                label ??= "国服";
            }
            // "国际服" / "全球" / anything else → no preference contribution.
        }

        return codes.Count == 0 ? null : new Preference(codes, label ?? "偏好地区");
    }
}

/// <summary>One ranked candidate node.</summary>
public sealed record RankedNode(
    Server Server,
    int Delay,
    string? Region,
    string? RegionName,
    bool RegionMatch,
    int Score,
    string Note);

/// <summary>The full ranking result for a game + node pool.</summary>
public sealed record NodeRanking(
    IReadOnlyList<RankedNode> Candidates,
    RankedNode? Recommended,
    string Reason,
    string? PreferenceLabel,
    IReadOnlyList<string> PreferredRegions);

/// <summary>
///     Ranks proxy nodes for a game. The delay used here is the TCP connect time to the node itself,
///     not the latency to the game server — it is a proxy for how close the node is, not a measurement
///     of in-game ping.
/// </summary>
public static class NodeRanker
{
    /// <summary>Default penalty (ms) applied to a node that is outside the game's preferred regions.</summary>
    public const int RegionPenaltyMs = 60;

    /// <summary>Ranks the pool best-first and picks a recommended node with a human-readable reason.</summary>
    public static NodeRanking Rank(Game? game, IReadOnlyList<Server> servers, int regionPenaltyMs = RegionPenaltyMs)
    {
        var pref = GameRegionPreference.For(game);
        var preferred = pref?.Regions ?? Array.Empty<string>();
        var label = pref?.Label;

        var candidates = new List<RankedNode>(servers.Count);
        foreach (var s in servers)
        {
            var region = NodeRegions.Detect(s);
            var regionCode = region?.Code;
            var regionName = region?.Name;
            var match = regionCode is not null && preferred.Contains(regionCode);

            var tested = s.Delay >= 0;
            // Untested/failed nodes get score -1 so the UI can render them as grey/empty bars.
            var score = tested ? s.Delay + (preferred.Count > 0 && !match ? regionPenaltyMs : 0) : -1;

            candidates.Add(new RankedNode(s, s.Delay, regionCode, regionName, match, score, string.Empty));
        }

        // Tested nodes first (by score, then remark); untested before failed; then by remark.
        var ordered = candidates
            .OrderBy(c => Bucket(c.Delay))
            .ThenBy(c => c.Delay >= 0 ? c.Score : 0)
            .ThenBy(c => c.Server.Remark, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Notes are tags derived from the whole ordered list: the lowest-delay tested node is "延迟最低",
        // matching nodes are "区服匹配", both → "延迟最低 · 区服匹配", otherwise empty.
        var lowestDelay = ordered.Where(c => c.Delay >= 0)
            .Select(c => (int?)c.Delay)
            .Min();
        var withNotes = ordered
            .Select(c => c with { Note = BuildNote(c, lowestDelay) })
            .ToList();

        var recommended = withNotes.FirstOrDefault();
        var reason = BuildReason(game, withNotes, preferred, label, regionPenaltyMs);

        return new NodeRanking(withNotes, recommended, reason, label, preferred);
    }

    // 0 = tested, 1 = untested (-1), 2 = failed (< -1)
    private static int Bucket(int delay) => delay >= 0 ? 0 : delay == -1 ? 1 : 2;

    private static string BuildNote(RankedNode c, int? lowestDelay)
    {
        if (c.Delay < 0)
            return c.Delay == -1 ? "未测速" : "超时/不可达";

        var tags = new List<string>(2);
        if (lowestDelay is { } low && c.Delay == low)
            tags.Add("延迟最低");
        if (c.RegionMatch)
            tags.Add("区服匹配");
        return string.Join(" · ", tags);
    }

    private static string BuildReason(Game? game, IReadOnlyList<RankedNode> ordered, IReadOnlyList<string> preferred,
        string? label, int regionPenaltyMs)
    {
        var best = ordered.FirstOrDefault();
        if (best is null)
            return "没有可用线路";

        var tested = ordered.Where(c => c.Delay >= 0).ToList();
        if (tested.Count == 0)
            return "所有线路均未测通，已选择列表中的第一条线路";

        var lowest = tested.OrderBy(c => c.Delay).ThenBy(c => c.Server.Remark, StringComparer.OrdinalIgnoreCase).First();

        if (preferred.Count == 0)
        {
            var extra = HasCnAndIntl(game) ? "；该游戏同时有国服和国际服，按延迟选择" : "";
            return $"延迟最低（{lowest.Delay} ms）{extra}";
        }

        // The reason must describe the RECOMMENDED node, not the lowest-delay one.
        if (best.RegionMatch)
        {
            if (best.Delay == lowest.Delay)
                return $"延迟最低（{best.Delay} ms），且位于{label}所在地区（{best.RegionName}）";

            var diff = best.Delay - lowest.Delay;
            return $"位于{label}所在地区（{best.RegionName}），延迟 {best.Delay} ms，仅比最低延迟线路高 {diff} ms";
        }

        var matching = tested.Where(c => c.RegionMatch).ToList();
        if (matching.Count == 0)
            return $"没有{label}所在地区的可用线路，已按延迟最低选择（{lowest.Delay} ms）";

        // A matching node exists but the recommended (lowest-delay) node is not one of them.
        // Describe the best MATCHING node, not the recommended unmatched one.
        var m = matching.OrderBy(c => c.Delay).First();
        return $"{label}所在地区的线路（{m.RegionName} {m.Delay} ms）比最低延迟线路慢 {regionPenaltyMs} ms 以上，已选择延迟最低的线路（{best.Delay} ms）";
    }

    private static bool HasCnAndIntl(Game? game)
    {
        if (game is null)
            return false;
        var hasCn = game.Regions.Any(r => r.Contains("国服", StringComparison.Ordinal));
        var hasIntl = game.Regions.Any(r => r.Contains("国际服", StringComparison.Ordinal));
        return hasCn && hasIntl;
    }
}
