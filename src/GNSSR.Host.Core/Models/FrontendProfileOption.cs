namespace GNSSR.Host.Core.Models;

public sealed class FrontendProfileOption
{
    public byte ProfileId { get; init; }

    public byte ChannelMask { get; init; } = 0x03;

    public uint CenterFrequencyHz { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public override string ToString() => DisplayName;
}
