namespace GNSSR.Host.Core.Models;

public sealed class Fx3Status
{
    public bool Active { get; init; }

    public string UsbSpeed { get; init; } = "Unknown";

    public uint ProdEventCount { get; init; }

    public uint DmaErrorCount { get; init; }
}
