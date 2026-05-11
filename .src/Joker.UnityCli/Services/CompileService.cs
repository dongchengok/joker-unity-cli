using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public partial class CompileService : ICompileService
{
    private readonly IExecService _execService;
    private readonly IUnityLocator _unityLocator;

    public CompileService(IExecService execService, IUnityLocator unityLocator)
    {
        _execService = execService;
        _unityLocator = unityLocator;
    }

    public async Task<CompileResult> CompileAsync(string projectPath, int timeoutMs, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // Try TCP path first
        var tcpResult = await TryCompileViaTcpAsync(projectPath, timeoutMs, stopwatch, ct);
        if (tcpResult != null)
            return tcpResult;

        // Fallback: batchmode
        return await CompileViaBatchmodeAsync(projectPath, timeoutMs, stopwatch, ct);
    }

    private async Task<CompileResult?> TryCompileViaTcpAsync(string projectPath, int timeoutMs, Stopwatch stopwatch, CancellationToken ct)
    {
        var initialPort = TryReadServerPort(projectPath);
        if (initialPort == null)
            return null;

        const string triggerScript = @"
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
""triggered""";

        try
        {
            var triggerResult = await _execService.ExecuteAsync(projectPath, triggerScript, "script", 30000, ct);
            if (!triggerResult.Success)
                return null;
        }
        catch
        {
            return null;
        }

        // Monitor for port change (indicates assembly reload = compilation succeeded)
        var deadline = stopwatch.Elapsed + TimeSpan.FromMilliseconds(timeoutMs);
        while (stopwatch.Elapsed < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var currentPort = TryReadServerPort(projectPath);
            if (currentPort != null && currentPort != initialPort)
            {
                return new CompileResult
                {
                    Success = true,
                    Status = "compiled",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            await Task.Delay(2000, ct);
        }

        // Timeout - check Editor log for compilation errors
        var logPath = GetEditorLogPath();
        var errors = ParseLogForErrors(logPath);

        return new CompileResult
        {
            Success = errors.Count == 0,
            Status = errors.Count == 0 ? "up_to_date" : "failed",
            Errors = errors,
            DurationMs = stopwatch.ElapsedMilliseconds
        };
    }

#pragma warning disable CS1998
    private async Task<CompileResult> CompileViaBatchmodeAsync(string projectPath, int timeoutMs, Stopwatch stopwatch, CancellationToken ct)
    {
        var unity = _unityLocator.Locate();
        if (unity == null)
        {
            return new CompileResult
            {
                Success = false,
                Status = "failed",
                Errors = ["Unity installation not found"],
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }

        var tempLog = Path.Combine(Path.GetTempPath(), $"joker-compile-{Guid.NewGuid():N}.log");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = unity.Path,
                Arguments = $"-batchmode -quit -projectPath \"{projectPath}\" -logFile \"{tempLog}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Unity");

            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0 || !process.WaitForExit(remaining))
            {
                try { process.Kill(); } catch { }
                return new CompileResult
                {
                    Success = false,
                    Status = "timeout",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            var errors = ParseLogForErrors(tempLog);
            return new CompileResult
            {
                Success = errors.Count == 0,
                Status = errors.Count == 0 ? "compiled" : "failed",
                Errors = errors,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        finally
        {
            if (File.Exists(tempLog))
                try { File.Delete(tempLog); } catch { }
        }
    }

    internal static int? TryReadServerPort(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            return null;

        try
        {
            var json = File.ReadAllText(portFile);
            var info = JsonSerializer.Deserialize<ServerInfo>(json, JsonOptions);
            return info?.Port;
        }
        catch
        {
            return null;
        }
    }

    internal static List<string> ParseLogForErrors(string logPath)
    {
        if (!File.Exists(logPath))
            return [];

        var errors = new List<string>();

        foreach (var line in File.ReadLines(logPath))
        {
            var match = ErrorLineRegex().Match(line);
            if (match.Success)
                errors.Add($"{match.Groups[4].Value}: {match.Groups[5].Value}");
        }

        return errors;
    }

    private static string GetEditorLogPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Unity", "Editor.log");

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "unity3d", "Editor.log");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private class ServerInfo
    {
        public int Port { get; set; }
    }

    [GeneratedRegex(@"^(.+?)\((\d+),(\d+)\):\s+error\s+(CS\d+):\s+(.+)$")]
    private static partial Regex ErrorLineRegex();
}
