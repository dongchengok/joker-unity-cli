using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IExecService
{
    Task<ExecResult> ExecuteAsync(string projectPath, string code, string mode, int timeoutMs, CancellationToken ct);
}
