using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Core.Abstractions;

public interface IAppLogger
{
    event EventHandler<LogEntry>? EntryWritten;

    IReadOnlyList<LogEntry> Entries { get; }

    void Info(string message);

    void Warning(string message);

    void Error(string message);
}
