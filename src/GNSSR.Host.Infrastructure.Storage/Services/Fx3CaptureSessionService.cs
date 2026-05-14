using System.Buffers;
using System.Text.Json;
using System.Threading.Channels;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using GNSSR.Host.Core.Services;

namespace GNSSR.Host.Infrastructure.Storage.Services;

public sealed class Fx3CaptureSessionService : ICaptureSessionService
{
    private const int UsbReadTransferSizeBytes = 1_048_576;
    private const int RingBufferChunkCapacity = 64;
    private const int StatusPollingIntervalMs = 500;
    private const int MetadataSampleClockHz = 26_000_000;
    private const int MetadataIfCenterHz = 4_000_000;
    private const int MetadataDataRateBytesPerSec = 26_000_000;

    private readonly FileNamingPolicy _fileNamingPolicy;
    private readonly IFx3UsbService _fx3UsbService;
    private readonly IAppLogger _logger;
    private readonly object _metricsLock = new();

    private CancellationTokenSource? _captureTokenSource;
    private Channel<CaptureChunk>? _ringBuffer;
    private Task? _receiveTask;
    private Task? _writerTask;
    private Task? _statusTask;
    private CaptureMetrics _currentMetrics = new();
    private Fx3DeviceInfo? _currentDevice;
    private Fx3Status? _startFx3Status;
    private Fx3Status? _latestFx3Status;
    private string _stopReason = "not_stopped";
    private string? _incompleteReason;
    private bool _isIncomplete;
    private long _lastWrittenForRate;
    private DateTimeOffset _lastRateTimestamp;
    private DateTimeOffset _lastDmaWarningTimestamp;

    public Fx3CaptureSessionService(
        FileNamingPolicy fileNamingPolicy,
        IFx3UsbService fx3UsbService,
        IAppLogger logger)
    {
        _fileNamingPolicy = fileNamingPolicy;
        _fx3UsbService = fx3UsbService;
        _logger = logger;
    }

    public event EventHandler<CaptureMetrics>? MetricsUpdated;

    public bool IsCapturing => CurrentSession is not null;

    public CaptureMetrics CurrentMetrics => CloneMetrics();

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

        if (!_fx3UsbService.IsConnected)
        {
            throw new InvalidOperationException("FX3 is not connected.");
        }

        var paths = _fileNamingPolicy.CreateSessionPaths(fileNamePrefix, outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        _currentDevice = device;
        _startFx3Status = fx3Status;
        _latestFx3Status = fx3Status;
        _stopReason = "not_stopped";
        _incompleteReason = null;
        _isIncomplete = false;
        _lastWrittenForRate = 0;
        _lastRateTimestamp = DateTimeOffset.Now;
        _lastDmaWarningTimestamp = DateTimeOffset.MinValue;

        lock (_metricsLock)
        {
            _currentMetrics = new CaptureMetrics
            {
                ProdEventCount = fx3Status.ProdEventCount,
                DmaErrorCount = fx3Status.DmaErrorCount
            };
        }

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
            IsSimulated = false
        };

        _ringBuffer = Channel.CreateBounded<CaptureChunk>(new BoundedChannelOptions(RingBufferChunkCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        _captureTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await _fx3UsbService.StartStreamAsync(cancellationToken).ConfigureAwait(false);
            var confirmedStatus = await _fx3UsbService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            UpdateFx3Status(confirmedStatus);

            if (!confirmedStatus.Active)
            {
                MarkIncomplete("start_stream_not_active");
                _logger.Warning("FX3 START_STREAM returned successfully, but GET_STATUS.active is still 0.");
            }

            _writerTask = Task.Run(() => RunDiskWriterAsync(CurrentSession.BinPath, _ringBuffer.Reader), CancellationToken.None);
            _receiveTask = Task.Run(() => RunUsbReceiveAsync(_ringBuffer.Writer, _captureTokenSource.Token), CancellationToken.None);
            _statusTask = Task.Run(() => RunStatusMonitorAsync(_captureTokenSource.Token), CancellationToken.None);

            _logger.Info($"Real FX3 capture started: {paths.BaseFileName}.bin");
            PublishMetrics();
            return CurrentSession;
        }
        catch
        {
            _ringBuffer.Writer.TryComplete();
            await SafeStopStreamAsync(CancellationToken.None).ConfigureAwait(false);
            await CleanupFailedStartAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CaptureSessionInfo?> StopAsync(string stopReason, CancellationToken cancellationToken)
    {
        if (!IsCapturing || CurrentSession is null)
        {
            return null;
        }

        _stopReason = string.IsNullOrWhiteSpace(stopReason) ? "stopped" : stopReason;

        await SafeStopStreamAsync(cancellationToken).ConfigureAwait(false);

        if (_captureTokenSource is not null)
        {
            await _captureTokenSource.CancelAsync().ConfigureAwait(false);
        }

        await AwaitWorkerAsync(_receiveTask, "USB receive", cancellationToken).ConfigureAwait(false);
        _ringBuffer?.Writer.TryComplete();
        await AwaitWorkerAsync(_statusTask, "status monitor", cancellationToken).ConfigureAwait(false);
        await AwaitWorkerAsync(_writerTask, "disk writer", cancellationToken).ConfigureAwait(false);

        Fx3Status? stopStatus = null;
        try
        {
            stopStatus = await _fx3UsbService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            UpdateFx3Status(stopStatus);
        }
        catch (Exception exception)
        {
            MarkIncomplete("final_status_failed");
            _logger.Warning($"Unable to read final FX3 status: {exception.Message}");
        }

        CurrentSession.StopTimeHost = DateTimeOffset.Now;
        CurrentSession.StopReason = _isIncomplete
            ? _incompleteReason ?? _stopReason
            : _stopReason;
        CurrentSession.IsIncomplete = _isIncomplete;

        var completedSession = CurrentSession;
        await WriteMetadataAsync(completedSession, CloneMetrics(), stopStatus ?? _latestFx3Status, cancellationToken).ConfigureAwait(false);

        _logger.Info($"Real FX3 capture stopped: {completedSession.BaseFileName} ({completedSession.StopReason}).");

        DisposeCaptureState();
        CurrentSession = null;
        PublishMetrics();

        return completedSession;
    }

    private async Task RunUsbReceiveAsync(ChannelWriter<CaptureChunk> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(UsbReadTransferSizeBytes);
                var bufferQueued = false;
                var bufferReturned = false;

                try
                {
                    var length = await _fx3UsbService.ReadBulkInAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (length <= 0)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        bufferReturned = true;
                        continue;
                    }

                    var chunk = new CaptureChunk(buffer, length);
                    if (!writer.TryWrite(chunk))
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        bufferReturned = true;
                        MarkIncomplete("ring_buffer_overflow");
                        _logger.Error("Ring buffer overflow. Capture will be marked incomplete.");
                        _captureTokenSource?.Cancel();
                        break;
                    }

                    bufferQueued = true;
                    AddBytesReceived(length);
                }
                finally
                {
                    if (!bufferQueued && !bufferReturned)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MarkIncomplete("bulk_read_exception");
            _logger.Error($"Bulk IN receive failed: {exception.Message}");
            _captureTokenSource?.Cancel();
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task RunDiskWriterAsync(string binPath, ChannelReader<CaptureChunk> reader)
    {
        try
        {
            await using var stream = new FileStream(
                binPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                UsbReadTransferSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await foreach (var chunk in reader.ReadAllAsync())
            {
                try
                {
                    await stream.WriteAsync(chunk.Buffer.AsMemory(0, chunk.Length), CancellationToken.None).ConfigureAwait(false);
                    AddBytesWritten(chunk.Length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(chunk.Buffer);
                }
            }

            await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            MarkIncomplete("disk_write_exception");
            _logger.Error($"Disk writer failed: {exception.Message}");
            _captureTokenSource?.Cancel();
        }
    }

    private async Task RunStatusMonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StatusPollingIntervalMs, cancellationToken).ConfigureAwait(false);
                var status = await _fx3UsbService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                UpdateFx3Status(status);

                if (_startFx3Status is not null && status.DmaErrorCount > _startFx3Status.DmaErrorCount)
                {
                    var now = DateTimeOffset.Now;
                    if ((now - _lastDmaWarningTimestamp).TotalSeconds >= 5)
                    {
                        _lastDmaWarningTimestamp = now;
                        MarkIncomplete("dma_error_growth");
                        _logger.Warning($"FX3 DMA error count increased to {status.DmaErrorCount}.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                MarkIncomplete("status_poll_exception");
                _logger.Warning($"FX3 status polling failed: {exception.Message}");
            }
        }
    }

    private void AddBytesReceived(int byteCount)
    {
        lock (_metricsLock)
        {
            _currentMetrics.BytesReceived += byteCount;
            UpdateRingBufferUsageNoLock();
        }

        PublishMetrics();
    }

    private void AddBytesWritten(int byteCount)
    {
        lock (_metricsLock)
        {
            _currentMetrics.BytesWritten += byteCount;
            var now = DateTimeOffset.Now;
            var elapsed = Math.Max((now - _lastRateTimestamp).TotalSeconds, 0.001);

            if (elapsed >= 0.25)
            {
                var writtenDelta = _currentMetrics.BytesWritten - _lastWrittenForRate;
                _currentMetrics.WriteRateBytesPerSecond = writtenDelta / elapsed;
                _lastWrittenForRate = _currentMetrics.BytesWritten;
                _lastRateTimestamp = now;
            }

            UpdateRingBufferUsageNoLock();
        }

        PublishMetrics();
    }

    private void UpdateFx3Status(Fx3Status status)
    {
        _latestFx3Status = status;

        lock (_metricsLock)
        {
            _currentMetrics.ProdEventCount = status.ProdEventCount;
            _currentMetrics.DmaErrorCount = status.DmaErrorCount;
        }

        PublishMetrics();
    }

    private void UpdateRingBufferUsageNoLock()
    {
        var pendingBytes = Math.Max(_currentMetrics.BytesReceived - _currentMetrics.BytesWritten, 0);
        var capacityBytes = (long)RingBufferChunkCapacity * UsbReadTransferSizeBytes;
        var usage = capacityBytes == 0 ? 0 : (int)Math.Min(100, pendingBytes * 100 / capacityBytes);
        _currentMetrics.RingBufferUsagePercent = usage;
        _currentMetrics.RingBufferPeakPercent = Math.Max(_currentMetrics.RingBufferPeakPercent, usage);
    }

    private void PublishMetrics()
    {
        MetricsUpdated?.Invoke(this, CloneMetrics());
    }

    private CaptureMetrics CloneMetrics()
    {
        lock (_metricsLock)
        {
            return _currentMetrics.Clone();
        }
    }

    private void MarkIncomplete(string reason)
    {
        _isIncomplete = true;
        _incompleteReason ??= reason;
        if (CurrentSession is not null)
        {
            CurrentSession.IsIncomplete = true;
            CurrentSession.StopReason = _incompleteReason;
        }
    }

    private async Task SafeStopStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _fx3UsbService.StopStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            MarkIncomplete("stop_stream_failed");
            _logger.Warning($"STOP_STREAM failed: {exception.Message}");
        }
    }

    private async Task AwaitWorkerAsync(Task? task, string workerName, CancellationToken cancellationToken)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, task))
            {
                MarkIncomplete($"{workerName.Replace(' ', '_').ToLowerInvariant()}_timeout");
                _logger.Warning($"{workerName} did not stop within 5 seconds.");
                return;
            }

            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MarkIncomplete($"{workerName.Replace(' ', '_').ToLowerInvariant()}_failed");
            _logger.Warning($"{workerName} stopped with error: {exception.Message}");
        }
    }

    private async Task CleanupFailedStartAsync()
    {
        if (_captureTokenSource is not null)
        {
            await _captureTokenSource.CancelAsync().ConfigureAwait(false);
        }

        await AwaitWorkerAsync(_receiveTask, "USB receive", CancellationToken.None).ConfigureAwait(false);
        await AwaitWorkerAsync(_statusTask, "status monitor", CancellationToken.None).ConfigureAwait(false);
        await AwaitWorkerAsync(_writerTask, "disk writer", CancellationToken.None).ConfigureAwait(false);

        DisposeCaptureState();
        CurrentSession = null;
    }

    private void DisposeCaptureState()
    {
        _captureTokenSource?.Dispose();
        _captureTokenSource = null;
        _ringBuffer = null;
        _receiveTask = null;
        _writerTask = null;
        _statusTask = null;
        _currentDevice = null;
        _startFx3Status = null;
        _latestFx3Status = null;
        _incompleteReason = null;
    }

    private async Task WriteMetadataAsync(
        CaptureSessionInfo session,
        CaptureMetrics metrics,
        Fx3Status? stopStatus,
        CancellationToken cancellationToken)
    {
        var metadata = new
        {
            device = _currentDevice?.DisplayName ?? "unknown",
            vid = _currentDevice?.Vid ?? "unknown",
            pid = _currentDevice?.Pid ?? "unknown",
            usb_speed = stopStatus?.UsbSpeed ?? _startFx3Status?.UsbSpeed ?? "unknown",
            sample_clock_hz = MetadataSampleClockHz,
            if_center_hz = MetadataIfCenterHz,
            data_rate_bytes_per_sec = MetadataDataRateBytesPerSec,
            mapping = "one byte per GPIF clock: low_nibble=direct MAX2769, high_nibble=reflected MAX2769",
            bit_layout = new[]
            {
                "bit0=direct.I0",
                "bit1=direct.I1",
                "bit2=direct.Q0",
                "bit3=direct.Q1",
                "bit4=reflected.I0",
                "bit5=reflected.I1",
                "bit6=reflected.Q0",
                "bit7=reflected.Q1"
            },
            frontend_link = "fx3_uart_tunnel",
            start_time_host = session.StartTimeHost,
            stop_time_host = session.StopTimeHost,
            file_name_prefix = session.FileNamePrefix,
            bin_file = Path.GetFileName(session.BinPath),
            bytes_received = metrics.BytesReceived,
            bytes_written = metrics.BytesWritten,
            dma_error_count_start = _startFx3Status?.DmaErrorCount ?? 0,
            dma_error_count_stop = stopStatus?.DmaErrorCount ?? metrics.DmaErrorCount,
            prod_event_count_start = _startFx3Status?.ProdEventCount ?? 0,
            prod_event_count_stop = stopStatus?.ProdEventCount ?? metrics.ProdEventCount,
            ring_buffer_peak_percent = metrics.RingBufferPeakPercent,
            is_incomplete = session.IsIncomplete,
            stop_reason = session.StopReason,
            is_simulated = session.IsSimulated
        };

        await using var stream = File.Create(session.JsonPath);
        await JsonSerializer.SerializeAsync(stream, metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }, cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct CaptureChunk(byte[] Buffer, int Length);
}
