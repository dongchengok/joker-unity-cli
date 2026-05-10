using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public partial class LogService : ILogService
{
    private const long MaxReadSize = 10 * 1024 * 1024;
    private readonly string _logFilePath;

    public LogService() : this(GetDefaultLogPath()) { }

    internal LogService(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    public async Task<List<LogEntry>> GetLogEntriesAsync(int tailCount, bool errorsOnly, string? projectPath, CancellationToken ct = default)
    {
        if (!File.Exists(_logFilePath))
            return [];

        var fileInfo = new FileInfo(_logFilePath);
        if (fileInfo.Length == 0)
            return [];

        var entries = new List<LogEntry>();
        var seen = new HashSet<(string FilePath, int Line, string Code)>();
        var regex = LogLineRegex();

        var offset = Math.Max(0, fileInfo.Length - MaxReadSize);

        using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();

            var match = regex.Match(line);
            if (!match.Success)
                continue;

            var filePath = match.Groups[1].Value;
            var lineNum = int.Parse(match.Groups[2].Value);
            var column = int.Parse(match.Groups[3].Value);
            var severity = match.Groups[4].Value;
            var code = match.Groups[5].Value;
            var message = match.Groups[6].Value;

            if (!seen.Add((filePath, lineNum, code)))
                continue;

            entries.Add(new LogEntry
            {
                FilePath = filePath,
                Line = lineNum,
                Column = column,
                Severity = severity,
                Code = code,
                Message = message
            });
        }

        if (errorsOnly)
            entries = entries.Where(e => e.Severity == "error").ToList();

        if (projectPath is not null)
            entries = entries.Where(e => e.FilePath.StartsWith(projectPath)).ToList();

        if (tailCount > 0 && entries.Count > tailCount)
            entries = entries.Skip(entries.Count - tailCount).ToList();

        return entries;
    }

    private static string GetDefaultLogPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Unity", "Editor.log");

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "unity3d", "Editor.log");
    }

    [GeneratedRegex(@"^(.+?)\((\d+),(\d+)\):\s+(error|warning)\s+(CS\d+):\s+(.+)$")]
    private static partial Regex LogLineRegex();
}
