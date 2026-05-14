using System.Buffers.Binary;
using System.Globalization;
using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Models;
using GNSSR.Host.Infrastructure.Serial.Protocol;

namespace GNSSR.Host.Infrastructure.Serial.Services;

public sealed class Fx3FrontendTunnelService : IFrontendSerialService
{
    public const string LogicalPortName = "FX3 UART Tunnel";

    private const int ConnectHelloTimeoutMs = 500;
    private const int NormalCommandTimeoutMs = 300;
    private const int Max2769CommandTimeoutMs = 1000;
    private const int ReadBufferBytes = 256;
    private const int RetriesPerCommand = 3;

    private readonly IFx3UsbService _fx3UsbService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _tunnelLock = new(1, 1);
    private byte _nextSequence = 1;
    private string _protocolVersionText = "1.0";
    private string _firmwareVersionText = "unknown";

    public Fx3FrontendTunnelService(IFx3UsbService fx3UsbService, IAppLogger logger)
    {
        _fx3UsbService = fx3UsbService;
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public string? CurrentPort { get; private set; }

    public Task<IReadOnlyList<string>> DiscoverPortsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([LogicalPortName]);
    }

    public async Task ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        if (!string.Equals(portName, LogicalPortName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported frontend link '{portName}'.", nameof(portName));
        }

        if (!_fx3UsbService.IsConnected)
        {
            throw new InvalidOperationException("Connect the FX3 device before opening the UART tunnel.");
        }

        IsConnected = false;
        CurrentPort = LogicalPortName;
        _nextSequence = 1;

        try
        {
            var hello = await ExecuteCommandAsync(
                FrontendCommand.Hello,
                payload: [],
                expectedPayloadLength: 8,
                timeoutMs: ConnectHelloTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            ApplyHelloPayload(hello.Payload);
            IsConnected = true;
            _logger.Info("Frontend connected through FX3 UART Tunnel.");
        }
        catch
        {
            IsConnected = false;
            CurrentPort = null;
            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsConnected)
        {
            _logger.Info("Frontend FX3 UART Tunnel disconnected.");
        }

        IsConnected = false;
        CurrentPort = null;
        _protocolVersionText = "1.0";
        _firmwareVersionText = "unknown";
        return Task.CompletedTask;
    }

    public async Task<FrontendStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Frontend UART tunnel has not completed HELLO.");
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
            HardwareVersion = "unknown"
        };
    }

    public async Task LoadDefaultProfileAsync(byte channelMask, byte profileId, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Frontend UART tunnel has not completed HELLO.");
        }

        await ExecuteCommandAsync(
            FrontendCommand.LoadDefaultProfile,
            payload: [channelMask, profileId],
            expectedPayloadLength: 4,
            timeoutMs: Max2769CommandTimeoutMs,
            cancellationToken).ConfigureAwait(false);

        _logger.Info($"FX3 UART MAX2769 profile applied: channelMask=0x{channelMask:X2}, profile=0x{profileId:X2}.");
    }

    public async Task SetCenterFrequencyAsync(byte channelMask, uint centerFrequencyHz, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Frontend UART tunnel has not completed HELLO.");
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

        _logger.Info($"FX3 UART MAX2769 center frequency set: channelMask=0x{channelMask:X2}, rf={centerFrequencyHz.ToString("N0", CultureInfo.InvariantCulture)} Hz.");
    }

    private async Task<FrontendFrame> ExecuteCommandAsync(
        FrontendCommand command,
        byte[] payload,
        int expectedPayloadLength,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        await _tunnelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sequence = GetNextSequence();
            var request = FrontendProtocolCodec.EncodeRequest(command, sequence, payload);
            var commandName = GetCommandName(command);

            for (var attempt = 1; attempt <= RetriesPerCommand; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _fx3UsbService.WriteFrontendUartAsync(request, cancellationToken).ConfigureAwait(false);
                _logger.Info($"FX3 UART TX {commandName} seq=0x{sequence:X2}: {FrontendProtocolCodec.FormatHex(request)}");

                try
                {
                    var response = await ReadMatchingResponseAsync(
                        command,
                        sequence,
                        timeoutMs,
                        cancellationToken).ConfigureAwait(false);

                    _logger.Info($"FX3 UART RX {commandName} seq=0x{sequence:X2}: {FrontendProtocolCodec.FormatHex(response.RawBytes)}");

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
                    _logger.Warning($"FX3 UART {commandName} timeout, retrying {attempt + 1}/{RetriesPerCommand}.");
                }
            }

            throw new TimeoutException($"FX3 UART {command} did not receive a valid response after {RetriesPerCommand} attempts.");
        }
        finally
        {
            _tunnelLock.Release();
        }
    }

    private async Task<FrontendFrame> ReadMatchingResponseAsync(
        FrontendCommand expectedCommand,
        byte expectedSequence,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        var accumulated = new List<byte>(ReadBufferBytes);
        var readBuffer = new byte[ReadBufferBytes];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= deadlineUtc)
            {
                throw new TimeoutException($"No response received within {timeoutMs} ms.");
            }

            var bytesRead = await _fx3UsbService.ReadFrontendUartAsync(readBuffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0)
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
            $"FX3 UART HELLO accepted: protocol={_protocolVersionText}, firmware={_firmwareVersionText}, capabilities=0x{payload[7]:X2}.");
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
}
