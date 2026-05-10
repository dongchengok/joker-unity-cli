# `joker-unity logs` 命令设计

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 提供 `joker-unity logs` 命令，从终端获取 Unity Editor 日志中的编译错误和警告信息，支持跨平台、项目过滤、条目数量限制。

**Architecture:** LogService 异步读取 Unity Editor 日志文件（从文件末尾反向读取，最多 10MB），按正则匹配解析编译诊断信息，去重后返回。LogsCommand 支持可选的项目路径过滤（自动检测或手动指定），输出文本/JSON 两种格式。

**Tech Stack:** C# / .NET 8、Spectre.Console.Cli、xUnit + FluentAssertions + NSubstitute

---

## 背景

Unity Editor 打开项目时有编译错误，需要从终端获取这些错误信息。日志文件可能很大（数百 MB），必须高效读取。当前计划是修复 Roslyn DLL .meta 文件 GUID 格式错误后的配套命令，用于后续 TDD 迭代中追踪编译错误。

---

## 命令接口

```
joker-unity logs [选项]
```

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--errors` | bool | false | 只显示编译错误（`error CS\d{4}`） |
| `--tail <N>` | int | 50 | 显示最近 N 条匹配记录 |
| `--json` | bool | false | JSON 格式输出 |
| `-p, --project <PATH>` | string? | null | Unity 项目路径（不指定时自动检测，失败则显示全部） |

---

## 跨平台日志路径

| 平台 | 日志路径 |
|------|----------|
| Windows | `%LOCALAPPDATA%\Unity\Editor\Editor.log` |
| macOS | `~/Library/Logs/Unity/Editor.log` |
| Linux | `~/.config/unity3d/Editor.log` |

路径选择在 LogService 构造时根据 `RuntimeInformation.IsOSPlatform()` 决定。

---

## 数据模型

```csharp
namespace Joker.UnityCli.Models;

public class LogEntry
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Severity { get; set; } = ""; // "error" or "warning"
    public string Code { get; set; } = "";     // "CS0234"
    public string Message { get; set; } = "";
}
```

---

## 服务接口

```csharp
namespace Joker.UnityCli.Services;

public interface ILogService
{
    Task<List<LogEntry>> GetLogEntriesAsync(
        int tailCount,
        bool errorsOnly,
        string? projectPath,
        CancellationToken ct = default);
}
```

---

## LogService 实现

### 依赖注入

```csharp
public class LogService : ILogService
{
    private readonly string _logFilePath;

    public LogService() : this(GetDefaultLogPath()) { }

    internal LogService(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    private static string GetDefaultLogPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Logs", "Unity", "Editor.log");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "unity3d", "Editor.log");
    }
}
```

- 构造函数注入日志路径（internal），便于测试时替换为临时文件
- 无参构造函数使用平台默认路径

### 反向读取策略

1. 检查文件是否存在，不存在返回空列表
2. 获取文件长度，计算读取起始位置（`max(length - 10MB, 0)`）
3. 用 `FileStream.Seek` + `ReadAsync` 分块读取
4. 按行解析，匹配正则
5. 收集足够条数（tailCount）后停止

```csharp
private static readonly Regex DiagnosticRegex = new(
    @"^(.+?)\((\d+),(\d+)\):\s+(error|warning)\s+(CS\d+):\s+(.+)$",
    RegexOptions.Compiled);
```

### 去重

按 `(FilePath, Line, Code)` 去重，保留最近出现的条目。去重后的结果按日志中的出现顺序排列（先出现的在前）。

### 项目过滤

日志中项目路径的匹配方式：日志条目的 FilePath 通常包含项目路径前缀。当 `projectPath` 非空时，过滤 `FilePath.StartsWith(projectPath)` 的条目。未匹配时返回空列表不报错。

### 兜底策略

| 场景 | 处理 |
|------|------|
| 日志文件不存在 | 返回空列表 |
| 日志文件为空 | 返回空列表 |
| 文件超过 10MB | 只读取末尾 10MB |
| 项目自动检测失败 | projectPath 为 null，不过滤 |
| 无匹配的日志条目 | 返回空列表 |
| CancellationToken 取消 | 立即返回已收集的条目 |

---

## LogsCommand

```csharp
public class LogsCommand : AsyncCommand<LogsCommand.Settings>
{
    public class Settings : GlobalCommandSettings
    {
        [CommandOption("--errors")]
        public bool ErrorsOnly { get; set; }

        [CommandOption("--tail")]
        public int Tail { get; set; } = 50;

        [CommandOption("-p|--project")]
        public string? ProjectPath { get; set; }
    }
}
```

### 执行流程

1. 如果未指定 `--project`，尝试用 `IProjectDetector` 自动检测当前目录
2. 自动检测失败时 `projectPath = null`（不过滤）
3. 调用 `_logService.GetLogEntriesAsync(settings.Tail, settings.ErrorsOnly, projectPath)`
4. JSON 模式：序列化 `List<LogEntry>` 到 stdout
5. 文本模式：Spectre Table（Severity, Code, File:Line, Message）

---

## 测试策略

### LogServiceTests

使用临时文件模拟日志内容：

- `GetLogEntriesAsync_EmptyFile_ReturnsEmptyList` — 空文件
- `GetLogEntriesAsync_FileNotFound_ReturnsEmptyList` — 文件不存在
- `GetLogEntriesAsync_WithErrors_ReturnsMatchedEntries` — 正常解析
- `GetLogEntriesAsync_ErrorsOnly_FiltersWarnings` — --errors 过滤
- `GetLogEntriesAsync_TailCount_LimitsResults` — --tail 限制
- `GetLogEntriesAsync_DuplicateEntries_Deduplicates` — 去重
- `GetLogEntriesAsync_ProjectPath_FiltersByProject` — 项目路径过滤
- `GetLogEntriesAsync_LargeFile_OnlyReadsTail` — 大文件只读末尾

### LogsCommandTests

使用 NSubstitute mock ILogService 和 IProjectDetector，验证命令调用参数和输出格式。

---

## 关键文件

| 文件 | 操作 |
|------|------|
| `.src/Joker.UnityCli/Models/LogEntry.cs` | 新建 |
| `.src/Joker.UnityCli/Services/ILogService.cs` | 新建 |
| `.src/Joker.UnityCli/Services/LogService.cs` | 新建 |
| `.src/Joker.UnityCli/Commands/LogsCommand.cs` | 新建 |
| `.src/Joker.UnityCli/Program.cs` | 修改（注册服务 + 命令） |
| `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs` | 新建 |

---

## 验证

1. `dotnet test` 全部通过
2. `joker-unity logs --errors --json` 输出合法 JSON
3. `joker-unity logs --tail 10` 输出 Spectre Table
4. 不带参数运行时自动检测项目
5. 日志文件不存在时输出空列表（不报错）
