using System.Globalization;
using System.Text;

namespace GNSSR.Host.Infrastructure.Serial.Protocol;

public static class FrontendProtocolCodec
{
    public const byte Sof0 = 0x55;
    public const byte Sof1 = 0xAA;
    public const byte Version = 0x01;
    public const int HeaderLength = 7;
    public const int CrcLength = 2;
    public const int MinimumFrameLength = HeaderLength + CrcLength;
    public const int RequestPayloadMaxBytes = 128;
    public const int ResponsePayloadMaxBytes = 160;

    public static byte[] EncodeRequest(FrontendCommand command, byte sequence, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > RequestPayloadMaxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), $"Request payload is limited to {RequestPayloadMaxBytes} bytes.");
        }

        var frame = new byte[MinimumFrameLength + payload.Length];
        frame[0] = Sof0;
        frame[1] = Sof1;
        frame[2] = Version;
        frame[3] = (byte)FrontendMessageType.HostRequest;
        frame[4] = (byte)command;
        frame[5] = sequence;
        frame[6] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(HeaderLength));

        WriteCrc(frame);
        return frame;
    }

    public static FrontendFrame DecodeFrame(ReadOnlySpan<byte> rawFrame)
    {
        if (!TryDecodeFrame(rawFrame, out var frame, out var error))
        {
            throw new FrontendProtocolException(error ?? "Invalid frontend serial frame.");
        }

        return frame;
    }

    public static bool TryDecodeFrame(
        ReadOnlySpan<byte> rawFrame,
        out FrontendFrame frame,
        out string? error)
    {
        frame = null!;
        error = null;

        if (rawFrame.Length < MinimumFrameLength)
        {
            error = "Frame is shorter than the minimum length.";
            return false;
        }

        if (rawFrame[0] != Sof0 || rawFrame[1] != Sof1)
        {
            error = "Frame start bytes are invalid.";
            return false;
        }

        if (rawFrame[2] != Version)
        {
            error = $"Unsupported protocol version 0x{rawFrame[2]:X2}.";
            return false;
        }

        var payloadLength = rawFrame[6];
        var expectedLength = GetFrameLength(payloadLength);
        if (rawFrame.Length != expectedLength)
        {
            error = $"Frame length mismatch. Expected {expectedLength} bytes, got {rawFrame.Length}.";
            return false;
        }

        var actualCrc = (ushort)(rawFrame[^2] | (rawFrame[^1] << 8));
        var expectedCrc = ComputeFrameCrc(rawFrame);
        if (actualCrc != expectedCrc)
        {
            error = $"Bad CRC. Expected 0x{expectedCrc:X4}, got 0x{actualCrc:X4}.";
            return false;
        }

        frame = new FrontendFrame
        {
            Version = rawFrame[2],
            Type = rawFrame[3],
            Command = rawFrame[4],
            Sequence = rawFrame[5],
            Payload = rawFrame.Slice(HeaderLength, payloadLength).ToArray(),
            RawBytes = rawFrame.ToArray()
        };
        return true;
    }

    public static int FindStartOfFrame(ReadOnlySpan<byte> buffer)
    {
        for (var index = 0; index < buffer.Length - 1; index++)
        {
            if (buffer[index] == Sof0 && buffer[index + 1] == Sof1)
            {
                return index;
            }
        }

        return -1;
    }

    public static int GetFrameLength(int payloadLength)
    {
        return MinimumFrameLength + payloadLength;
    }

    public static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 3 - 1);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void WriteCrc(Span<byte> frame)
    {
        var crc = ComputeFrameCrc(frame);
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)(crc >> 8);
    }

    private static ushort ComputeFrameCrc(ReadOnlySpan<byte> frame)
    {
        var payloadLength = frame[6];
        return Crc16Ibm.Compute(frame.Slice(2, 5 + payloadLength));
    }
}
