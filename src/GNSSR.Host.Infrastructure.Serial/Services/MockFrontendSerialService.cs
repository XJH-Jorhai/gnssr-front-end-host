using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Infrastructure.Serial.Services;

public sealed class MockFrontendSerialService : IFrontendSerialService
{
    private readonly IAppLogger _logger;

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
            ActiveProfile = 1,
            CurrentFrequencyHz = 1_575_420_000,
            TcxoFrequencyHz = 26_000_000,
            LastError = 0,
            ProtocolVersion = "1.0",
            FirmwareVersion = "mock-fw-0.1",
            HardwareVersion = "rev-a"
        };
    }
}
