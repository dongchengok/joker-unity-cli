using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface ILogService
{
    Task<List<LogEntry>> GetLogEntriesAsync(int tailCount, bool errorsOnly, string? projectPath, CancellationToken ct = default);
}
