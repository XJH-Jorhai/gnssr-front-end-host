using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Core.Abstractions;

public interface ICaptureSessionService
{
    event EventHandler<CaptureMetrics>? MetricsUpdated;

    bool IsCapturing { get; }

    CaptureMetrics CurrentMetrics { get; }

    CaptureSessionInfo? CurrentSession { get; }

    Task<CaptureSessionInfo> StartAsync(
        string operatorName,
        string outputDirectory,
        Fx3DeviceInfo device,
        Fx3Status fx3Status,
        CancellationToken cancellationToken);

    Task<CaptureSessionInfo?> StopAsync(string stopReason, CancellationToken cancellationToken);
}
