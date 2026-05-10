using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IBuildService
{
    List<string> BuildCommandArgs(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null
    );

    Task<BuildResult> BuildAsync(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null,
        CancellationToken cancellationToken = default
    );
}
