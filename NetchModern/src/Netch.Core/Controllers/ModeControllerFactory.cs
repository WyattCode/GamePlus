using Netch.Core.Abstractions;
using Netch.Core.Models.Modes;
using Netch.Core.Services;

namespace Netch.Core.Controllers;

public sealed class ModeControllerFactory : IModeControllerFactory
{
    private readonly ISettingsService? _settings;

    /// <summary>Parameterless ctor for callers without DI (e.g. <c>AcceleratorService</c>'s convenience overload).</summary>
    public ModeControllerFactory()
    {
    }

    public ModeControllerFactory(ISettingsService settings) => _settings = settings;

    public IModeController Create(ModeType type) => type switch
    {
        ModeType.ProcessMode => new RedirectorModeController(),
        ModeType.TunMode => new TunModeController(_settings?.Settings.Dns.OtherDns),
        _ => throw new NotSupportedException($"Unknown mode type: {type}")
    };

    /// <inheritdoc />
    public bool IsAvailable(ModeType type) => type switch
    {
        ModeType.ProcessMode => ProcessModeSupport.IsAvailable(),
        _ => true
    };
}
