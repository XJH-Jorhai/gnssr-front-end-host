using System.Text.Json;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using GNSSR.Host.Core.Services;

namespace GNSSR.Host.Infrastructure.Storage.Services;

public sealed class MockCaptureSessionService : ICaptureSessionService
{
    private readonly FileNamingPolicy _fileNamingPolicy;
    private readonly IAppLogger _logger;

    private CancellationTokenSource? _captureTokenSource;
    private Task? _captureLoop;
    private Fx3DeviceInfo? _currentDevice;
    private Fx3Status? _currentFx3Status;
    private CaptureMetrics _currentMetrics = new();

    public MockCaptureSessionService(FileNamingPolicy fileNamingPolicy, IAppLogger logger)
    {
        _fileNamingPolicy = fileNamingPolicy;
        _logger = logger;
    }

    public event EventHandler<CaptureMetrics>? MetricsUpdated;

    public bool IsCapturing => CurrentSession is not null;

    public CaptureMetrics CurrentMetrics => _currentMetrics.Clone();

    public CaptureSessionInfo? CurrentSession { get; private set; }

    public async Task<CaptureSessionInfo> StartAsync(
        string fileNamePrefix,
        string outputDirectory,
        Fx3DeviceInfo device,
        Fx3Status fx3Status,
        CancellationToken cancellationToken)
    {
        if (IsCapturing)
        {
            throw new InvalidOperationException("A capture session is already running.");
        }

        var paths = _fileNamingPolicy.CreateSessionPaths(fileNamePrefix, outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        await File.WriteAllBytesAsync(paths.BinPath, Array.Empty<byte>(), cancellationToken);

        _currentDevice = device;
        _currentFx3Status = fx3Status;
        _currentMetrics = new CaptureMetrics();
        CurrentSession = new CaptureSessionInfo
        {
            SessionId = Guid.NewGuid().ToString("N"),
            FileNamePrefix = _fileNamingPolicy.SanitizeFileNamePrefix(fileNamePrefix),
            OutputDirectory = outputDirectory,
            BaseFileName = paths.BaseFileName,
            BinPath = paths.BinPath,
            JsonPath = paths.JsonPath,
            StartTimeHost = DateTimeOffset.Now,
            IsIncomplete = false,
            IsSimulated = true
        };

        _captureTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureLoop = Task.Run(() => RunCaptureLoopAsync(_captureTokenSource.Token), CancellationToken.None);

        _logger.Info($"已创建采集文件：{paths.BaseFileName}.bin");
        return CurrentSession;
    }

    public async Task<CaptureSessionInfo?> StopAsync(string stopReason, CancellationToken cancellationToken)
    {
        if (!IsCapturing || CurrentSession is null)
        {
            return null;
        }

        if (_captureTokenSource is not null)
        {
            await _captureTokenSource.CancelAsync();
        }

        if (_captureLoop is not null)
        {
            try
            {
                await _captureLoop.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        CurrentSession.StopTimeHost = DateTimeOffset.Now;
        CurrentSession.StopReason = stopReason;

        await WriteMetadataAsync(CurrentSession, _currentMetrics.Clone(), cancellationToken);

        var completedSession = CurrentSession;
        _logger.Info($"采集已停止：{completedSession.BaseFileName}（{stopReason}）。");

        _captureLoop = null;
        _captureTokenSource?.Dispose();
        _captureTokenSource = null;
        CurrentSession = null;

        return completedSession;
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var deltaBytes = 26_000_000L + Random.Shared.Next(-2_000_000, 2_000_001);
                var writeBytes = Math.Max(deltaBytes - Random.Shared.Next(0, 250_000), 0);
                var usage = Random.Shared.Next(14, 42);

                _currentMetrics.BytesReceived += deltaBytes;
                _currentMetrics.BytesWritten += writeBytes;
                _currentMetrics.WriteRateBytesPerSecond = writeBytes;
                _currentMetrics.RingBufferUsagePercent = usage;
                _currentMetrics.RingBufferPeakPercent = Math.Max(_currentMetrics.RingBufferPeakPercent, usage);

                MetricsUpdated?.Invoke(this, _currentMetrics.Clone());
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteMetadataAsync(
        CaptureSessionInfo session,
        CaptureMetrics metrics,
        CancellationToken cancellationToken)
    {
        var metadata = new
        {
            device = _currentDevice?.DisplayName ?? "unknown",
            vid = _currentDevice?.Vid ?? "unknown",
            pid = _currentDevice?.Pid ?? "unknown",
            usb_speed = _currentFx3Status?.UsbSpeed ?? "unknown",
            sample_clock_hz = 26_000_000,
            data_rate_bytes_per_sec = 26_000_000,
            mapping = "low_nibble=MAX2769_A_D3_D0, high_nibble=MAX2769_B_D3_D0",
            start_time_host = session.StartTimeHost,
            stop_time_host = session.StopTimeHost,
            file_name_prefix = session.FileNamePrefix,
            bin_file = Path.GetFileName(session.BinPath),
            bytes_received = metrics.BytesReceived,
            bytes_written = metrics.BytesWritten,
            dma_error_count_start = _currentFx3Status?.DmaErrorCount ?? 0,
            dma_error_count_stop = _currentFx3Status?.DmaErrorCount ?? 0,
            prod_event_count_start = _currentFx3Status?.ProdEventCount ?? 0,
            prod_event_count_stop = _currentFx3Status?.ProdEventCount ?? 0,
            is_incomplete = session.IsIncomplete,
            stop_reason = session.StopReason,
            is_simulated = session.IsSimulated
        };

        await using var stream = File.Create(session.JsonPath);
        await JsonSerializer.SerializeAsync(stream, metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }, cancellationToken);
    }
}
