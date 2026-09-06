namespace Netch.Core.Models.Modes;

[Flags]
public enum ModeFeature
{
    None = 0,
    SupportIPv6 = 1 << 0,
    SupportSocks5Auth = 1 << 1
}
