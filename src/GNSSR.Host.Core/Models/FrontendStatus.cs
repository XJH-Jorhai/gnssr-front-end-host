namespace GNSSR.Host.Core.Models;

public sealed class FrontendStatus
{
    public bool PllLocked { get; init; }

    public bool AntennaOk { get; init; }

    public int ActiveProfile { get; init; }

    public uint CurrentFrequencyHz { get; init; }

    public uint TcxoFrequencyHz { get; init; }

    public ushort LastError { get; init; }

    public string ProtocolVersion { get; init; } = "1.0";

    public string FirmwareVersion { get; init; } = "未读取";

    public string HardwareVersion { get; init; } = "未读取";
}
