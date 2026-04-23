using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using Microsoft.Win32;

namespace GNSSR.Host.Infrastructure.Serial.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFrontendSerialService : IFrontendSerialService
{
    private const string SerialCommRegistryPath = @"HARDWARE\DEVICEMAP\SERIALCOMM";
    private static readonly Regex ComPortRegex = new(@"^COM(?<number>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAppLogger _logger;

    public WindowsFrontendSerialService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public string? CurrentPort { get; private set; }

    public Task<IReadOnlyList<string>> DiscoverPortsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ports = EnumerateSerialPorts()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPortSortNumber)
            .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ports.Length == 0)
        {
            _logger.Warning("未发现可用串口。");
        }

        return Task.FromResult<IReadOnlyList<string>>(ports);
    }

    public Task ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CurrentPort = portName;
        IsConnected = true;
        _logger.Info($"前端串口已选择：{portName}。");

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (CurrentPort is not null)
        {
            _logger.Info($"前端串口已断开：{CurrentPort}。");
        }

        CurrentPort = null;
        IsConnected = false;

        return Task.CompletedTask;
    }

    public Task<FrontendStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new FrontendStatus
        {
            PllLocked = false,
            AntennaOk = false,
            ActiveProfile = 0,
            CurrentFrequencyHz = 0,
            TcxoFrequencyHz = 0,
            LastError = 0,
            ProtocolVersion = "待读取",
            FirmwareVersion = "待读取",
            HardwareVersion = "待读取"
        });
    }

    private static IEnumerable<string> EnumerateSerialPorts()
    {
        using var serialCommKey = Registry.LocalMachine.OpenSubKey(SerialCommRegistryPath);
        if (serialCommKey is null)
        {
            yield break;
        }

        foreach (var valueName in serialCommKey.GetValueNames())
        {
            if (serialCommKey.GetValue(valueName) is string portName &&
                !string.IsNullOrWhiteSpace(portName))
            {
                yield return portName.Trim();
            }
        }
    }

    private static int GetPortSortNumber(string portName)
    {
        var match = ComPortRegex.Match(portName);
        return match.Success && int.TryParse(match.Groups["number"].Value, out var number)
            ? number
            : int.MaxValue;
    }
}
