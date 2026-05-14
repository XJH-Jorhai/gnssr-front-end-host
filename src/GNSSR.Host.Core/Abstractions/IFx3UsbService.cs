using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Core.Abstractions;

public interface IFx3UsbService
{
    bool IsConnected { get; }

    Fx3DeviceInfo? CurrentDevice { get; }

    Task<IReadOnlyList<Fx3DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);

    Task ConnectAsync(Fx3DeviceInfo device, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<Fx3Status> GetStatusAsync(CancellationToken cancellationToken);

    Task StartStreamAsync(CancellationToken cancellationToken);

    Task StopStreamAsync(CancellationToken cancellationToken);

    Task ResetStreamAsync(CancellationToken cancellationToken);

    Task<int> ReadBulkInAsync(byte[] buffer, CancellationToken cancellationToken);

    Task WriteFrontendUartAsync(byte[] buffer, CancellationToken cancellationToken);

    Task<int> ReadFrontendUartAsync(byte[] buffer, CancellationToken cancellationToken);
}
