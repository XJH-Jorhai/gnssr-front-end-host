using GNSSR.Host.Core.Enums;

namespace GNSSR.Host.Core.Models;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public LogSeverity Severity { get; init; } = LogSeverity.Info;

    public required string Message { get; init; }

    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string SeverityDisplay => Severity.ToString().ToUpperInvariant();
}
