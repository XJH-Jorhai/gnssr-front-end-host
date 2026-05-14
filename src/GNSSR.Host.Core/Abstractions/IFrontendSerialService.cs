using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Core.Abstractions;

public interface IFrontendSerialService
{
    bool IsConnected { get; }

    string? CurrentPort { get; }

    Task<IReadOnlyList<string>> DiscoverPortsAsync(CancellationToken cancellationToken);

    Task ConnectAsync(string portName, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<FrontendStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task LoadDefaultProfileAsync(byte channelMask, byte profileId, CancellationToken cancellationToken);

    Task SetCenterFrequencyAsync(byte channelMask, uint centerFrequencyHz, CancellationToken cancellationToken);
}
