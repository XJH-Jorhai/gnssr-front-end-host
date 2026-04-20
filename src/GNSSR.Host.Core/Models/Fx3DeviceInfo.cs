namespace GNSSR.Host.Core.Models;

public sealed class Fx3DeviceInfo
{
    public required string DeviceId { get; init; }

    public required string DisplayName { get; init; }

    public required string Vid { get; init; }

    public required string Pid { get; init; }

    public required string InterfaceDescription { get; init; }

    public override string ToString()
    {
        return DisplayName;
    }
}
