using System.Buffers.Binary;
using System.Globalization;
using System.IO.Ports;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using GNSSR.Host.Infrastructure.Serial.Protocol;
using Microsoft.Win32;

namespace GNSSR.Host.Infrastructure.Serial.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFrontendSerialService : IFrontendSerialService
{
    private const string SerialCommRegistryPath = @"HARDWARE\DEVICEMAP\SERIALCOMM";
    private const int BaudRate = 115200;
    private const int DataBits = 8;
    private const int ConnectHelloTimeoutMs = 200;
    private const int NormalCommandTimeoutMs = 100;
    private const int Max2769CommandTimeoutMs = 1000;
    private const int ReadPollTimeoutMs = 20;
    private const int RetriesPerCommand = 3;

    private static readonly Regex ComPortRegex = new(@"^COM(?<number>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _serialLock = new(1, 1);
    private SerialPort? _serialPort;
    private byte _nextSequence = 1;
    private string _protocolVersionText = "1.0";
    private string _firmwareVersionText = "未读取";

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

    public async Task ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("Serial port name cannot be empty.", nameof(portName));
        }

        try
        {
            await OpenSerialPortAsync(portName, cancellationToken).ConfigureAwait(false);

            var hello = await ExecuteCommandAsync(
                FrontendCommand.Hello,
                payload: [],
                expectedPayloadLength: 8,
                timeoutMs: ConnectHelloTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            ApplyHelloPayload(hello.Payload);
            IsConnected = true;
            _logger.Info($"前端串口已连接：{portName}。");
        }
        catch (OperationCanceledException)
        {
            await CloseAfterFailedConnectAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await CloseAfterFailedConnectAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"前端串口连接失败：{exception.Message}", exception);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CloseSerialPortCore(logDisconnect: true);
        }
        finally
        {
            _serialLock.Release();
        }
    }

    public async Task<FrontendStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("前端串口尚未完成 HELLO 握手。");
        }

        var response = await ExecuteCommandAsync(
            FrontendCommand.GetStatus,
            payload: [],
            expectedPayloadLength: 17,
            timeoutMs: NormalCommandTimeoutMs,
            cancellationToken).ConfigureAwait(false);

        var payload = response.Payload;
        return new FrontendStatus
        {
            PllLocked = payload[4] == 1,
            AntennaOk = payload[5] == 1,
            ActiveProfile = payload[6],
            CurrentFrequencyHz = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(7, 4)),
            TcxoFrequencyHz = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(11, 4)),
            LastError = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(15, 2)),
            ProtocolVersion = _protocolVersionText,
            FirmwareVersion = _firmwareVersionText,
            HardwareVersion = "未读取"
        };
    }

    public async Task LoadDefaultProfileAsync(byte channelMask, byte profileId, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("前端串口尚未完成 HELLO 握手。");
        }

        await ExecuteCommandAsync(
            FrontendCommand.LoadDefaultProfile,
            payload: [channelMask, profileId],
            expectedPayloadLength: 4,
            timeoutMs: Max2769CommandTimeoutMs,
            cancellationToken).ConfigureAwait(false);

        _logger.Info($"MAX2769 配置已写入：channelMask=0x{channelMask:X2}, profile=0x{profileId:X2}。");
    }

    public async Task SetCenterFrequencyAsync(byte channelMask, uint centerFrequencyHz, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("前端串口尚未完成 HELLO 握手。");
        }

        var payload = new byte[5];
        payload[0] = channelMask;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1, 4), centerFrequencyHz);

        await ExecuteCommandAsync(
            FrontendCommand.SetCenterFrequency,
            payload,
            expectedPayloadLength: 4,
            timeoutMs: Max2769CommandTimeoutMs,
            cancellationToken).ConfigureAwait(false);

        _logger.Info($"MAX2769 中心频率已写入：channelMask=0x{channelMask:X2}, rf={centerFrequencyHz.ToString("N0", CultureInfo.InvariantCulture)} Hz。");
    }

    private async Task<FrontendFrame> ExecuteCommandAsync(
        FrontendCommand command,
        byte[] payload,
        int expectedPayloadLength,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var serialPort = _serialPort;
            if (serialPort is null || !serialPort.IsOpen)
            {
                throw new InvalidOperationException("前端串口未打开。");
            }

            var sequence = GetNextSequence();
            var request = FrontendProtocolCodec.EncodeRequest(command, sequence, payload);
            var commandName = GetCommandName(command);

            for (var attempt = 1; attempt <= RetriesPerCommand; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                serialPort.DiscardInBuffer();
                await serialPort.BaseStream.WriteAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
                await serialPort.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info($"串口 TX {commandName} seq=0x{sequence:X2}: {FrontendProtocolCodec.FormatHex(request)}");

                try
                {
                    var response = await Task.Run(
                            () => ReadMatchingResponse(serialPort, command, sequence, timeoutMs, cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(false);

                    _logger.Info($"串口 RX {commandName} seq=0x{sequence:X2}: {FrontendProtocolCodec.FormatHex(response.RawBytes)}");

                    if (response.Payload.Length != expectedPayloadLength)
                    {
                        throw new FrontendProtocolException(
                            $"{commandName} response payload length mismatch. Expected {expectedPayloadLength}, got {response.Payload.Length}.");
                    }

                    EnsureSuccessResponse(commandName, response.Payload);
                    return response;
                }
                catch (TimeoutException) when (attempt < RetriesPerCommand)
                {
                    _logger.Warning($"串口 {commandName} 超时，正在重试 {attempt + 1}/{RetriesPerCommand}。");
                }
            }

            throw new TimeoutException($"串口 {commandName} 在 {RetriesPerCommand} 次尝试后仍未收到有效响应。");
        }
        finally
        {
            _serialLock.Release();
        }
    }

    private async Task OpenSerialPortAsync(string portName, CancellationToken cancellationToken)
    {
        SerialPort? newSerialPort = null;

        await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CloseSerialPortCore(logDisconnect: false);

            newSerialPort = new SerialPort(portName, BaudRate, Parity.None, DataBits, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = NormalCommandTimeoutMs,
                WriteTimeout = NormalCommandTimeoutMs,
                DtrEnable = false,
                RtsEnable = false
            };

            newSerialPort.Open();
            newSerialPort.DiscardInBuffer();
            newSerialPort.DiscardOutBuffer();

            _serialPort = newSerialPort;
            newSerialPort = null;
            CurrentPort = portName;
            IsConnected = false;
            _nextSequence = 1;
        }
        finally
        {
            newSerialPort?.Dispose();
            _serialLock.Release();
        }
    }

    private static FrontendFrame ReadMatchingResponse(
        SerialPort serialPort,
        FrontendCommand expectedCommand,
        byte expectedSequence,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        var accumulated = new List<byte>(256);
        var readBuffer = new byte[64];
        var previousReadTimeout = serialPort.ReadTimeout;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remainingMs = (int)Math.Ceiling((deadlineUtc - DateTimeOffset.UtcNow).TotalMilliseconds);
                if (remainingMs <= 0)
                {
                    throw new TimeoutException($"No response received within {timeoutMs} ms.");
                }

                serialPort.ReadTimeout = Math.Max(1, Math.Min(ReadPollTimeoutMs, remainingMs));

                int bytesRead;
                try
                {
                    bytesRead = serialPort.Read(readBuffer, 0, readBuffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                for (var index = 0; index < bytesRead; index++)
                {
                    accumulated.Add(readBuffer[index]);
                }

                var response = TryExtractMatchingResponse(accumulated, expectedCommand, expectedSequence);
                if (response is not null)
                {
                    return response;
                }
            }
        }
        finally
        {
            serialPort.ReadTimeout = previousReadTimeout;
        }
    }

    private static FrontendFrame? TryExtractMatchingResponse(
        List<byte> accumulated,
        FrontendCommand expectedCommand,
        byte expectedSequence)
    {
        while (accumulated.Count >= 2)
        {
            var frameStart = FindStartOfFrame(accumulated);
            if (frameStart < 0)
            {
                var keepTrailingSof = accumulated[^1] == FrontendProtocolCodec.Sof0;
                accumulated.Clear();
                if (keepTrailingSof)
                {
                    accumulated.Add(FrontendProtocolCodec.Sof0);
                }

                return null;
            }

            if (frameStart > 0)
            {
                accumulated.RemoveRange(0, frameStart);
            }

            if (accumulated.Count < FrontendProtocolCodec.HeaderLength)
            {
                return null;
            }

            var payloadLength = accumulated[6];
            if (payloadLength > FrontendProtocolCodec.ResponsePayloadMaxBytes)
            {
                accumulated.RemoveAt(0);
                continue;
            }

            var frameLength = FrontendProtocolCodec.GetFrameLength(payloadLength);
            if (accumulated.Count < frameLength)
            {
                return null;
            }

            var candidate = accumulated.GetRange(0, frameLength).ToArray();
            if (!FrontendProtocolCodec.TryDecodeFrame(candidate, out var frame, out var decodeError))
            {
                if (candidate[3] == (byte)FrontendMessageType.DeviceResponse &&
                    candidate[4] == (byte)expectedCommand &&
                    candidate[5] == expectedSequence)
                {
                    throw new FrontendProtocolException($"Invalid {expectedCommand} response frame: {decodeError}");
                }

                accumulated.RemoveAt(0);
                continue;
            }

            accumulated.RemoveRange(0, frameLength);

            if (frame.Type != (byte)FrontendMessageType.DeviceResponse ||
                frame.Command != (byte)expectedCommand ||
                frame.Sequence != expectedSequence)
            {
                continue;
            }

            return frame;
        }

        return null;
    }

    private static int FindStartOfFrame(IReadOnlyList<byte> buffer)
    {
        for (var index = 0; index < buffer.Count - 1; index++)
        {
            if (buffer[index] == FrontendProtocolCodec.Sof0 &&
                buffer[index + 1] == FrontendProtocolCodec.Sof1)
            {
                return index;
            }
        }

        return -1;
    }

    private void ApplyHelloPayload(ReadOnlySpan<byte> payload)
    {
        _protocolVersionText = $"{payload[4].ToString(CultureInfo.InvariantCulture)}.0";
        _firmwareVersionText =
            $"{payload[5].ToString(CultureInfo.InvariantCulture)}.{payload[6].ToString(CultureInfo.InvariantCulture)}";

        _logger.Info(
            $"串口 HELLO 成功：protocol={_protocolVersionText}, firmware={_firmwareVersionText}, capabilities=0x{payload[7]:X2}。");
    }

    private byte GetNextSequence()
    {
        var sequence = _nextSequence;
        _nextSequence++;
        return sequence;
    }

    private static void EnsureSuccessResponse(string commandName, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            throw new FrontendProtocolException($"{commandName} response does not contain the common status wrapper.");
        }

        var status = payload[0];
        var errorCode = payload[1];
        if (status != 0)
        {
            throw new FrontendProtocolException($"{commandName} failed: status=0x{status:X2}, err=0x{errorCode:X2}.");
        }
    }

    private async Task CloseAfterFailedConnectAsync()
    {
        await _serialLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            CloseSerialPortCore(logDisconnect: false);
        }
        finally
        {
            _serialLock.Release();
        }
    }

    private void CloseSerialPortCore(bool logDisconnect)
    {
        var previousPort = CurrentPort;

        try
        {
            if (_serialPort is not null)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                _serialPort.Dispose();
            }
        }
        finally
        {
            _serialPort = null;
            CurrentPort = null;
            IsConnected = false;
            _protocolVersionText = "1.0";
            _firmwareVersionText = "未读取";
        }

        if (logDisconnect && previousPort is not null)
        {
            _logger.Info($"前端串口已断开：{previousPort}。");
        }
    }

    private static string GetCommandName(FrontendCommand command)
    {
        return command switch
        {
            FrontendCommand.Hello => "HELLO",
            FrontendCommand.GetStatus => "GET_STATUS",
            FrontendCommand.Ping => "PING",
            FrontendCommand.StartStream => "START_STREAM",
            FrontendCommand.StopStream => "STOP_STREAM",
            FrontendCommand.ResetFrontend => "RESET_FRONTEND",
            FrontendCommand.LoadDefaultProfile => "LOAD_DEFAULT_PROFILE",
            FrontendCommand.SetCenterFrequency => "SET_CENTER_FREQ",
            FrontendCommand.Max2769WriteRegister => "MAX2769_WRITE_REG",
            FrontendCommand.Max2769ReadShadow => "MAX2769_READ_SHADOW",
            FrontendCommand.Max2769ConfigStatus => "MAX2769_CONFIG_STATUS",
            _ => $"CMD_0x{((byte)command):X2}"
        };
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
