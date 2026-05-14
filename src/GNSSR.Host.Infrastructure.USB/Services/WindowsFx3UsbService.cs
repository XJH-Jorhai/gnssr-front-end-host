using System.IO;
using System.Runtime.Versioning;
using CyUSB;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Infrastructure.USB.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFx3UsbService : IFx3UsbService, IDisposable
{
    private const int CypressVendorId = 0x04B4;
    private const int GnssrProductId = 0x00F1;
    private const byte BulkInEndpointAddress = 0x81;
    private const byte UartOutEndpointAddress = 0x02;
    private const byte UartInEndpointAddress = 0x82;
    private const byte BenchmarkInEndpointAddress = 0x83;
    private const byte StartStreamRequest = 0xB0;
    private const byte StopStreamRequest = 0xB1;
    private const byte ResetStreamRequest = 0xB2;
    private const byte GetStatusRequest = 0xB3;
    private const int StatusPayloadLength = 12;
    private const int BulkReadTimeoutMs = 1000;
    private const int UartTransferTimeoutMs = 50;
    private const int ControlTimeoutMs = 200;

    private readonly IAppLogger _logger;
    private readonly object _controlLock = new();
    private readonly object _bulkLock = new();
    private readonly object _uartLock = new();

    private USBDeviceList? _deviceList;
    private CyUSBDevice? _device;
    private CyBulkEndPoint? _bulkInEndPoint;
    private CyBulkEndPoint? _uartOutEndPoint;
    private CyBulkEndPoint? _uartInEndPoint;

    public WindowsFx3UsbService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public Fx3DeviceInfo? CurrentDevice { get; private set; }

    public Task<IReadOnlyList<Fx3DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var deviceList = new USBDeviceList(CyConst.DEVICES_CYUSB);
        var devices = EnumerateGnssrDevices(deviceList)
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (devices.Length == 0)
        {
            _logger.Warning("No CYUSB3014 GNSSR device with VID_04B4 PID_00F1 was found.");
        }

        return Task.FromResult<IReadOnlyList<Fx3DeviceInfo>>(devices);
    }

    public Task ConnectAsync(Fx3DeviceInfo device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CloseDevice();

        _deviceList = new USBDeviceList(CyConst.DEVICES_CYUSB);
        var selectedDevice = FindDevice(_deviceList, device.DeviceId);
        if (selectedDevice is null)
        {
            CloseDevice();
            throw new InvalidOperationException("The selected FX3 device is no longer present.");
        }

        selectedDevice.ControlEndPt.TimeOut = ControlTimeoutMs;

        var bulkIn = selectedDevice.EndPointOf(BulkInEndpointAddress) as CyBulkEndPoint;
        if (bulkIn is null)
        {
            CloseDevice();
            throw new InvalidOperationException("FX3 Bulk IN endpoint 0x81 was not found.");
        }

        var uartOut = selectedDevice.EndPointOf(UartOutEndpointAddress) as CyBulkEndPoint;
        var uartIn = selectedDevice.EndPointOf(UartInEndpointAddress) as CyBulkEndPoint;
        if (uartOut is null || uartIn is null)
        {
            CloseDevice();
            throw new InvalidOperationException("FX3 UART tunnel endpoints 0x02/0x82 were not found. Download the Stream+UART firmware first.");
        }

        bulkIn.TimeOut = BulkReadTimeoutMs;
        uartOut.TimeOut = UartTransferTimeoutMs;
        uartIn.TimeOut = UartTransferTimeoutMs;

        _device = selectedDevice;
        _bulkInEndPoint = bulkIn;
        _uartOutEndPoint = uartOut;
        _uartInEndPoint = uartIn;
        CurrentDevice = BuildDeviceInfo(selectedDevice, device.DeviceId);
        IsConnected = true;

        _logger.Info($"FX3 device connected: {CurrentDevice.DisplayName}.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AbortAllPipes();

        if (IsConnected && CurrentDevice is not null)
        {
            _logger.Info($"FX3 device disconnected: {CurrentDevice.DisplayName}.");
        }

        CloseDevice();
        return Task.CompletedTask;
    }

    public Task<Fx3Status> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var buffer = new byte[StatusPayloadLength];
        var transferred = VendorControlTransfer(GetStatusRequest, isDeviceToHost: true, buffer, StatusPayloadLength);
        if (transferred != StatusPayloadLength)
        {
            throw new IOException($"GET_STATUS returned {transferred} bytes instead of {StatusPayloadLength}.");
        }

        return Task.FromResult(ParseStatus(buffer));
    }

    public Task StartStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        VendorControlTransfer(StartStreamRequest, isDeviceToHost: false, Array.Empty<byte>(), 0);
        _logger.Info("FX3 START_STREAM accepted.");
        return Task.CompletedTask;
    }

    public Task StopStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        VendorControlTransfer(StopStreamRequest, isDeviceToHost: false, Array.Empty<byte>(), 0);
        AbortDataPipe();
        _logger.Info("FX3 STOP_STREAM accepted.");
        return Task.CompletedTask;
    }

    public Task ResetStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AbortDataPipe();
        VendorControlTransfer(ResetStreamRequest, isDeviceToHost: false, Array.Empty<byte>(), 0);
        AbortDataPipe();
        _logger.Info("FX3 RESET_STREAM accepted.");
        return Task.CompletedTask;
    }

    public Task<int> ReadBulkInAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.Length == 0)
        {
            return Task.FromResult(0);
        }

        var endPoint = _bulkInEndPoint ?? throw new InvalidOperationException("FX3 is not connected.");
        var transferBuffer = buffer;
        var transferLength = buffer.Length;
        bool success;

        lock (_bulkLock)
        {
            success = endPoint.XferData(ref transferBuffer, ref transferLength);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!success)
        {
            throw new IOException(
                $"Bulk IN 0x81 transfer failed. LastError={endPoint.LastError}, NtStatus={endPoint.NtStatus}, UsbdStatus={endPoint.UsbdStatus}.");
        }

        if (!ReferenceEquals(transferBuffer, buffer) && transferLength > 0)
        {
            Buffer.BlockCopy(transferBuffer, 0, buffer, 0, transferLength);
        }

        return Task.FromResult(transferLength);
    }

    public Task WriteFrontendUartAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.Length == 0)
        {
            return Task.CompletedTask;
        }

        var endPoint = _uartOutEndPoint ?? throw new InvalidOperationException("FX3 UART tunnel is not connected.");
        var transferBuffer = buffer;
        var transferLength = buffer.Length;
        bool success;

        lock (_uartLock)
        {
            success = endPoint.XferData(ref transferBuffer, ref transferLength);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!success || transferLength != buffer.Length)
        {
            throw new IOException(
                $"UART tunnel OUT 0x02 transfer failed. Requested={buffer.Length}, transferred={transferLength}, LastError={endPoint.LastError}, NtStatus={endPoint.NtStatus}, UsbdStatus={endPoint.UsbdStatus}.");
        }

        return Task.CompletedTask;
    }

    public Task<int> ReadFrontendUartAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.Length == 0)
        {
            return Task.FromResult(0);
        }

        var endPoint = _uartInEndPoint ?? throw new InvalidOperationException("FX3 UART tunnel is not connected.");
        var transferBuffer = buffer;
        var transferLength = buffer.Length;
        bool success;

        lock (_uartLock)
        {
            success = endPoint.XferData(ref transferBuffer, ref transferLength);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!success)
        {
            return Task.FromResult(0);
        }

        if (!ReferenceEquals(transferBuffer, buffer) && transferLength > 0)
        {
            Buffer.BlockCopy(transferBuffer, 0, buffer, 0, transferLength);
        }

        return Task.FromResult(transferLength);
    }

    public void Dispose()
    {
        CloseDevice();
    }

    private int VendorControlTransfer(byte request, bool isDeviceToHost, byte[] buffer, int length)
    {
        var device = _device ?? throw new InvalidOperationException("FX3 is not connected.");
        var controlEndPoint = device.ControlEndPt;
        var transferBuffer = buffer;
        var transferLength = length;
        bool success;

        lock (_controlLock)
        {
            controlEndPoint.Target = CyConst.TGT_DEVICE;
            controlEndPoint.ReqType = CyConst.REQ_VENDOR;
            controlEndPoint.Direction = isDeviceToHost ? CyConst.DIR_FROM_DEVICE : CyConst.DIR_TO_DEVICE;
            controlEndPoint.ReqCode = request;
            controlEndPoint.Value = 0;
            controlEndPoint.Index = 0;
            controlEndPoint.TimeOut = ControlTimeoutMs;

            success = controlEndPoint.XferData(ref transferBuffer, ref transferLength);
        }

        if (!success)
        {
            throw new IOException(
                $"Vendor request 0x{request:X2} failed. LastError={controlEndPoint.LastError}, NtStatus={controlEndPoint.NtStatus}, UsbdStatus={controlEndPoint.UsbdStatus}.");
        }

        if (isDeviceToHost && !ReferenceEquals(transferBuffer, buffer) && transferLength > 0)
        {
            Buffer.BlockCopy(transferBuffer, 0, buffer, 0, transferLength);
        }

        return transferLength;
    }

    private void CloseDevice()
    {
        AbortAllPipes();
        IsConnected = false;
        CurrentDevice = null;
        _bulkInEndPoint = null;
        _uartOutEndPoint = null;
        _uartInEndPoint = null;
        _device = null;
        _deviceList?.Dispose();
        _deviceList = null;
    }

    private void AbortDataPipe()
    {
        try
        {
            _bulkInEndPoint?.Abort();
            _bulkInEndPoint?.Reset();
        }
        catch
        {
        }
    }

    private void AbortAllPipes()
    {
        AbortDataPipe();

        try
        {
            _uartOutEndPoint?.Abort();
            _uartOutEndPoint?.Reset();
            _uartInEndPoint?.Abort();
            _uartInEndPoint?.Reset();
        }
        catch
        {
        }
    }

    private static IEnumerable<Fx3DeviceInfo> EnumerateGnssrDevices(USBDeviceList deviceList)
    {
        for (var index = 0; index < deviceList.Count; index++)
        {
            if (deviceList[index] is CyUSBDevice device &&
                device.VendorID == CypressVendorId &&
                device.ProductID == GnssrProductId)
            {
                yield return BuildDeviceInfo(device, BuildDeviceId(device, index));
            }
        }
    }

    private static CyUSBDevice? FindDevice(USBDeviceList deviceList, string deviceId)
    {
        CyUSBDevice? fallback = null;

        for (var index = 0; index < deviceList.Count; index++)
        {
            if (deviceList[index] is not CyUSBDevice device ||
                device.VendorID != CypressVendorId ||
                device.ProductID != GnssrProductId)
            {
                continue;
            }

            fallback ??= device;
            if (string.Equals(BuildDeviceId(device, index), deviceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.Path, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return fallback;
    }

    private static Fx3DeviceInfo BuildDeviceInfo(CyUSBDevice device, string deviceId)
    {
        var name = FirstNonEmpty(device.FriendlyName, device.Product, device.Name, "CYUSB3014 FX3 GNSSR Stream");
        var interfaceDescription =
            $"CyUSB vendor interface, data IN 0x{BulkInEndpointAddress:X2}, UART OUT 0x{UartOutEndpointAddress:X2}/IN 0x{UartInEndpointAddress:X2}, benchmark IN 0x{BenchmarkInEndpointAddress:X2}";

        return new Fx3DeviceInfo
        {
            DeviceId = deviceId,
            DisplayName = name,
            Vid = $"0x{device.VendorID:X4}",
            Pid = $"0x{device.ProductID:X4}",
            InterfaceDescription = interfaceDescription
        };
    }

    private static string BuildDeviceId(CyUSBDevice device, int index)
    {
        return string.IsNullOrWhiteSpace(device.Path)
            ? $"VID_{device.VendorID:X4}&PID_{device.ProductID:X4}#{index}"
            : device.Path;
    }

    private static Fx3Status ParseStatus(ReadOnlySpan<byte> status)
    {
        return new Fx3Status
        {
            Active = status[0] != 0,
            UsbSpeed = DecodeUsbSpeed(status[1]),
            ProdEventCount = ReadUInt32LittleEndian(status[4..8]),
            DmaErrorCount = ReadUInt32LittleEndian(status[8..12])
        };
    }

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> value)
    {
        return (uint)(value[0] |
                      (value[1] << 8) |
                      (value[2] << 16) |
                      (value[3] << 24));
    }

    private static string DecodeUsbSpeed(byte speedCode)
    {
        return speedCode switch
        {
            1 => "FullSpeed",
            2 => "HighSpeed",
            3 => "SuperSpeed",
            _ => "Unknown"
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "CYUSB3014 FX3 GNSSR Stream";
    }
}
