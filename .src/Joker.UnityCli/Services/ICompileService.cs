using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface ICompileService
{
    Task<CompileResult> CompileAsync(string projectPath, int timeoutMs, CancellationToken ct);
}
