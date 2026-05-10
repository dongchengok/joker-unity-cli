using System.Diagnostics;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class BuildService : IBuildService
{
    public List<string> BuildCommandArgs(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null)
    {
        var args = new List<string>
        {
            "-batchmode",
            "-quit",
            "-projectPath", projectPath,
            "-executeMethod", executeMethod,
            "-buildTarget", buildTarget,
        };

        if (scenes is { Length: > 0 })
        {
            args.Add("-scenes");
            args.Add(string.Join(",", scenes));
        }

        if (!string.IsNullOrEmpty(logFile))
        {
            args.Add("-logFile");
            args.Add(logFile);
        }

        return args;
    }

    public Task<BuildResult> BuildAsync(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null,
        CancellationToken cancellationToken = default)
    {
        var args = BuildCommandArgs(projectPath, unityPath, buildTarget, executeMethod, outputPath, scenes, logFile);

        return Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = unityPath,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var stopwatch = Stopwatch.StartNew();

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start Unity process: {unityPath}");

            process.WaitForExit();

            stopwatch.Stop();

            return new BuildResult
            {
                Success = process.ExitCode == 0,
                LogPath = logFile ?? "",
                OutputPath = outputPath,
                Duration = stopwatch.Elapsed,
            };
        }, cancellationToken);
    }
}
