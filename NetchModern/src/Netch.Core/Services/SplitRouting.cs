using System.Text;
using System.Text.RegularExpressions;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Settings;

namespace Netch.Core.Services;

/// <summary>
///     Pure helpers for per-app split routing (分应用分流). Extra applications are turned into
///     Redirector process rules: proxied apps are added to <see cref="Mode.Handle" /> (AIO_ADDNAME),
///     bypassed apps to <see cref="Mode.Bypass" /> (AIO_BYPNAME, checked first by the driver).
/// </summary>
public static class SplitRouting
{
    // Executable file name: letters/digits/space/underscore/dot/plus/minus/parens/brackets, ending in .exe.
    private static readonly Regex ProcessNameRegex =
        new(@"^[A-Za-z0-9 _.+\-()\[\]]{1,100}\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when <paramref name="s" /> is a bare executable file name (no path separators).</summary>
    public static bool IsValidProcessName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        if (s.Contains('/') || s.Contains('\\'))
            return false;
        return ProcessNameRegex.IsMatch(s);
    }

    /// <summary>
    ///     Turns a process name into a Redirector rule: regex metacharacters are escaped and the pattern
    ///     is anchored to the file name, so <c>Discord.exe</c> → <c>(^|\\)Discord\.exe$</c> and does not
    ///     match <c>NotDiscord.exe</c>.
    /// </summary>
    public static string ToRule(string processName)
    {
        var sb = new StringBuilder("(^|\\\\)");
        foreach (var c in processName)
        {
            if (c is '.' or '+' or '(' or ')' or '[' or ']')
                sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('$');
        return sb.ToString();
    }

    /// <summary>
    ///     Builds the process-mode rule set: the game's own process rules verbatim, plus the enabled
    ///     extra apps. Invalid names are skipped, duplicates are collapsed case-insensitively, and a
    ///     process listed both as proxy and bypass ends up in <see cref="Mode.Bypass" /> only.
    /// </summary>
    public static Mode BuildProcessMode(IEnumerable<string> gameProcesses, IEnumerable<ExtraApp> extraApps)
    {
        var mode = new Mode { Type = ModeType.ProcessMode };

        var handle = new List<string>();
        var bypass = new List<string>();
        var seenHandle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBypass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Game rules are kept verbatim (they may be regexes already) and are never removed.
        foreach (var r in gameProcesses)
        {
            if (string.IsNullOrWhiteSpace(r))
                continue;
            if (seenHandle.Add(r))
                handle.Add(r);
        }

        var gameRuleSet = new HashSet<string>(handle, StringComparer.OrdinalIgnoreCase);

        var proxyRules = new List<string>();
        var bypassRules = new List<string>();

        foreach (var app in extraApps)
        {
            if (app is null || !app.Enabled)
                continue;
            if (!IsValidProcessName(app.Process))
                continue;

            var rule = ToRule(app.Process);
            if (app.Mode == ExtraAppMode.Bypass)
            {
                if (seenBypass.Add(rule))
                    bypassRules.Add(rule);
            }
            else
            {
                if (seenHandle.Add(rule))
                    proxyRules.Add(rule);
            }
        }

        // Bypass wins over an extra-app proxy rule (but never over a game rule).
        var bypassSet = new HashSet<string>(bypassRules, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in proxyRules)
        {
            if (bypassSet.Contains(rule))
                continue;
            handle.Add(rule);
        }

        foreach (var rule in bypassRules)
            bypass.Add(rule);

        // A process listed both as proxy and bypass: drop the extra-app proxy rule from Handle.
        // (Game rules are never removed — gameRuleSet is only used to document that intent.)
        _ = gameRuleSet;

        foreach (var r in handle)
            mode.Handle.Add(r);
        foreach (var r in bypass)
            mode.Bypass.Add(r);

        return mode;
    }

    /// <summary>
    ///     Builds the routing input for the sing-box TUN instance: the game's own processes
    ///     (basename, deduped case-insensitively ignoring a trailing ".exe"), the enabled extra
    ///     apps (game rules win over bypass, bypass wins over an extra proxy app) and the
    ///     domain/IP lists, all trimmed, blank-free and deduped case-insensitively in order.
    /// </summary>
    public static TunRouting BuildTunRouting(IEnumerable<string> gameProcesses, IEnumerable<ExtraApp> extraApps,
        IEnumerable<string> proxyDomains, IEnumerable<string> proxyIps, IEnumerable<string>? directDomains,
        IEnumerable<string>? excludeAddresses)
    {
        var proxyProcesses = new List<string>();
        var seenProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Game processes: trim, basename, skip blanks, dedupe ignoring a trailing ".exe"
        // ("steam" and "steam.exe" are one entry; the first spelling is kept).
        foreach (var process in gameProcesses)
        {
            if (string.IsNullOrWhiteSpace(process))
                continue;
            var name = ProcessBaseName(process);
            if (name.Length == 0)
                continue;
            if (seenProcesses.Add(NormalizeProcessName(name)))
                proxyProcesses.Add(name);
        }

        var bypassProcesses = new List<string>();
        var seenBypass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extraProxy = new List<string>();

        foreach (var app in extraApps)
        {
            if (app is null || !app.Enabled)
                continue;
            if (!IsValidProcessName(app.Process))
                continue;

            var name = ProcessBaseName(app.Process);
            if (app.Mode == ExtraAppMode.Bypass)
            {
                if (seenBypass.Add(NormalizeProcessName(name)))
                    bypassProcesses.Add(name);
            }
            else
            {
                extraProxy.Add(name);
            }
        }

        // A bypass app that is one of the game's own processes is dropped (game rules win).
        bypassProcesses.RemoveAll(name => seenProcesses.Contains(NormalizeProcessName(name)));

        // Bypass wins over an extra "proxy" app; proxy apps are appended after the game
        // processes and deduped against everything already routed.
        var bypassSet = new HashSet<string>(bypassProcesses.Select(NormalizeProcessName), StringComparer.OrdinalIgnoreCase);
        foreach (var name in extraProxy)
        {
            if (bypassSet.Contains(NormalizeProcessName(name)))
                continue;
            if (seenProcesses.Add(NormalizeProcessName(name)))
                proxyProcesses.Add(name);
        }

        return new TunRouting(
            proxyProcesses,
            bypassProcesses,
            DedupeList(proxyDomains),
            DedupeList(proxyIps),
            DedupeList(directDomains),
            DedupeList(excludeAddresses));
    }

    /// <summary>The basename of <paramref name="path" /> (anything after the last '\' or '/'), trimmed.</summary>
    private static string ProcessBaseName(string path)
    {
        var trimmed = path.Trim();
        var lastSeparator = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
    }

    /// <summary>The comparison key: the basename with one trailing ".exe" removed case-insensitively.</summary>
    private static string NormalizeProcessName(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>Trims, drops blanks and dedupes case-insensitively, keeping order; null → empty.</summary>
    private static List<string> DedupeList(IEnumerable<string>? source)
    {
        var list = new List<string>();
        if (source is null)
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            var trimmed = item?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                continue;
            if (seen.Add(trimmed))
                list.Add(trimmed);
        }

        return list;
    }
}
