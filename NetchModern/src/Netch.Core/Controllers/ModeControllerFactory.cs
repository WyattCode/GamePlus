using Netch.Core.Abstractions;
using Netch.Core.Models.Modes;

namespace Netch.Core.Controllers;

public sealed class ModeControllerFactory : IModeControllerFactory
{
    public IModeController Create(ModeType type) => type switch
    {
        ModeType.ProcessMode => new RedirectorModeController(),
        ModeType.TunMode => new TunModeController(),
        ModeType.ShareMode => throw new NotSupportedException("Share mode (pcap) is not implemented yet."),
        ModeType.WebMode => throw new NotSupportedException("Web mode is not implemented yet."),
        _ => throw new NotSupportedException($"Unknown mode type: {type}")
    };
}
