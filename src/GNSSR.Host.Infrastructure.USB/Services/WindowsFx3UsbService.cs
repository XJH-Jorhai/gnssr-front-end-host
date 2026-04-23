using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using Microsoft.Win32;

namespace GNSSR.Host.Infrastructure.USB.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFx3UsbService : IFx3UsbService
{
    private const string UsbRegistryPath = @"SYSTEM\CurrentControlSet\Enum\USB";
    private const string CypressVendorId = "04B4";
    private const string DefaultFx3Pid = "00F1";
    private static readonly Regex VidPidRegex = new(
        @"VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAppLogger _logger;

    public WindowsFx3UsbService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public Fx3DeviceInfo? CurrentDevice { get; private set; }

    public Task<IReadOnlyList<Fx3DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var devices = EnumerateFx3Devices()
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (devices.Length == 0)
        {
            _logger.Warning("未发现 CYUSB3014 FX3 设备。");
        }

        return Task.FromResult<IReadOnlyList<Fx3DeviceInfo>>(devices);
    }

    public Task ConnectAsync(Fx3DeviceInfo device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CurrentDevice = device;
        IsConnected = true;
        _logger.Info($"FX3 设备已选择：{device.DisplayName}。");

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (CurrentDevice is not null)
        {
            _logger.Info($"FX3 设备已断开：{CurrentDevice.DisplayName}。");
        }

        CurrentDevice = null;
        IsConnected = false;

        return Task.CompletedTask;
    }

    public Task<Fx3Status> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new Fx3Status
        {
            Active = false,
            UsbSpeed = IsConnected ? "待读取" : "Unknown",
            ProdEventCount = 0,
            DmaErrorCount = 0
        });
    }

    public Task ResetStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Info("FX3 数据流重置请求已记录，控制传输将在硬件协议接入后启用。");
        return Task.CompletedTask;
    }

    private static IEnumerable<Fx3DeviceInfo> EnumerateFx3Devices()
    {
        using var usbKey = Registry.LocalMachine.OpenSubKey(UsbRegistryPath);
        if (usbKey is null)
        {
            yield break;
        }

        foreach (var deviceKeyName in usbKey.GetSubKeyNames())
        {
            var vidPid = VidPidRegex.Match(deviceKeyName);
            if (!vidPid.Success)
            {
                continue;
            }

            var vid = vidPid.Groups["vid"].Value.ToUpperInvariant();
            var pid = vidPid.Groups["pid"].Value.ToUpperInvariant();
            if (!string.Equals(vid, CypressVendorId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pid, DefaultFx3Pid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var deviceKey = usbKey.OpenSubKey(deviceKeyName);
            if (deviceKey is null)
            {
                continue;
            }

            foreach (var instanceKeyName in deviceKey.GetSubKeyNames())
            {
                using var instanceKey = deviceKey.OpenSubKey(instanceKeyName);
                if (instanceKey is null)
                {
                    continue;
                }

                var displayName = CleanRegistryText(
                    instanceKey.GetValue("FriendlyName") as string ??
                    instanceKey.GetValue("DeviceDesc") as string) ?? "CYUSB3014 FX3 Front-End";

                yield return new Fx3DeviceInfo
                {
                    DeviceId = $@"USB\{deviceKeyName}\{instanceKeyName}",
                    DisplayName = displayName.Contains("FX3", StringComparison.OrdinalIgnoreCase)
                        ? displayName
                        : $"{displayName} FX3",
                    Vid = $"0x{vid}",
                    Pid = $"0x{pid}",
                    InterfaceDescription = CleanRegistryText(instanceKey.GetValue("DeviceDesc") as string) ??
                                           "Cypress FX3 USB interface"
                };
            }
        }
    }

    private static string? CleanRegistryText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var semicolonIndex = value.LastIndexOf(';');
        return semicolonIndex >= 0 && semicolonIndex < value.Length - 1
            ? value[(semicolonIndex + 1)..].Trim()
            : value.Trim();
    }
}
