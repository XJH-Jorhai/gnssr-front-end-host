namespace GNSSR.Host.Infrastructure.Serial.Protocol;

public sealed class FrontendFrame
{
    public required byte Version { get; init; }

    public required byte Type { get; init; }

    public required byte Command { get; init; }

    public required byte Sequence { get; init; }

    public required byte[] Payload { get; init; }

    public required byte[] RawBytes { get; init; }
}
