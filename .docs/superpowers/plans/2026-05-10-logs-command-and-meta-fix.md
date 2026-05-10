# 修复 Unity 编译错误 + 添加 logs 命令 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 Unity Editor 侧编译错误（Roslyn DLL .meta 文件 GUID 格式错误），更新 CLAUDE.md 添加 Superpowers 技能红线要求，并新增 `joker-unity logs` 命令从终端获取 Unity 编译错误信息。

**Architecture:** 修复 6 个 .meta 文件的 GUID 格式（从带连字符改为 32 位十六进制）；新增 LogService 异步读取 Unity Editor 日志（跨平台、反向读取、最多 10MB），解析编译诊断信息，去重并支持项目路径过滤；LogsCommand 支持文本/JSON 两种输出格式。

**Tech Stack:** C# / .NET 8、Spectre.Console.Cli、xUnit + FluentAssertions + NSubstitute、Unity Editor Roslyn 3.8.0

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `CLAUDE.md` | 修改 | 添加 Superpowers 技能红线要求 |
| `Editor/Plugins/Roslyn/*.meta`（6 个 .dll.meta） | 修改 | 修复 GUID 格式 |
| `.src/Joker.UnityCli/Models/LogEntry.cs` | 新建 | 日志条目数据模型 |
| `.src/Joker.UnityCli/Services/ILogService.cs` | 新建 | 日志服务接口 |
| `.src/Joker.UnityCli/Services/LogService.cs` | 新建 | 日志服务实现（跨平台、反向读取） |
| `.src/Joker.UnityCli/Commands/LogsCommand.cs` | 新建 | logs 命令（参数解析 + 输出格式化） |
| `.src/Joker.UnityCli/Program.cs` | 修改 | 注册 ILogService + LogsCommand |
| `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs` | 新建 | LogService 单元测试 |
| `.src/Joker.UnityCli.Tests/Commands/LogsCommandTests.cs` | 新建 | LogsCommand 单元测试 |
| `README.md` | 修改 | 添加 logs 命令文档 |

---

## Task 1: 更新 CLAUDE.md — 添加 Superpowers 技能红线要求

**Files:**
- Modify: `CLAUDE.md`

在 `## 基本规则` 之后、`## 项目概述` 之前，添加新的 section。

- [ ] **Step 1: 编辑 CLAUDE.md**

在 `## 基本规则` 末尾（`- 不确定时坦诚表达，不装懂` 之后）添加：

```markdown

## 开发流程（红线要求）

所有功能开发、Bug 修复、架构变更的计划和实施，**必须**使用以下 Superpowers 技能，不得跳过：

1. **`superpowers:brainstorming`** — 协作式需求分析和方案设计，任何新功能或变更必须先经过 brainstorming 流程
2. **`superpowers:writing-plans`** — 编写详细实施计划，计划文件保存到 `.docs/superpowers/plans/` 目录
3. **`superpowers:subagent-driven-development`** — 通过子代理分任务执行计划，每个任务完成后进行规范合规审查和代码质量审查
4. **`superpowers:test-driven-development`** — 严格 TDD 流程（红-绿-重构），先写失败测试，再写最小实现

**不得跳过的原因：** 这些技能确保了设计充分讨论、计划完整可执行、代码质量有保障、测试覆盖到位。跳过任何环节都会导致返工和质量下降。
```

- [ ] **Step 2: 验证**

确保 CLAUDE.md 格式正确，Markdown 渲染无异常。

- [ ] **Step 3: 提交**

```bash
git add CLAUDE.md
git commit -m "docs: add superpowers skills red-line requirement to CLAUDE.md"
```

---

## Task 2: 修复 Roslyn DLL .meta 文件 GUID

**Files:**
- Modify: `Editor/Plugins/Roslyn/Microsoft.CodeAnalysis.dll.meta`
- Modify: `Editor/Plugins/Roslyn/Microsoft.CodeAnalysis.CSharp.dll.meta`
- Modify: `Editor/Plugins/Roslyn/Microsoft.CodeAnalysis.CSharp.Scripting.dll.meta`
- Modify: `Editor/Plugins/Roslyn/Microsoft.CodeAnalysis.Scripting.dll.meta`
- Modify: `Editor/Plugins/Roslyn/System.Collections.Immutable.dll.meta`
- Modify: `Editor/Plugins/Roslyn/System.Reflection.Metadata.dll.meta`

**问题：** 当前 GUID 格式为带连字符的 UUID（如 `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b`），Unity 要求 32 位十六进制无连字符（如 `a1b2c3d4e5f6789012345678abcdef01`）。参考同目录下 `Microsoft.CodeAnalysis.CSharp.Scripting.pdb.meta` 的正确格式：`371eb1b6d6bf3444f875216753b188e3`。

- [ ] **Step 1: 生成 6 个有效 GUID**

```bash
python3 -c "import uuid; [print(uuid.uuid4().hex) for _ in range(6)]"
```

或 PowerShell：

```powershell
1..6 | ForEach-Object { [guid]::NewGuid().ToString('N') }
```

预期输出 6 个 32 位十六进制字符串，如：
```
a1b2c3d4e5f6789012345678abcdef01
...
```

- [ ] **Step 2: 替换每个 .meta 文件的 guid 行**

每个文件只需替换第 2 行的 `guid:` 值，保持其余内容不变。

6 个文件及其新 GUID：

| 文件 | 旧 GUID | 新 GUID（示例，使用 Step 1 生成的） |
|------|---------|--------------------------------------|
| `Microsoft.CodeAnalysis.dll.meta` | `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b` | `<guid1>` |
| `Microsoft.CodeAnalysis.CSharp.dll.meta` | `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b` | `<guid2>` |
| `Microsoft.CodeAnalysis.CSharp.Scripting.dll.meta` | `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b` | `<guid3>` |
| `Microsoft.CodeAnalysis.Scripting.dll.meta` | `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b` | `<guid4>` |
| `System.Collections.Immutable.dll.meta` | `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b` | `<guid5>` |
| `System.Reflection.Metadata.dll.meta` | `2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b` | `<guid6>` |

每个文件的修改只有一行：
```yaml
# 旧
guid: 2e3f4a5b-6c7d-8e9f-0a1b-2c3d4e5f6a7b
# 新
guid: <对应的32位hex>
```

- [ ] **Step 3: 提交**

```bash
git add Editor/Plugins/Roslyn/*.meta
git commit -m "fix: correct Roslyn DLL .meta file GUID format for Unity compatibility"
```

---

## Task 3: LogEntry 模型 + 序列化测试

**Files:**
- Create: `.src/Joker.UnityCli/Models/LogEntry.cs`
- Create: `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs`（先添加序列化测试）

- [ ] **Step 1: 写 LogEntry 序列化测试**

创建 `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs`：

```csharp
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class LogServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void LogEntry_Serializes_To_CamelCase_Json()
    {
        var entry = new LogEntry
        {
            FilePath = "Scripts/Player.cs",
            Line = 10,
            Column = 5,
            Severity = "error",
            Code = "CS0234",
            Message = "The name 'foo' does not exist"
        };

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        json.Should().Contain("\"filePath\":\"Scripts/Player.cs\"");
        json.Should().Contain("\"line\":10");
        json.Should().Contain("\"column\":5");
        json.Should().Contain("\"severity\":\"error\"");
        json.Should().Contain("\"code\":\"CS0234\"");
        json.Should().Contain("\"message\":\"The name 'foo' does not exist\"");
    }

    [Fact]
    public void LogEntry_Defaults()
    {
        var entry = new LogEntry();
        entry.FilePath.Should().BeEmpty();
        entry.Line.Should().Be(0);
        entry.Column.Should().Be(0);
        entry.Severity.Should().BeEmpty();
        entry.Code.Should().BeEmpty();
        entry.Message.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 运行测试验证失败**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test Joker.UnityCli.Tests --filter "LogServiceTests" --no-restore
```

预期：编译失败（`LogEntry` 类型不存在）。

- [ ] **Step 3: 实现 LogEntry 模型**

创建 `.src/Joker.UnityCli/Models/LogEntry.cs`：

```csharp
namespace Joker.UnityCli.Models;

public class LogEntry
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Severity { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
```

- [ ] **Step 4: 运行测试验证通过**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test Joker.UnityCli.Tests --filter "LogServiceTests" --no-restore
```

预期：2 个测试通过。

- [ ] **Step 5: 提交**

```bash
git add .src/Joker.UnityCli/Models/LogEntry.cs .src/Joker.UnityCli.Tests/Services/LogServiceTests.cs
git commit -m "feat: add LogEntry model with serialization tests"
```

---

## Task 4: ILogService 接口 + LogService 核心解析测试

**Files:**
- Create: `.src/Joker.UnityCli/Services/ILogService.cs`
- Modify: `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs`（添加解析测试）
- Create: `.src/Joker.UnityCli/Services/LogService.cs`（先实现最小可编译版本）

- [ ] **Step 1: 写文件不存在 + 空文件的测试**

追加到 `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs`：

```csharp
[Fact]
public async Task GetLogEntriesAsync_FileNotFound_ReturnsEmptyList()
{
    var service = new LogService(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".log"));
    var result = await service.GetLogEntriesAsync(50, false, null);
    result.Should().BeEmpty();
}

[Fact]
public async Task GetLogEntriesAsync_EmptyFile_ReturnsEmptyList()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(50, false, null);
        result.Should().BeEmpty();
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 2: 写正常解析的测试**

追加到 `LogServiceTests`：

```csharp
[Fact]
public async Task GetLogEntriesAsync_WithErrors_ReturnsMatchedEntries()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var logContent = """
            Some random log line
            Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist in the current context
            Another random line
            Assets/Scripts/Enemy.cs(20,10): warning CS0168: The variable 'bar' is declared but never used
            """;
        File.WriteAllText(tempFile, logContent);

        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(50, false, null);

        result.Should().HaveCount(2);
        result[0].FilePath.Should().Be("Assets/Scripts/Player.cs");
        result[0].Line.Should().Be(10);
        result[0].Column.Should().Be(5);
        result[0].Severity.Should().Be("error");
        result[0].Code.Should().Be("CS0234");
        result[0].Message.Should().Be("The name 'foo' does not exist in the current context");
        result[1].FilePath.Should().Be("Assets/Scripts/Enemy.cs");
        result[1].Severity.Should().Be("warning");
        result[1].Code.Should().Be("CS0168");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 3: 运行测试验证失败**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test Joker.UnityCli.Tests --filter "LogServiceTests" --no-restore
```

预期：编译失败（`ILogService` 和 `LogService` 不存在）。

- [ ] **Step 4: 创建 ILogService 接口**

创建 `.src/Joker.UnityCli/Services/ILogService.cs`：

```csharp
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface ILogService
{
    Task<List<LogEntry>> GetLogEntriesAsync(int tailCount, bool errorsOnly, string? projectPath, CancellationToken ct = default);
}
```

- [ ] **Step 5: 创建 LogService 最小实现**

创建 `.src/Joker.UnityCli/Services/LogService.cs`：

```csharp
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class LogService : ILogService
{
    private const int MaxReadSize = 10 * 1024 * 1024; // 10MB

    private static readonly Regex DiagnosticRegex = new(
        @"^(.+?)\((\d+),(\d+)\):\s+(error|warning)\s+(CS\d+):\s+(.+)$",
        RegexOptions.Compiled);

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

        var readStart = Math.Max(0, fileInfo.Length - MaxReadSize);
        var entries = new List<LogEntry>();
        var seen = new HashSet<(string, int, string)>();

        using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(readStart, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            var match = DiagnosticRegex.Match(line);
            if (!match.Success) continue;

            var severity = match.Groups[4].Value;
            if (errorsOnly && severity != "error") continue;

            var filePath = match.Groups[1].Value;
            var lineNum = int.Parse(match.Groups[2].Value);
            var code = match.Groups[5].Value;

            var key = (filePath, lineNum, code);
            if (!seen.Add(key))
                continue;

            entries.Add(new LogEntry
            {
                FilePath = filePath,
                Line = lineNum,
                Column = int.Parse(match.Groups[3].Value),
                Severity = severity,
                Code = code,
                Message = match.Groups[6].Value
            });
        }

        if (!string.IsNullOrEmpty(projectPath))
        {
            var normalizedProjectPath = projectPath.Replace('\\', '/');
            entries = entries.Where(e => e.FilePath.Replace('\\', '/').StartsWith(normalizedProjectPath, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return entries.Skip(Math.Max(0, entries.Count - tailCount)).ToList();
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

- [ ] **Step 6: 运行测试验证通过**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test Joker.UnityCli.Tests --filter "LogServiceTests" --no-restore
```

预期：5 个测试全部通过。

- [ ] **Step 7: 提交**

```bash
git add .src/Joker.UnityCli/Services/ILogService.cs .src/Joker.UnityCli/Services/LogService.cs .src/Joker.UnityCli.Tests/Services/LogServiceTests.cs
git commit -m "feat: add ILogService interface and LogService with core parsing logic"
```

---

## Task 5: LogService 过滤和边界测试

**Files:**
- Modify: `.src/Joker.UnityCli.Tests/Services/LogServiceTests.cs`

- [ ] **Step 1: 写过滤和边界测试**

追加到 `LogServiceTests`：

```csharp
[Fact]
public async Task GetLogEntriesAsync_ErrorsOnly_FiltersWarnings()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var logContent = """
            Assets/Scripts/A.cs(1,1): error CS0234: error message
            Assets/Scripts/B.cs(2,2): warning CS0168: warning message
            """;
        File.WriteAllText(tempFile, logContent);

        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(50, true, null);

        result.Should().HaveCount(1);
        result[0].Severity.Should().Be("error");
        result[0].Code.Should().Be("CS0234");
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public async Task GetLogEntriesAsync_TailCount_LimitsResults()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var lines = Enumerable.Range(0, 10)
            .Select(i => $"Assets/Script{i}.cs({i},1): error CS000{i}: msg{i}")
            .ToList();
        // Pad with non-matching lines to ensure we have enough content
        var logContent = string.Join("\n", lines.Select((l, i) => $"noise line {i}\n{l}"));
        File.WriteAllText(tempFile, logContent);

        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(3, false, null);

        result.Should().HaveCount(3);
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public async Task GetLogEntriesAsync_DuplicateEntries_Deduplicates()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var logContent = """
            Assets/Player.cs(10,5): error CS0234: The name 'foo' does not exist
            Assets/Player.cs(10,5): error CS0234: The name 'foo' does not exist
            """;
        File.WriteAllText(tempFile, logContent);

        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(50, false, null);

        result.Should().HaveCount(1);
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public async Task GetLogEntriesAsync_ProjectPath_FiltersByProject()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var logContent = """
            C:/Projects/MyGame/Assets/Player.cs(10,5): error CS0234: msg1
            C:/Projects/Other/Assets/Enemy.cs(20,10): error CS0234: msg2
            """;
        File.WriteAllText(tempFile, logContent);

        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(50, false, "C:/Projects/MyGame");

        result.Should().HaveCount(1);
        result[0].FilePath.Should().Contain("MyGame");
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public async Task GetLogEntriesAsync_NoProjectPath_ReturnsAll()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var logContent = """
            C:/Projects/A/Script.cs(1,1): error CS0001: msg1
            C:/Projects/B/Script.cs(2,2): error CS0002: msg2
            """;
        File.WriteAllText(tempFile, logContent);

        var service = new LogService(tempFile);
        var result = await service.GetLogEntriesAsync(50, false, null);

        result.Should().HaveCount(2);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 2: 运行测试验证通过**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test Joker.UnityCli.Tests --filter "LogServiceTests" --no-restore
```

预期：全部 LogServiceTests 通过（约 10 个测试）。

- [ ] **Step 3: 提交**

```bash
git add .src/Joker.UnityCli.Tests/Services/LogServiceTests.cs
git commit -m "test: add LogService filter and edge case tests"
```

---

## Task 6: LogsCommand + 命令注册

**Files:**
- Create: `.src/Joker.UnityCli/Commands/LogsCommand.cs`
- Modify: `.src/Joker.UnityCli/Program.cs`
- Create: `.src/Joker.UnityCli.Tests/Commands/LogsCommandTests.cs`

- [ ] **Step 1: 写 LogsCommand 测试**

创建 `.src/Joker.UnityCli.Tests/Commands/LogsCommandTests.cs`：

```csharp
using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;
using Xunit;

namespace Joker.UnityCli.Tests.Commands;

public class LogsCommandTests
{
    [Fact]
    public async Task LogsCommand_WithJson_ReturnsEntries()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(50, false, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<LogEntry>
            {
                new() { FilePath = "Player.cs", Line = 10, Column = 5, Severity = "error", Code = "CS0234", Message = "Test error" }
            }));

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateLogsApp(logService, projectDetector);

        var result = await app.RunAsync(["logs", "--json"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task LogsCommand_WithErrorsFlag_PassesToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(50, true, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<LogEntry>()));

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--errors", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_WithTail_PassesToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(10, false, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<LogEntry>()));

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--tail", "10", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(10, false, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_WithProject_PassesToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(50, false, "/path/to/project", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<LogEntry>()));

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Path = "/path/to/project",
            Name = "TestProject",
            UnityVersion = "2022.3.20f1"
        });

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--project", "/path/to/project", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, false, "/path/to/project", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_AutoDetectProject_PassesPathToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(50, false, "/auto/project", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<LogEntry>()));

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns(new UnityProject
        {
            Path = "/auto/project",
            Name = "AutoProject",
            UnityVersion = "2022.3.20f1"
        });

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, false, "/auto/project", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_AutoDetectFails_PassesNullToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(50, false, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<LogEntry>()));

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, false, null, Arg.Any<CancellationToken>());
    }

    private static CommandApp CreateLogsApp(ILogService logService, IProjectDetector projectDetector)
    {
        var services = new ServiceCollection();
        services.AddSingleton(logService);
        services.AddSingleton(projectDetector);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<LogsCommand>("logs");
        });
        return app;
    }
}
```

- [ ] **Step 2: 运行测试验证失败**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test Joker.UnityCli.Tests --filter "LogsCommandTests" --no-restore
```

预期：编译失败（`LogsCommand` 不存在）。

- [ ] **Step 3: 实现 LogsCommand**

创建 `.src/Joker.UnityCli/Commands/LogsCommand.cs`：

```csharp
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class LogsCommand : AsyncCommand<LogsCommand.Settings>
{
    private readonly ILogService _logService;
    private readonly IProjectDetector _projectDetector;

    public LogsCommand(ILogService logService, IProjectDetector projectDetector)
    {
        _logService = logService;
        _projectDetector = projectDetector;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandOption("--errors")]
        [Description("Show only compilation errors")]
        public bool ErrorsOnly { get; set; }

        [CommandOption("--tail")]
        [Description("Number of recent entries to show (default: 50)")]
        public int Tail { get; set; } = 50;

        [CommandOption("-p|--project <PATH>")]
        [Description("Unity project path (auto-detected if omitted)")]
        public string? ProjectPath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string? projectPath = null;

        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            var project = _projectDetector.Detect(settings.ProjectPath);
            projectPath = project?.Path;
        }
        else
        {
            var project = _projectDetector.DetectFromCurrentDirectory(Environment.CurrentDirectory);
            projectPath = project?.Path;
        }

        var entries = await _logService.GetLogEntriesAsync(settings.Tail, settings.ErrorsOnly, projectPath, cancellationToken);

        if (settings.JsonOutput)
        {
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            Console.WriteLine(json);
            return 0;
        }

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No matching log entries found.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("Severity");
        table.AddColumn("Code");
        table.AddColumn("Location");
        table.AddColumn("Message");

        foreach (var entry in entries)
        {
            var severity = entry.Severity == "error" ? "[red]error[/]" : "[yellow]warning[/]";
            var location = $"{entry.FilePath}:{entry.Line}:{entry.Column}";
            table.AddRow(severity, entry.Code, location, entry.Message.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
```

- [ ] **Step 4: 注册到 Program.cs**

修改 `.src/Joker.UnityCli/Program.cs`，在服务注册区域添加：

```csharp
services.AddSingleton<ILogService, LogService>();
```

在命令注册区域添加：

```csharp
config.AddCommand<LogsCommand>("logs")
    .WithDescription("Show Unity Editor log entries");
```

完整修改后的 `Program.cs`：

```csharp
using Joker.UnityCli.Commands;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Joker.UnityCli;

class Program
{
    static int Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProjectDetector, ProjectDetector>();
        services.AddSingleton<IUnityLocator, UnityLocator>();
        services.AddSingleton<IAssetService, AssetService>();
        services.AddSingleton<IBuildService, BuildService>();
        services.AddSingleton<IExecService, ExecService>();
        services.AddSingleton<ILogService, LogService>();

        var registrar = new DependencyInjectionTypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("joker-unity");
            config.AddCommand<InfoCommand>("info")
                .WithDescription("Display Unity project information");
            config.AddCommand<BuildCommand>("build")
                .WithDescription("Build the Unity project");
            config.AddCommand<AssetsCommand>("assets")
                .WithDescription("List or search project assets");
            config.AddCommand<ExecCommand>("exec")
                .WithDescription("Execute C# code in Unity Editor");
            config.AddCommand<LogsCommand>("logs")
                .WithDescription("Show Unity Editor log entries");
        });

        return app.Run(args);
    }
}
```

- [ ] **Step 5: 运行全部测试**

```bash
cd E:/Work/joker-unity-cli/.src && dotnet test --no-restore
```

预期：全部通过（约 64+ 个测试）。

- [ ] **Step 6: 提交**

```bash
git add .src/Joker.UnityCli/Commands/LogsCommand.cs .src/Joker.UnityCli/Program.cs .src/Joker.UnityCli.Tests/Commands/LogsCommandTests.cs
git commit -m "feat: add logs command with LogService integration"
```

---

## Task 7: 更新 README.md

**Files:**
- Modify: `README.md`

- [ ] **Step 1: 在 README.md 的命令参考区域添加 logs 命令文档**

在 `### joker-unity exec` 之后、`## 退出码` 之前，添加：

````markdown

### `joker-unity logs`

显示 Unity Editor 日志条目（编译错误和警告）。

```
joker-unity logs [选项]
```

**选项：**

| 选项 | 说明 |
|------|------|
| `--errors` | 只显示编译错误 |
| `--tail <N>` | 显示最近 N 条（默认 50） |
| `-p, --project <PATH>` | Unity 项目路径（不指定时自动检测） |
| `--json` | 以 JSON 格式输出 |

**示例：**

```bash
# 显示所有日志条目
joker-unity logs

# 只显示编译错误
joker-unity logs --errors

# 限制 10 条
joker-unity logs --tail 10 --json

# 指定项目路径
joker-unity logs --project /path/to/project --json
```

**JSON 输出示例（`--json`）：**

```json
[
  {
    "filePath": "Assets/Scripts/Player.cs",
    "line": 10,
    "column": 5,
    "severity": "error",
    "code": "CS0234",
    "message": "The name 'foo' does not exist in the current context"
  }
]
```
````

- [ ] **Step 2: 提交**

```bash
git add README.md
git commit -m "docs: add logs command to README"
```

---

## 验证

1. `cd .src && dotnet test` — 全部通过
2. `cd .src/Joker.UnityCli && dotnet run -- logs --json` — 输出合法 JSON（空列表或日志条目）
3. `cd .src/Joker.UnityCli && dotnet run -- logs --errors` — Spectre Table 输出
4. .meta 文件修复后，重新打开 Unity 不再报告 GUID 错误

## Self-Review

1. **Spec coverage:**
   - 跨平台日志路径 → Task 4 Step 5 的 `GetDefaultLogPath()` 方法
   - 异步接口 → ILogService 使用 `Task<List<LogEntry>>`
   - `-p/--project` 过滤 → Task 4 Step 5 的项目过滤逻辑 + Task 6 的自动检测
   - `--errors` / `--tail` / `--json` → Task 6 的 Settings + LogsCommand
   - 去重 → Task 4 Step 5 的 `seen` HashSet
   - 兜底策略 → Task 4 Step 5 的文件不存在/空文件检查
   - 10MB 限制 → Task 4 Step 5 的 `MaxReadSize` 常量

2. **Placeholder scan:** 无 TBD/TODO，所有代码完整。

3. **Type consistency:**
   - `LogEntry` 字段名在模型、测试、命令中一致（`FilePath`, `Line`, `Column`, `Severity`, `Code`, `Message`）
   - `ILogService.GetLogEntriesAsync` 签名在接口、实现、测试 mock 中一致（`int tailCount, bool errorsOnly, string? projectPath, CancellationToken ct`）
   - `LogsCommand.Settings` 属性名与测试断言一致（`ErrorsOnly`, `Tail`, `ProjectPath`）
