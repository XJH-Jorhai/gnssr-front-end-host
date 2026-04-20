using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Infrastructure.USB.Services;

public sealed class MockFx3UsbService : IFx3UsbService
{
    private readonly IAppLogger _logger;

    public MockFx3UsbService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public Fx3DeviceInfo? CurrentDevice { get; private set; }

    public async Task<IReadOnlyList<Fx3DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);

        return
        [
            new Fx3DeviceInfo
            {
                DeviceId = "FX3-MOCK-001",
                DisplayName = "CYUSB3014 FX3 Mock Device",
                Vid = "0x04B4",
                Pid = "0x00F1",
                InterfaceDescription = "Vendor-specific bulk IN endpoint 0x81"
            }
        ];
    }

    public async Task ConnectAsync(Fx3DeviceInfo device, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        CurrentDevice = device;
        IsConnected = true;
        _logger.Info($"FX3 device connected: {device.DisplayName}.");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);

        if (CurrentDevice is not null)
        {
            _logger.Info($"FX3 device disconnected: {CurrentDevice.DisplayName}.");
        }

        CurrentDevice = null;
        IsConnected = false;
    }

    public async Task<Fx3Status> GetStatusAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);

        return new Fx3Status
        {
            Active = false,
            UsbSpeed = "SuperSpeed",
            ProdEventCount = 128,
            DmaErrorCount = 0
        };
    }

    public async Task ResetStreamAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);
        _logger.Warning("FX3 RESET_STREAM issued through the mock driver.");
    }
}
