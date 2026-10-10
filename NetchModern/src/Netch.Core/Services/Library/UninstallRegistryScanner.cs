using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Netch.Core.Services.Library;

/// <summary>A single "Apps &amp; features" entry.</summary>
public record UninstallEntry(
    string Key,
    string DisplayName,
    string? Publisher,
    string? InstallLocation,
    string? DisplayIcon);

/// <summary>Source of uninstall entries — abstracted so tests can inject a fake.</summary>
public interface IUninstallSource
{
    IEnumerable<UninstallEntry> Read();
}

/// <summary>
///     Reads uninstall entries from HKLM (64- and 32-bit views) and HKCU. Windows-only; never throws.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryUninstallSource : IUninstallSource
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IEnumerable<UninstallEntry> Read()
    {
        foreach (var entry in ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64))
            yield return entry;
        foreach (var entry in ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32))
            yield return entry;
        foreach (var entry in ReadHive(RegistryHive.CurrentUser, RegistryView.Default))
            yield return entry;
    }

    private static IEnumerable<UninstallEntry> ReadHive(RegistryHive hive, RegistryView view)
    {
        RegistryKey? baseKey = null;
        try
        {
            baseKey = RegistryKey.OpenBaseKey(hive, view);
        }
        catch
        {
            yield break;
        }

        using (baseKey)
        {
            RegistryKey? uninstall = null;
            try
            {
                uninstall = baseKey.OpenSubKey(UninstallKey);
            }
            catch
            {
                yield break;
            }

            if (uninstall is null)
                yield break;

            using (uninstall)
            {
                string[] subKeys;
                try
                {
                    subKeys = uninstall.GetSubKeyNames();
                }
                catch
                {
                    yield break;
                }

                foreach (var subKeyName in subKeys)
                {
                    UninstallEntry? entry = null;
                    try
                    {
                        using var sub = uninstall.OpenSubKey(subKeyName);
                        if (sub is null)
                            continue;
                        if (sub.GetValue("SystemComponent") is int sc && sc == 1)
                            continue;

                        var displayName = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                            continue;

                        entry = new UninstallEntry(
                            subKeyName,
                            displayName,
                            sub.GetValue("Publisher") as string,
                            sub.GetValue("InstallLocation") as string,
                            sub.GetValue("DisplayIcon") as string);
                    }
                    catch
                    {
                        // skip this entry
                    }

                    if (entry is not null)
                        yield return entry;
                }
            }
        }
    }
}

/// <summary>
///     Scans Windows "Apps &amp; features" entries for games published by known game publishers.
/// </summary>
public sealed class UninstallRegistryScanner : IGameScanner
{
    private static readonly string[] LauncherNames =
    {
        "Battle.net", "EA app", "Ubisoft Connect", "Riot Client"
    };

    private static readonly string[] TencentNonGameMarkers =
    {
        "QQ", "微信", "WeChat", "腾讯会议", "电脑管家"
    };

    private readonly IUninstallSource _source;

    public UninstallRegistryScanner(IUninstallSource source) => _source = source;

    public string Name => "其他";

    public IEnumerable<InstalledGame> Scan(CancellationToken ct)
    {
        IEnumerable<UninstallEntry> entries;
        try
        {
            entries = _source.Read();
        }
        catch
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var game = Map(entry);
            if (game is not null)
                yield return game;
        }
    }

    private static InstalledGame? Map(UninstallEntry entry)
    {
        var name = entry.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var launcher in LauncherNames)
            if (string.Equals(name, launcher, StringComparison.OrdinalIgnoreCase))
                return null;

        var publisher = entry.Publisher ?? "";
        string? source = null;

        if (Contains(publisher, "Blizzard Entertainment"))
            source = "Battle.net";
        else if (Contains(publisher, "Electronic Arts"))
            source = "EA";
        else if (Contains(publisher, "Ubisoft"))
            source = "Ubisoft";
        else if (Contains(publisher, "Riot Games"))
            source = "Riot";
        else if (Contains(publisher, "miHoYo") || Contains(publisher, "HoYoverse") || Contains(publisher, "COGNOSPHERE"))
            source = "其他";
        else if (Contains(publisher, "Tencent"))
        {
            foreach (var marker in TencentNonGameMarkers)
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return null;
            source = "其他";
        }

        if (source is null)
            return null;

        var executables = new List<string>();
        var exe = ExtractExe(entry.DisplayIcon);
        if (exe is not null)
            executables.Add(exe);
        if (executables.Count == 0)
            executables.AddRange(ExecutableFinder.Find(entry.InstallLocation));

        return new InstalledGame(source, $"uninstall:{entry.Key}", name, entry.InstallLocation, executables);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the file name from a DisplayIcon value like <c>"C:\Games\x.exe",0</c>.</summary>
    private static string? ExtractExe(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
            return null;

        var value = displayIcon.Trim();

        // Strip a trailing ",0" / ",-1" index suffix.
        var comma = value.LastIndexOf(',');
        if (comma > 0)
        {
            var suffix = value[(comma + 1)..].Trim();
            if (suffix.Length > 0 && suffix.All(c => char.IsDigit(c) || c == '-'))
                value = value[..comma];
        }

        value = value.Trim().Trim('"');
        if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        var fileName = Path.GetFileName(value.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }
}
