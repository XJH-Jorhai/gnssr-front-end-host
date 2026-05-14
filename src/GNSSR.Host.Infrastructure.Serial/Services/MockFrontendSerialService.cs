using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Infrastructure.Serial.Services;

public sealed class MockFrontendSerialService : IFrontendSerialService
{
    private readonly IAppLogger _logger;
    private byte _activeProfile = 1;
    private uint _centerFrequencyHz = 1_575_420_000;

    public MockFrontendSerialService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public string? CurrentPort { get; private set; }

    public async Task<IReadOnlyList<string>> DiscoverPortsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);

        return
        [
            "COM3 (Mock Frontend)",
            "COM7 (Mock Frontend Backup)"
        ];
    }

    public async Task ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        CurrentPort = portName;
        IsConnected = true;
        _logger.Info($"Frontend serial link opened on {portName}.");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);

        if (CurrentPort is not null)
        {
            _logger.Info($"Frontend serial link closed on {CurrentPort}.");
        }

        CurrentPort = null;
        IsConnected = false;
    }

    public async Task<FrontendStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);

        return new FrontendStatus
        {
            PllLocked = true,
            AntennaOk = true,
            ActiveProfile = _activeProfile,
            CurrentFrequencyHz = _centerFrequencyHz,
            TcxoFrequencyHz = 26_000_000,
            LastError = 0,
            ProtocolVersion = "1.0",
            FirmwareVersion = "mock-fw-0.1",
            HardwareVersion = "rev-a"
        };
    }

    public async Task LoadDefaultProfileAsync(byte channelMask, byte profileId, CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);
        _activeProfile = profileId;
        _centerFrequencyHz = 1_575_420_000;
        _logger.Info($"Mock frontend profile applied: channelMask=0x{channelMask:X2}, profile=0x{profileId:X2}.");
    }

    public async Task SetCenterFrequencyAsync(byte channelMask, uint centerFrequencyHz, CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);
        _activeProfile = 0x80;
        _centerFrequencyHz = centerFrequencyHz;
        _logger.Info($"Mock frontend center frequency set: channelMask=0x{channelMask:X2}, rf={centerFrequencyHz} Hz.");
    }
}
