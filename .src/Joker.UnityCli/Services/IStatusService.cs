using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IStatusService
{
    Task<ServerStatus> GetStatusAsync(string projectPath, CancellationToken ct);
}
