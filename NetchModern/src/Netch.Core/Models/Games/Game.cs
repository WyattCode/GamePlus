using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Games;

public enum GameCategory
{
    Hot,
    Pc,
    Console,
    Platform
}

/// <summary>
///     A game (or game platform) in the acceleration library. Its <see cref="ProxyDomains" /> /
///     <see cref="ProxyProcesses" /> are the "traffic rules" that decide what gets routed through a node.
/// </summary>
public partial class Game : ObservableObject
{
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Stable id (slug).</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string EnglishName { get; set; } = string.Empty;

    /// <summary>Search aliases (pinyin / abbreviations / alternate names).</summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>Emoji used as the game icon (no external assets needed).</summary>
    public string Icon { get; set; } = "🎮";

    /// <summary>Brand accent color (hex, e.g. "#FF4655"). Empty → derived from a stable palette.</summary>
    public string AccentColor { get; set; } = string.Empty;

    public GameCategory Category { get; set; } = GameCategory.Pc;

    /// <summary>Supported regions, e.g. "国服", "国际服", "亚服".</summary>
    public List<string> Regions { get; set; } = new();

    public List<string> Platforms { get; set; } = new();

    /// <summary>Domains routed through the accelerator (the rest goes direct).</summary>
    public List<string> ProxyDomains { get; set; } = new();

    /// <summary>IPs / CIDR ranges routed through the accelerator, for games that connect by raw IP.</summary>
    public List<string> ProxyIps { get; set; } = new();

    /// <summary>Process names for this game (used by driver/TUN modes for per-process capture).</summary>
    public List<string> ProxyProcesses { get; set; } = new();

    /// <summary>Total routing rules (domains + IPs) — what actually gets accelerated.</summary>
    [JsonIgnore]
    public int RuleCount => ProxyDomains.Count + ProxyIps.Count;

    /// <summary>User-added game (persisted, editable, deletable). Seed games are false.</summary>
    public bool IsCustom { get; set; }

    [JsonIgnore]
    public string CategoryName => Category switch
    {
        GameCategory.Hot => "热门",
        GameCategory.Pc => "PC",
        GameCategory.Console => "主机",
        GameCategory.Platform => "平台",
        _ => ""
    };

    [JsonIgnore]
    public string RegionText => Regions.Count == 0 ? "" : string.Join(" · ", Regions);

    /// <summary>Curated vivid palette for cards without an explicit brand color.</summary>
    private static readonly string[] Palette =
    {
        "#5B8FF9", "#7B61FF", "#FF6B6B", "#37C7B0", "#F0A500",
        "#EC5B9C", "#3FB950", "#00AEEF", "#FF8C42", "#9B5DE5"
    };

    /// <summary>Resolved accent color — the brand color if set, else a stable palette pick from the id.</summary>
    [JsonIgnore]
    public string Accent
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AccentColor))
                return AccentColor;

            var seed = string.IsNullOrEmpty(Id) ? Name : Id;
            var hash = 0;
            foreach (var ch in seed)
                hash = (hash * 31 + ch) & 0x7fffffff;
            return Palette[hash % Palette.Length];
        }
    }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        query = query.Trim();
        return Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
