namespace Netch.Core.Services.Diagnostics;

/// <summary>Pure matching of Windows firewall block rules against program file names.</summary>
public static class FirewallRuleMatcher
{
    /// <summary>
    ///     Returns the rules whose <see cref="FirewallRuleInfo.ApplicationPath" /> file name matches one
    ///     of <paramref name="programFileNames" /> (case-insensitive). Rules without a path never match.
    /// </summary>
    public static IReadOnlyList<FirewallRuleInfo> Match(IEnumerable<FirewallRuleInfo> rules, IEnumerable<string> programFileNames)
    {
        var names = new HashSet<string>(programFileNames, StringComparer.OrdinalIgnoreCase);
        var result = new List<FirewallRuleInfo>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.ApplicationPath))
                continue;

            // Split on both separators so Windows paths parse on any host OS.
            var path = rule.ApplicationPath.Trim().Trim('"');
            var file = path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
            if (names.Contains(file))
                result.Add(rule);
        }

        return result;
    }
}
