using System.Text.Json.Serialization;

namespace Netch.Core.Models.Settings;

/// <summary>
///     How an extra application's traffic is treated when acceleration runs in process-capture mode.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExtraAppMode
{
    /// <summary>Additionally proxied together with the game.</summary>
    Proxy,

    /// <summary>Kept direct even if a game rule would match it.</summary>
    Bypass
}

/// <summary>
///     An extra application (voice chat, browser, …) that is additionally proxied or explicitly
///     bypassed when acceleration runs in process-capture mode (netfilter Redirector).
/// </summary>
public sealed class ExtraApp
{
    /// <summary>Display name; falls back to the process name when empty.</summary>
    public string Name { get; set; } = "";

    /// <summary>Executable file name, e.g. <c>Discord.exe</c>.</summary>
    public string Process { get; set; } = "";

    public ExtraAppMode Mode { get; set; } = ExtraAppMode.Proxy;

    public bool Enabled { get; set; } = true;
}
