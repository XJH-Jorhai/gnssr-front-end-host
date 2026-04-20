using GNSSR.Host.Core.Abstractions;
using GNSSR.Host.Core.Enums;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Infrastructure.Logging.Services;

public sealed class InMemoryLogService : IAppLogger
{
    private readonly List<LogEntry> _entries = [];
    private readonly object _syncRoot = new();

    public event EventHandler<LogEntry>? EntryWritten;

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    public void Info(string message)
    {
        Write(LogSeverity.Info, message);
    }

    public void Warning(string message)
    {
        Write(LogSeverity.Warning, message);
    }

    public void Error(string message)
    {
        Write(LogSeverity.Error, message);
    }

    private void Write(LogSeverity severity, string message)
    {
        var entry = new LogEntry
        {
            Severity = severity,
            Message = message,
            Timestamp = DateTimeOffset.Now
        };

        lock (_syncRoot)
        {
            _entries.Add(entry);
        }

        EntryWritten?.Invoke(this, entry);
    }
}
