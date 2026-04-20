using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Enums;
using GNSSR.Host.Core.Models;
using GNSSR.Host.UI.Infrastructure;

namespace GNSSR.Host.UI.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IFx3UsbService _fx3UsbService;
    private readonly IFrontendSerialService _frontendSerialService;
    private readonly ICaptureSessionService _captureSessionService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _applicationTokenSource = new();

    private bool _isBusy;
    private bool _isInitialized;
    private bool _fx3Connected;
    private bool _frontendConnected;
    private string _operatorName = Environment.UserName;
    private string _outputDirectory = BuildDefaultOutputDirectory();
    private string _appStateText = "Idle / 等待设备";
    private string _fx3ConnectionText = "未连接";
    private string _frontendConnectionText = "未连接";
    private string _captureStateText = "未采集";
    private string _fx3ActiveText = "Inactive";
    private string _usbSpeedText = "--";
    private string _prodEventCountText = "0";
    private string _dmaErrorCountText = "0";
    private string _pllLockText = "Unknown";
    private string _antennaStatusText = "Unknown";
    private string _activeProfileText = "--";
    private string _currentFrequencyText = "--";
    private string _tcxoFrequencyText = "--";
    private string _frontendLastErrorText = "0x0000";
    private string _currentFileName = "尚未开始";
    private string _bytesReceivedText = "0 B";
    private string _bytesWrittenText = "0 B";
    private string _writeRateText = "0 B/s";
    private string _sessionStartText = "--";
    private string _ringBufferPeakText = "0%";
    private int _ringBufferUsagePercent;
    private Fx3DeviceInfo? _selectedFx3Device;
    private string? _selectedSerialPort;
    private CaptureSessionInfo? _currentSession;

    public MainViewModel(
        IFx3UsbService fx3UsbService,
        IFrontendSerialService frontendSerialService,
        ICaptureSessionService captureSessionService,
        IAppLogger logger)
    {
        _fx3UsbService = fx3UsbService;
        _frontendSerialService = frontendSerialService;
        _captureSessionService = captureSessionService;
        _logger = logger;

        Fx3Devices = [];
        SerialPorts = [];
        Logs = [];
        BackgroundWorkers =
        [
            "usb_receive_worker: 批量提交 Bulk IN 读取，维护 bytes_received 统计。",
            "disk_writer_worker: 消费 ring buffer，顺序写出 .bin 与 metadata。",
            "status_monitor_worker: 轮询 FX3/Frontend 状态并驱动 UI 告警。"
        ];

        RefreshDevicesCommand = new AsyncRelayCommand(RefreshDevicesAsync, () => !_isBusy && !_captureSessionService.IsCapturing);
        ConnectFx3Command = new AsyncRelayCommand(ConnectFx3Async, () => !_isBusy && !_fx3Connected && SelectedFx3Device is not null);
        DisconnectFx3Command = new AsyncRelayCommand(DisconnectFx3Async, () => !_isBusy && _fx3Connected && !_captureSessionService.IsCapturing);
        ConnectFrontendCommand = new AsyncRelayCommand(ConnectFrontendAsync, () => !_isBusy && !_frontendConnected && !string.IsNullOrWhiteSpace(SelectedSerialPort));
        DisconnectFrontendCommand = new AsyncRelayCommand(DisconnectFrontendAsync, () => !_isBusy && _frontendConnected && !_captureSessionService.IsCapturing);
        StartCaptureCommand = new AsyncRelayCommand(StartCaptureAsync, CanStartCapture);
        StopCaptureCommand = new AsyncRelayCommand(StopCaptureAsync, () => !_isBusy && _captureSessionService.IsCapturing);
        ResetStreamCommand = new AsyncRelayCommand(ResetStreamAsync, () => !_isBusy && _fx3Connected && !_captureSessionService.IsCapturing);
        UseRecommendedOutputDirectoryCommand = new RelayCommand(UseRecommendedOutputDirectory);

        _logger.EntryWritten += OnLogWritten;
        _captureSessionService.MetricsUpdated += OnMetricsUpdated;
    }

    public ObservableCollection<Fx3DeviceInfo> Fx3Devices { get; }

    public ObservableCollection<string> SerialPorts { get; }

    public ObservableCollection<LogEntry> Logs { get; }

    public ObservableCollection<string> BackgroundWorkers { get; }

    public AsyncRelayCommand RefreshDevicesCommand { get; }

    public AsyncRelayCommand ConnectFx3Command { get; }

    public AsyncRelayCommand DisconnectFx3Command { get; }

    public AsyncRelayCommand ConnectFrontendCommand { get; }

    public AsyncRelayCommand DisconnectFrontendCommand { get; }

    public AsyncRelayCommand StartCaptureCommand { get; }

    public AsyncRelayCommand StopCaptureCommand { get; }

    public AsyncRelayCommand ResetStreamCommand { get; }

    public RelayCommand UseRecommendedOutputDirectoryCommand { get; }

    public string AppStateText
    {
        get => _appStateText;
        private set => SetProperty(ref _appStateText, value);
    }

    public string Fx3ConnectionText
    {
        get => _fx3ConnectionText;
        private set => SetProperty(ref _fx3ConnectionText, value);
    }

    public string FrontendConnectionText
    {
        get => _frontendConnectionText;
        private set => SetProperty(ref _frontendConnectionText, value);
    }

    public string CaptureStateText
    {
        get => _captureStateText;
        private set => SetProperty(ref _captureStateText, value);
    }

    public string OperatorName
    {
        get => _operatorName;
        set
        {
            if (SetProperty(ref _operatorName, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public Fx3DeviceInfo? SelectedFx3Device
    {
        get => _selectedFx3Device;
        set
        {
            if (SetProperty(ref _selectedFx3Device, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string? SelectedSerialPort
    {
        get => _selectedSerialPort;
        set
        {
            if (SetProperty(ref _selectedSerialPort, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string Fx3ActiveText
    {
        get => _fx3ActiveText;
        private set => SetProperty(ref _fx3ActiveText, value);
    }

    public string UsbSpeedText
    {
        get => _usbSpeedText;
        private set => SetProperty(ref _usbSpeedText, value);
    }

    public string ProdEventCountText
    {
        get => _prodEventCountText;
        private set => SetProperty(ref _prodEventCountText, value);
    }

    public string DmaErrorCountText
    {
        get => _dmaErrorCountText;
        private set => SetProperty(ref _dmaErrorCountText, value);
    }

    public string PllLockText
    {
        get => _pllLockText;
        private set => SetProperty(ref _pllLockText, value);
    }

    public string AntennaStatusText
    {
        get => _antennaStatusText;
        private set => SetProperty(ref _antennaStatusText, value);
    }

    public string ActiveProfileText
    {
        get => _activeProfileText;
        private set => SetProperty(ref _activeProfileText, value);
    }

    public string CurrentFrequencyText
    {
        get => _currentFrequencyText;
        private set => SetProperty(ref _currentFrequencyText, value);
    }

    public string TcxoFrequencyText
    {
        get => _tcxoFrequencyText;
        private set => SetProperty(ref _tcxoFrequencyText, value);
    }

    public string FrontendLastErrorText
    {
        get => _frontendLastErrorText;
        private set => SetProperty(ref _frontendLastErrorText, value);
    }

    public string CurrentFileName
    {
        get => _currentFileName;
        private set => SetProperty(ref _currentFileName, value);
    }

    public string BytesReceivedText
    {
        get => _bytesReceivedText;
        private set => SetProperty(ref _bytesReceivedText, value);
    }

    public string BytesWrittenText
    {
        get => _bytesWrittenText;
        private set => SetProperty(ref _bytesWrittenText, value);
    }

    public string WriteRateText
    {
        get => _writeRateText;
        private set => SetProperty(ref _writeRateText, value);
    }

    public string SessionStartText
    {
        get => _sessionStartText;
        private set => SetProperty(ref _sessionStartText, value);
    }

    public string RingBufferPeakText
    {
        get => _ringBufferPeakText;
        private set => SetProperty(ref _ringBufferPeakText, value);
    }

    public int RingBufferUsagePercent
    {
        get => _ringBufferUsagePercent;
        private set => SetProperty(ref _ringBufferUsagePercent, value);
    }

    public string ThemeDescription => "WPF UI 4.2.0 / Fluent Light";

    public string StackDescription => ".NET 8 + WPF + MVVM shell";

    public string ThroughputTargetText => "26 MB/s stream / 30 MB/s sustained disk";

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _logger.Info("GNSSR Host skeleton bootstrapped.");
        _logger.Info("Current infrastructure layer uses mock FX3/serial/capture services for UI and workflow validation.");

        await RefreshDevicesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _captureSessionService.MetricsUpdated -= OnMetricsUpdated;
        _logger.EntryWritten -= OnLogWritten;

        if (_captureSessionService.IsCapturing)
        {
            try
            {
                await StopCaptureAsync();
            }
            catch
            {
            }
        }

        _applicationTokenSource.Cancel();
        _applicationTokenSource.Dispose();
    }

    private async Task RefreshDevicesAsync()
    {
        await RunBusyAsync(async () =>
        {
            AppStateText = "Discovering / 扫描设备中";
            Fx3Devices.Clear();
            SerialPorts.Clear();

            var devices = await _fx3UsbService.DiscoverAsync(_applicationTokenSource.Token);
            foreach (var device in devices)
            {
                Fx3Devices.Add(device);
            }

            var ports = await _frontendSerialService.DiscoverPortsAsync(_applicationTokenSource.Token);
            foreach (var port in ports)
            {
                SerialPorts.Add(port);
            }

            SelectedFx3Device ??= Fx3Devices.FirstOrDefault();
            SelectedSerialPort ??= SerialPorts.FirstOrDefault();

            AppStateText = "Idle / 等待连接";
            _logger.Info($"Discovery completed: {Fx3Devices.Count} FX3 device(s), {SerialPorts.Count} serial port(s).");
        });
    }

    private async Task ConnectFx3Async()
    {
        if (SelectedFx3Device is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _fx3UsbService.ConnectAsync(SelectedFx3Device, _applicationTokenSource.Token);
            var status = await _fx3UsbService.GetStatusAsync(_applicationTokenSource.Token);

            _fx3Connected = true;
            Fx3ConnectionText = $"{SelectedFx3Device.DisplayName} / 已连接";
            Fx3ActiveText = status.Active ? "Streaming" : "Standby";
            UsbSpeedText = status.UsbSpeed;
            ProdEventCountText = status.ProdEventCount.ToString("N0", CultureInfo.InvariantCulture);
            DmaErrorCountText = status.DmaErrorCount.ToString("N0", CultureInfo.InvariantCulture);
            AppStateText = _frontendConnected ? "ReadyToCapture / 可开始采集" : "DeviceReady / FX3 已就绪";
        });
    }

    private async Task DisconnectFx3Async()
    {
        await RunBusyAsync(async () =>
        {
            await _fx3UsbService.DisconnectAsync(_applicationTokenSource.Token);
            _fx3Connected = false;
            Fx3ConnectionText = "未连接";
            Fx3ActiveText = "Inactive";
            UsbSpeedText = "--";
            ProdEventCountText = "0";
            DmaErrorCountText = "0";
            AppStateText = _frontendConnected ? "FrontendReady / 等待 FX3" : "Idle / 等待连接";
        });
    }

    private async Task ConnectFrontendAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSerialPort))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _frontendSerialService.ConnectAsync(SelectedSerialPort, _applicationTokenSource.Token);
            var status = await _frontendSerialService.GetStatusAsync(_applicationTokenSource.Token);

            _frontendConnected = true;
            FrontendConnectionText = $"{SelectedSerialPort} / 已连接";
            PllLockText = status.PllLocked ? "Locked" : "Unlocked";
            AntennaStatusText = status.AntennaOk ? "Normal" : "Fault";
            ActiveProfileText = $"Profile {status.ActiveProfile}";
            CurrentFrequencyText = $"{status.CurrentFrequencyHz:N0} Hz";
            TcxoFrequencyText = $"{status.TcxoFrequencyHz:N0} Hz";
            FrontendLastErrorText = $"0x{status.LastError:X4}";
            AppStateText = _fx3Connected ? "ReadyToCapture / 可开始采集" : "FrontendReady / 串口链路已就绪";
        });
    }

    private async Task DisconnectFrontendAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _frontendSerialService.DisconnectAsync(_applicationTokenSource.Token);
            _frontendConnected = false;
            FrontendConnectionText = "未连接";
            PllLockText = "Unknown";
            AntennaStatusText = "Unknown";
            ActiveProfileText = "--";
            CurrentFrequencyText = "--";
            TcxoFrequencyText = "--";
            FrontendLastErrorText = "0x0000";
            AppStateText = _fx3Connected ? "DeviceReady / 仅 FX3 已连接" : "Idle / 等待连接";
        });
    }

    private async Task StartCaptureAsync()
    {
        if (!CanStartCapture() || SelectedFx3Device is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            AppStateText = "StartingCapture / 启动采集中";
            CaptureStateText = "Starting";

            var fx3Status = await _fx3UsbService.GetStatusAsync(_applicationTokenSource.Token);
            _currentSession = await _captureSessionService.StartAsync(
                OperatorName,
                OutputDirectory,
                SelectedFx3Device,
                fx3Status,
                _applicationTokenSource.Token);

            Fx3ActiveText = "Streaming";
            CurrentFileName = Path.GetFileName(_currentSession.BinPath);
            SessionStartText = _currentSession.StartTimeHost.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            CaptureStateText = "Capturing";
            AppStateText = "Capturing / 采集中";

            _logger.Info($"Capture started for operator '{OperatorName}' to '{OutputDirectory}'.");
        });
    }

    private async Task StopCaptureAsync()
    {
        await RunBusyAsync(async () =>
        {
            AppStateText = "StoppingCapture / 正在停止";
            CaptureStateText = "Stopping";

            var completedSession = await _captureSessionService.StopAsync("user_requested", _applicationTokenSource.Token);
            if (completedSession is not null)
            {
                _currentSession = completedSession;
                _logger.Info($"Metadata written: {completedSession.JsonPath}");
            }

            Fx3ActiveText = "Standby";
            CaptureStateText = "Stopped";
            AppStateText = _fx3Connected && _frontendConnected ? "ReadyToCapture / 可再次开始采集" : "Idle / 等待连接";
        });
    }

    private async Task ResetStreamAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _fx3UsbService.ResetStreamAsync(_applicationTokenSource.Token);
            Fx3ActiveText = "Standby";
            ProdEventCountText = "0";
            DmaErrorCountText = "0";
        });
    }

    private void UseRecommendedOutputDirectory()
    {
        OutputDirectory = BuildDefaultOutputDirectory();
        _logger.Info($"Output directory set to '{OutputDirectory}'.");
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        try
        {
            _isBusy = true;
            RefreshCommandStates();
            await action();
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Operation canceled.");
            AppStateText = "Error / 操作已取消";
        }
        catch (Exception exception)
        {
            _logger.Error(exception.Message);
            AppStateText = "Error / 请检查日志";
            CaptureStateText = _captureSessionService.IsCapturing ? "Capturing" : "Stopped";
        }
        finally
        {
            _isBusy = false;
            RefreshCommandStates();
        }
    }

    private bool CanStartCapture()
    {
        return !_isBusy &&
               _fx3Connected &&
               _frontendConnected &&
               !_captureSessionService.IsCapturing &&
               !string.IsNullOrWhiteSpace(OperatorName) &&
               !string.IsNullOrWhiteSpace(OutputDirectory);
    }

    private void OnMetricsUpdated(object? sender, CaptureMetrics metrics)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            BytesReceivedText = FormatBytes(metrics.BytesReceived);
            BytesWrittenText = FormatBytes(metrics.BytesWritten);
            WriteRateText = $"{FormatBytes((long)metrics.WriteRateBytesPerSecond)}/s";
            RingBufferUsagePercent = metrics.RingBufferUsagePercent;
            RingBufferPeakText = $"{metrics.RingBufferPeakPercent}%";
            ProdEventCountText = (metrics.BytesReceived / 1_048_576L).ToString("N0", CultureInfo.InvariantCulture);
        });
    }

    private void OnLogWritten(object? sender, LogEntry entry)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Logs.Insert(0, entry);
            while (Logs.Count > 200)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
        });
    }

    private void RefreshCommandStates()
    {
        RefreshDevicesCommand.RaiseCanExecuteChanged();
        ConnectFx3Command.RaiseCanExecuteChanged();
        DisconnectFx3Command.RaiseCanExecuteChanged();
        ConnectFrontendCommand.RaiseCanExecuteChanged();
        DisconnectFrontendCommand.RaiseCanExecuteChanged();
        StartCaptureCommand.RaiseCanExecuteChanged();
        StopCaptureCommand.RaiseCanExecuteChanged();
        ResetStreamCommand.RaiseCanExecuteChanged();
        UseRecommendedOutputDirectoryCommand.RaiseCanExecuteChanged();
    }

    private static string BuildDefaultOutputDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = string.IsNullOrWhiteSpace(documents)
            ? AppContext.BaseDirectory
            : documents;

        return Path.Combine(root, "GNSSR", "Captures");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = bytes;
        var unitIndex = 0;
        double scaledSize = size;

        while (scaledSize >= 1024 && unitIndex < units.Length - 1)
        {
            scaledSize /= 1024;
            unitIndex++;
        }

        return $"{scaledSize:0.##} {units[unitIndex]}";
    }
}
