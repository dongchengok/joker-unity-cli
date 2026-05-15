using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public partial class CompileService : ICompileService
{
    private readonly IExecService _execService;

    public CompileService(IExecService execService)
    {
        _execService = execService;
    }

    public async Task<CompileResult> CompileAsync(string projectPath, int timeoutMs, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var serverInfo = TryReadServerInfo(projectPath);
        if (serverInfo == null || serverInfo.Port <= 0)
        {
            return new CompileResult
            {
                Success = false,
                Status = "server_not_found",
                Errors = ["Unity Editor is not running. Open the Unity Editor project first."],
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }

        var initialPort = serverInfo.Port;

        const string triggerScript = @"
Joker.UnityCli.Editor.ScriptServer.HttpExecHandler.TriggerDelayedRecompile();
""compile_triggered""";

        try
        {
            var triggerResult = await _execService.ExecuteAsync(projectPath, triggerScript, "script", 30000, ct);
            if (!triggerResult.Success)
            {
                return new CompileResult
                {
                    Success = false,
                    Status = "failed",
                    Errors = [$"Failed to trigger compilation: {triggerResult.Error}"],
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
        }
        catch (Exception ex)
        {
            return new CompileResult
            {
                Success = false,
                Status = "failed",
                Errors = [$"Failed to trigger compilation: {ex.Message}"],
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }

        // Monitor for compilation complete via status field or port change
        var deadline = stopwatch.Elapsed + TimeSpan.FromMilliseconds(timeoutMs);
        while (stopwatch.Elapsed < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var currentInfo = TryReadServerInfo(projectPath);
            if (currentInfo != null)
            {
                // Status field present: compilation complete when status is "ready"
                if (currentInfo.Status == "ready")
                {
                    return new CompileResult
                    {
                        Success = true,
                        Status = "compiled",
                        DurationMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Status field absent (old format): fallback to port change detection
                if (currentInfo.Status == null && currentInfo.Port != initialPort)
                {
                    return new CompileResult
                    {
                        Success = true,
                        Status = "compiled",
                        DurationMs = stopwatch.ElapsedMilliseconds
                    };
                }
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

    internal static ServerInfoFull? TryReadServerInfo(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            return null;

        try
        {
            var json = File.ReadAllText(portFile);
            return JsonSerializer.Deserialize<ServerInfoFull>(json, JsonOptions);
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

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
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

    internal class ServerInfoFull
    {
        public int Port { get; set; }
        public int Pid { get; set; }
        public string? Status { get; set; }
    }

    [GeneratedRegex(@"^(.+?)\((\d+),(\d+)\):\s+error\s+(CS\d+):\s+(.+)$")]
    private static partial Regex ErrorLineRegex();
}
