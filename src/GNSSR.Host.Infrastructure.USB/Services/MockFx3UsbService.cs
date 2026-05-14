using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Infrastructure.USB.Services;

public sealed class MockFx3UsbService : IFx3UsbService
{
    private readonly IAppLogger _logger;
    private bool _streamActive;
    private uint _prodEventCount;
    private byte[] _lastFrontendRequest = [];

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
            Active = _streamActive,
            UsbSpeed = "SuperSpeed",
            ProdEventCount = _prodEventCount,
            DmaErrorCount = 0
        };
    }

    public async Task StartStreamAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        _streamActive = true;
        _logger.Warning("FX3 START_STREAM issued through the mock driver.");
    }

    public async Task StopStreamAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        _streamActive = false;
        _logger.Warning("FX3 STOP_STREAM issued through the mock driver.");
    }

    public async Task ResetStreamAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);
        _streamActive = false;
        _prodEventCount = 0;
        _logger.Warning("FX3 RESET_STREAM issued through the mock driver.");
    }

    public Task<int> ReadBulkInAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_streamActive)
        {
            return Task.FromResult(0);
        }

        Random.Shared.NextBytes(buffer);
        _prodEventCount++;
        return Task.FromResult(buffer.Length);
    }

    public Task WriteFrontendUartAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        _lastFrontendRequest = buffer.ToArray();
        return Task.CompletedTask;
    }

    public Task<int> ReadFrontendUartAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        if (_lastFrontendRequest.Length == 0 || buffer.Length == 0)
        {
            return Task.FromResult(0);
        }

        _lastFrontendRequest = [];
        return Task.FromResult(0);
    }
}
