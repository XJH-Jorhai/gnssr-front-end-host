namespace GNSSR.Host.Core.Models;

public sealed class CaptureSessionInfo
{
    public required string SessionId { get; init; }

    public required string FileNamePrefix { get; init; }

    public required string OutputDirectory { get; init; }

    public required string BaseFileName { get; init; }

    public required string BinPath { get; init; }

    public required string JsonPath { get; init; }

    public DateTimeOffset StartTimeHost { get; init; }

    public DateTimeOffset? StopTimeHost { get; set; }

    public string StopReason { get; set; } = "not_stopped";

    public bool IsIncomplete { get; set; }

    public bool IsSimulated { get; init; }
}
