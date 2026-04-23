using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using GNSSR.Host.Core.Services;
using GNSSR.Host.UI.Infrastructure;

namespace GNSSR.Host.UI.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly SolidColorBrush NeutralBrush = CreateBrush("#94A3B8");
    private static readonly SolidColorBrush AccentBrush = CreateBrush("#2563EB");
    private static readonly SolidColorBrush SuccessBrush = CreateBrush("#16A34A");
    private static readonly SolidColorBrush WarningBrush = CreateBrush("#D97706");
    private static readonly SolidColorBrush DangerBrush = CreateBrush("#DC2626");

    private readonly IFx3UsbService _fx3UsbService;
    private readonly IFrontendSerialService _frontendSerialService;
    private readonly ICaptureSessionService _captureSessionService;
    private readonly FileNamingPolicy _fileNamingPolicy;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _applicationTokenSource = new();

    private bool _isBusy;
    private bool _isInitialized;
    private bool _fx3Connected;
    private bool _frontendConnected;
    private string _fileNamePrefix = "capture";
    private string _outputDirectory = BuildDefaultOutputDirectory();
    private string _appStateText = "等待连接";
    private string _fx3ConnectionText = "未连接";
    private string _frontendConnectionText = "未连接";
    private string _captureStateText = "未采集";
    private string _fx3ActiveText = "未启动";
    private string _usbSpeedText = "--";
    private string _prodEventCountText = "0";
    private string _dmaErrorCountText = "0";
    private string _pllLockText = "未知";
    private string _antennaStatusText = "未知";
    private string _activeProfileText = "未读取";
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
        FileNamingPolicy fileNamingPolicy,
        IAppLogger logger)
    {
        _fx3UsbService = fx3UsbService;
        _frontendSerialService = frontendSerialService;
        _captureSessionService = captureSessionService;
        _fileNamingPolicy = fileNamingPolicy;
        _logger = logger;

        Fx3Devices = [];
        SerialPorts = [];
        Logs = [];

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
        private set
        {
            if (SetProperty(ref _appStateText, value))
            {
                OnPropertyChanged(nameof(AppStateBrush));
            }
        }
    }

    public string Fx3ConnectionText
    {
        get => _fx3ConnectionText;
        private set
        {
            if (SetProperty(ref _fx3ConnectionText, value))
            {
                OnPropertyChanged(nameof(Fx3StatusBrush));
            }
        }
    }

    public string FrontendConnectionText
    {
        get => _frontendConnectionText;
        private set
        {
            if (SetProperty(ref _frontendConnectionText, value))
            {
                OnPropertyChanged(nameof(FrontendStatusBrush));
            }
        }
    }

    public string CaptureStateText
    {
        get => _captureStateText;
        private set
        {
            if (SetProperty(ref _captureStateText, value))
            {
                OnPropertyChanged(nameof(CaptureStatusBrush));
            }
        }
    }

    public string FileNamePrefix
    {
        get => _fileNamePrefix;
        set
        {
            if (SetProperty(ref _fileNamePrefix, value))
            {
                OnPropertyChanged(nameof(PreviewFileName));
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
                OnPropertyChanged(nameof(CurrentFx3DeviceText));
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
                OnPropertyChanged(nameof(CurrentSerialPortText));
                RefreshCommandStates();
            }
        }
    }

    public string Fx3ActiveText
    {
        get => _fx3ActiveText;
        private set
        {
            if (SetProperty(ref _fx3ActiveText, value))
            {
                OnPropertyChanged(nameof(Fx3StreamBrush));
            }
        }
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
        private set
        {
            if (SetProperty(ref _pllLockText, value))
            {
                OnPropertyChanged(nameof(PllLockBrush));
            }
        }
    }

    public string AntennaStatusText
    {
        get => _antennaStatusText;
        private set
        {
            if (SetProperty(ref _antennaStatusText, value))
            {
                OnPropertyChanged(nameof(AntennaStatusBrush));
            }
        }
    }

    public string ActiveProfileText
    {
        get => _activeProfileText;
        private set => SetProperty(ref _activeProfileText, value);
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

    public string CurrentFx3DeviceText => SelectedFx3Device?.DisplayName ?? "未选择";

    public string CurrentSerialPortText => string.IsNullOrWhiteSpace(SelectedSerialPort) ? "未选择" : SelectedSerialPort;

    public string PreviewFileName => $"{_fileNamingPolicy.BuildBaseFileName(FileNamePrefix, DateTimeOffset.Now)}.bin";

    public Brush AppStateBrush => _captureSessionService.IsCapturing
        ? SuccessBrush
        : _isBusy
            ? WarningBrush
            : _fx3Connected && _frontendConnected
                ? AccentBrush
                : NeutralBrush;

    public Brush Fx3StatusBrush => _fx3Connected ? SuccessBrush : NeutralBrush;

    public Brush FrontendStatusBrush => _frontendConnected ? SuccessBrush : NeutralBrush;

    public Brush CaptureStatusBrush => _captureSessionService.IsCapturing
        ? SuccessBrush
        : _isBusy
            ? WarningBrush
            : NeutralBrush;

    public Brush Fx3StreamBrush => Fx3ActiveText switch
    {
        "采集中" => SuccessBrush,
        "待机" => AccentBrush,
        _ => NeutralBrush
    };

    public Brush PllLockBrush => PllLockText switch
    {
        "已锁定" => SuccessBrush,
        "未锁定" => WarningBrush,
        _ => NeutralBrush
    };

    public Brush AntennaStatusBrush => AntennaStatusText switch
    {
        "正常" => SuccessBrush,
        "异常" => DangerBrush,
        _ => NeutralBrush
    };

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _logger.Info("GNSSR 采集控制台已启动。");
        _logger.Info("已加载本机设备发现适配器，当前界面将显示实际串口与 FX3 设备枚举结果。");

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
            AppStateText = "扫描设备";

            var previousDeviceId = SelectedFx3Device?.DeviceId;
            var previousSerialPort = SelectedSerialPort;

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

            SelectedFx3Device = Fx3Devices.FirstOrDefault(device => device.DeviceId == previousDeviceId) ?? Fx3Devices.FirstOrDefault();
            SelectedSerialPort = SerialPorts.FirstOrDefault(port => string.Equals(port, previousSerialPort, StringComparison.OrdinalIgnoreCase)) ?? SerialPorts.FirstOrDefault();

            RefreshReadyState();
            _logger.Info($"设备扫描完成：{Fx3Devices.Count} 个 FX3，{SerialPorts.Count} 个串口。");
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
            Fx3ConnectionText = "已连接";
            Fx3ActiveText = status.Active ? "采集中" : "待机";
            UsbSpeedText = status.UsbSpeed;
            ProdEventCountText = status.ProdEventCount.ToString("N0", CultureInfo.InvariantCulture);
            DmaErrorCountText = status.DmaErrorCount.ToString("N0", CultureInfo.InvariantCulture);
            RefreshReadyState();
        });
    }

    private async Task DisconnectFx3Async()
    {
        await RunBusyAsync(async () =>
        {
            await _fx3UsbService.DisconnectAsync(_applicationTokenSource.Token);
            _fx3Connected = false;
            Fx3ConnectionText = "未连接";
            Fx3ActiveText = "未启动";
            UsbSpeedText = "--";
            ProdEventCountText = "0";
            DmaErrorCountText = "0";
            RefreshReadyState();
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
            FrontendConnectionText = "已连接";
            PllLockText = status.PllLocked ? "已锁定" : "未锁定";
            AntennaStatusText = status.AntennaOk ? "正常" : "异常";
            ActiveProfileText = $"配置 {status.ActiveProfile}";
            FrontendLastErrorText = $"0x{status.LastError:X4}";
            RefreshReadyState();
        });
    }

    private async Task DisconnectFrontendAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _frontendSerialService.DisconnectAsync(_applicationTokenSource.Token);
            _frontendConnected = false;
            FrontendConnectionText = "未连接";
            PllLockText = "未知";
            AntennaStatusText = "未知";
            ActiveProfileText = "未读取";
            FrontendLastErrorText = "0x0000";
            RefreshReadyState();
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
            AppStateText = "正在启动";
            CaptureStateText = "准备中";

            var fx3Status = await _fx3UsbService.GetStatusAsync(_applicationTokenSource.Token);
            _currentSession = await _captureSessionService.StartAsync(
                FileNamePrefix,
                OutputDirectory,
                SelectedFx3Device,
                fx3Status,
                _applicationTokenSource.Token);

            Fx3ActiveText = "采集中";
            CurrentFileName = Path.GetFileName(_currentSession.BinPath);
            SessionStartText = _currentSession.StartTimeHost.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            CaptureStateText = "采集中";
            AppStateText = "采集中";

            _logger.Info($"开始采集：{CurrentFileName} -> {OutputDirectory}");
        });
    }

    private async Task StopCaptureAsync()
    {
        await RunBusyAsync(async () =>
        {
            AppStateText = "正在停止";
            CaptureStateText = "停止中";

            var completedSession = await _captureSessionService.StopAsync("user_requested", _applicationTokenSource.Token);
            if (completedSession is not null)
            {
                _currentSession = completedSession;
                _logger.Info($"元数据已写入：{completedSession.JsonPath}");
            }

            Fx3ActiveText = "待机";
            CaptureStateText = "已停止";
            RefreshReadyState();
        });
    }

    private async Task ResetStreamAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _fx3UsbService.ResetStreamAsync(_applicationTokenSource.Token);
            Fx3ActiveText = "待机";
            ProdEventCountText = "0";
            DmaErrorCountText = "0";
            _logger.Info("已重置 FX3 数据流。");
        });
    }

    private void UseRecommendedOutputDirectory()
    {
        OutputDirectory = BuildDefaultOutputDirectory();
        _logger.Info($"输出目录已设为：{OutputDirectory}");
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        try
        {
            _isBusy = true;
            RefreshCommandStates();
            RaiseStatusIndicatorsChanged();
            await action();
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("操作已取消。");
            AppStateText = "操作已取消";
        }
        catch (Exception exception)
        {
            _logger.Error(exception.Message);
            AppStateText = "请检查日志";
            CaptureStateText = _captureSessionService.IsCapturing ? "采集中" : "已停止";
        }
        finally
        {
            _isBusy = false;
            RefreshCommandStates();
            RaiseStatusIndicatorsChanged();
        }
    }

    private bool CanStartCapture()
    {
        return !_isBusy &&
               _fx3Connected &&
               _frontendConnected &&
               !_captureSessionService.IsCapturing &&
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

    private void RefreshReadyState()
    {
        AppStateText = _fx3Connected && _frontendConnected
            ? "可开始采集"
            : _fx3Connected
                ? "等待前端连接"
                : _frontendConnected
                    ? "等待 FX3 连接"
                    : "等待连接";
    }

    private void RaiseStatusIndicatorsChanged()
    {
        OnPropertyChanged(nameof(AppStateBrush));
        OnPropertyChanged(nameof(Fx3StatusBrush));
        OnPropertyChanged(nameof(FrontendStatusBrush));
        OnPropertyChanged(nameof(CaptureStatusBrush));
        OnPropertyChanged(nameof(Fx3StreamBrush));
        OnPropertyChanged(nameof(PllLockBrush));
        OnPropertyChanged(nameof(AntennaStatusBrush));
        OnPropertyChanged(nameof(PreviewFileName));
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
        double scaledSize = bytes;
        var unitIndex = 0;

        while (scaledSize >= 1024 && unitIndex < units.Length - 1)
        {
            scaledSize /= 1024;
            unitIndex++;
        }

        return $"{scaledSize:0.##} {units[unitIndex]}";
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
