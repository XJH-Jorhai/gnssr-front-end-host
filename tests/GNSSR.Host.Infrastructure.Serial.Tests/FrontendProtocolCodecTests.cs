using GNSSR.Host.Infrastructure.Serial.Protocol;
using Xunit;

namespace GNSSR.Host.Infrastructure.Serial.Tests;

public sealed class FrontendProtocolCodecTests
{
    [Fact]
    public void EncodeRequest_WithHelloVector_MatchesFirmwareContract()
    {
        var frame = FrontendProtocolCodec.EncodeRequest(FrontendCommand.Hello, 0x01);

        Assert.Equal("55 AA 01 01 01 01 00 48 6C", FrontendProtocolCodec.FormatHex(frame));
    }

    [Fact]
    public void EncodeRequest_WithGetStatusVector_MatchesFirmwareContract()
    {
        var frame = FrontendProtocolCodec.EncodeRequest(FrontendCommand.GetStatus, 0x02);

        Assert.Equal("55 AA 01 01 02 02 00 B8 9C", FrontendProtocolCodec.FormatHex(frame));
    }

    [Fact]
    public void EncodeRequest_WithPingPayloadVector_MatchesFirmwareContract()
    {
        var frame = FrontendProtocolCodec.EncodeRequest(FrontendCommand.Ping, 0x03, [0x10, 0x20, 0x30]);

        Assert.Equal("55 AA 01 01 03 03 03 10 20 30 CD 61", FrontendProtocolCodec.FormatHex(frame));
    }

    [Fact]
    public void EncodeRequest_WithLoadDefaultProfileVector_MatchesFirmwareContract()
    {
        var frame = FrontendProtocolCodec.EncodeRequest(FrontendCommand.LoadDefaultProfile, 0x05, [0x03, 0x01]);

        Assert.Equal("55 AA 01 01 13 05 02 03 01 EF EE", FrontendProtocolCodec.FormatHex(frame));
    }

    [Fact]
    public void EncodeRequest_WithSetCenterFrequencyVector_MatchesFirmwareContract()
    {
        var frame = FrontendProtocolCodec.EncodeRequest(
            FrontendCommand.SetCenterFrequency,
            0x06,
            [0x03, 0x60, 0x00, 0xE7, 0x5D]);

        Assert.Equal("55 AA 01 01 14 06 05 03 60 00 E7 5D 9A F1", FrontendProtocolCodec.FormatHex(frame));
    }

    [Fact]
    public void DecodeFrame_WithHelloResponseVector_ReturnsPayloadFields()
    {
        var frame = FrontendProtocolCodec.DecodeFrame(Hex("55 AA 01 02 01 01 08 00 00 00 00 01 00 01 3F 8A 28"));

        Assert.Equal((byte)FrontendMessageType.DeviceResponse, frame.Type);
        Assert.Equal((byte)FrontendCommand.Hello, frame.Command);
        Assert.Equal(0x01, frame.Sequence);
        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x3F], frame.Payload);
    }

    [Fact]
    public void DecodeFrame_WithBadCrc_ThrowsProtocolException()
    {
        var frame = Hex("55 AA 01 02 01 01 08 00 00 00 00 01 00 01 3F 8A 28");
        frame[^1] ^= 0x01;

        Assert.Throws<FrontendProtocolException>(() => FrontendProtocolCodec.DecodeFrame(frame));
    }

    [Fact]
    public void FindStartOfFrame_WithNoisePrefix_FindsNextSofPair()
    {
        var bytes = Hex("00 FF 55 00 55 AA 01 02 01 01 08 00 00 00 00 01 00 01 3F 8A 28");

        var frameStart = FrontendProtocolCodec.FindStartOfFrame(bytes);
        var frame = FrontendProtocolCodec.DecodeFrame(bytes.AsSpan(frameStart));

        Assert.Equal(4, frameStart);
        Assert.Equal((byte)FrontendCommand.Hello, frame.Command);
    }

    private static byte[] Hex(string value)
    {
        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Convert.ToByte(part, 16))
            .ToArray();
    }
}
