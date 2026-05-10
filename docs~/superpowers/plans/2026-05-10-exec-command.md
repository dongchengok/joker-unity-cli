# Exec 命令实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 `joker-unity exec` 命令，通过 TCP 将 C# 代码发送到 Unity Editor 执行并返回结果。

**Architecture:** CLI 通过 TCP 连接 Unity Editor 内的常驻服务器，服务器接收代码后在主线程通过 Roslyn Scripting API 编译执行，结果通过 TCP 返回。Unity 侧使用 `[InitializeOnLoad]` 自启动，Domain Reload 时自动重启。

**Tech Stack:** C# / .NET 8（CLI 侧）、Unity Editor C# / Roslyn Scripting 3.8.0（Unity 侧）、TCP + NDJSON 协议

---

## 决策记录

| 决策项 | 结论 | 理由 |
|--------|------|------|
| 通信方式 | TCP Socket（localhost） | 简单、低延迟、跨平台、无额外依赖 |
| 代码执行 | Roslyn Scripting API 3.8.0 | 完整 C# 支持、前向兼容 CoreCLR、netstandard2.0 兼容 Unity Mono |
| 协议格式 | NDJSON（换行分隔 JSON） | 简单可靠，StreamReader.ReadLine 即可解析 |
| 执行模式 | 单线程顺序执行 | Unity API 仅主线程可用，AI 调用天然顺序 |
| 端口发现 | `.joker-unity/server.json` | 支持多项目并行，自然作用域隔离 |
| 连接模式 | 一次连接一个请求 | 简单，无需管理连接状态 |

## 架构图

```
CLI (joker-unity exec)
  → 读取 <project>/.joker-unity/server.json 获取端口
  → TCP 连接 localhost:port
  → 发送 NDJSON 请求
  → 接收 NDJSON 响应
  → 输出结果（文本/JSON）

Unity Editor
  → [InitializeOnLoad] 自动启动 TCP Server
  → 后台线程 Accept 连接 + 读取请求
  → EditorApplication.update 主线程调度执行
  → Roslyn CSharpScript.EvaluateAsync 编译执行
  → 返回结果给 CLI
  → Domain Reload: beforeAssemblyReload 停止 → afterAssemblyReload 重启
```

## 文件结构

```
Editor/
├── Joker.UnityCli.Editor.asmdef
├── Plugins/Roslyn/                          # 6 个 Roslyn DLL（netstandard2.0）
├── ScriptServer/
│   ├── ScriptServer.cs                      # TCP 服务器生命周期
│   ├── ScriptServerSession.cs               # 单连接处理
│   └── PortRegistry.cs                     # 端口文件读写
├── ScriptExecution/
│   ├── ScriptExecutor.cs                    # Roslyn Scripting 封装
│   └── ScriptGlobals.cs                    # 脚本全局变量
├── Models/
│   ├── ExecRequest.cs
│   └── ExecResult.cs
└── ScriptServerBootstrap.cs                 # [InitializeOnLoad] 自启动

src/Joker.UnityCli/
├── Commands/ExecCommand.cs                  # 新 exec 命令
├── Services/
│   ├── IExecService.cs
│   └── ExecService.cs                      # TCP 客户端
└── Models/
    ├── ExecRequest.cs
    └── ExecResult.cs

src/Joker.UnityCli.Tests/
├── Services/ExecServiceTests.cs
└── Commands/ExecCommandTests.cs
```

## TCP 协议

**传输**：TCP + NDJSON（每条消息一行 JSON，UTF-8 编码，`\n` 结尾）

**请求**（CLI → Unity）：
```json
{"type":"exec","id":"a1b2c3d4","code":"UnityEngine.Debug.Log(\"hello\")","mode":"script","timeout":30000}
```

- `mode`: `"script"`（语句级，用 CSharpScript）或 `"compile"`（完整 .cs 文件，用 CSharpCompilation）
- CLI inline code → `"script"`，CLI `--file` → `"compile"`

**响应**（Unity → CLI）：
```json
{"type":"exec_result","id":"a1b2c3d4","success":true,"result":"null","output":"hello\n","error":null,"durationMs":42}
```

---

## Task 1: CLI Models（ExecRequest + ExecResult）

**Files:**
- Create: `src/Joker.UnityCli/Models/ExecRequest.cs`
- Create: `src/Joker.UnityCli/Models/ExecResult.cs`
- Create: `src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs`

- [ ] **Step 1: 写 ExecRequest 失败测试**

```csharp
// src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs
using System.Text.Json;
using Joker.UnityCli.Models;
using Xunit;
using FluentAssertions;

namespace Joker.UnityCli.Tests.Services;

public class ExecServiceTests
{
    [Fact]
    public void ExecRequest_Serializes_To_CamelCase_Json()
    {
        var request = new ExecRequest
        {
            Type = "exec",
            Id = "test-id",
            Code = "1+1",
            Timeout = 5000
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().Contain("\"type\":\"exec\"");
        json.Should().Contain("\"id\":\"test-id\"");
        json.Should().Contain("\"code\":\"1+1\"");
        json.Should().Contain("\"timeout\":5000");
    }

    [Fact]
    public void ExecRequest_Deserializes_From_CamelCase_Json()
    {
        var json = "{\"type\":\"exec\",\"id\":\"abc\",\"code\":\"2+2\",\"timeout\":10000}";
        var request = JsonSerializer.Deserialize<ExecRequest>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })!;

        request.Type.Should().Be("exec");
        request.Id.Should().Be("abc");
        request.Code.Should().Be("2+2");
        request.Timeout.Should().Be(10000);
    }

    [Fact]
    public void ExecResult_Serializes_To_CamelCase_Json()
    {
        var result = new ExecResult
        {
            Type = "exec_result",
            Id = "test-id",
            Success = true,
            Result = "4",
            Output = null,
            Error = null,
            DurationMs = 10
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"result\":\"4\"");
        json.Should().Contain("\"durationMs\":10");
    }

    [Fact]
    public void ExecResult_Deserializes_Error_Case()
    {
        var json = "{\"type\":\"exec_result\",\"id\":\"x\",\"success\":false,\"result\":null,\"output\":null,\"error\":\"CS0103: foo not found\",\"durationMs\":0}";
        var result = JsonSerializer.Deserialize<ExecResult>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })!;

        result.Success.Should().BeFalse();
        result.Error.Should().Be("CS0103: foo not found");
        result.Result.Should().BeNull();
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd src && dotnet test Joker.UnityCli.Tests --filter ExecServiceTests --no-restore`
Expected: 编译失败（类型不存在）

- [ ] **Step 3: 实现 ExecRequest 模型**

```csharp
// src/Joker.UnityCli/Models/ExecRequest.cs
namespace Joker.UnityCli.Models;

public class ExecRequest
{
    public string Type { get; set; } = "exec";
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Mode { get; set; } = "script"; // "script" or "compile"
    public int Timeout { get; set; } = 30000;
}
```

```csharp
// src/Joker.UnityCli/Models/ExecResult.cs
namespace Joker.UnityCli.Models;

public class ExecResult
{
    public string Type { get; set; } = "exec_result";
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd src && dotnet test Joker.UnityCli.Tests --filter ExecServiceTests`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
git add src/Joker.UnityCli/Models/ExecRequest.cs src/Joker.UnityCli/Models/ExecResult.cs src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs
git commit -m "feat: add ExecRequest and ExecResult models with serialization tests"
```

---

## Task 2: CLI ExecService（TCP 客户端 + 端口发现）

**Files:**
- Create: `src/Joker.UnityCli/Services/IExecService.cs`
- Create: `src/Joker.UnityCli/Services/ExecService.cs`
- Modify: `src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs`（追加测试）

- [ ] **Step 1: 写 ExecService 失败测试**

```csharp
// 追加到 ExecServiceTests.cs

[Fact]
public async Task ExecuteAsync_WhenServerNotRunning_ReturnsConnectionError()
{
    var service = new ExecService();
    var result = await service.ExecuteAsync(
        Path.GetTempPath(), "1+1", 5000, CancellationToken.None);

    result.Should().NotBeNull();
    // 当 server.json 不存在时，应抛出或返回错误
}

[Fact]
public async Task ExecuteAsync_WithMockServer_ReturnsResult()
{
    // 启动一个 mock TCP server
    var listener = TcpListener.Create(0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;

    // 写临时 server.json
    var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
    Directory.CreateDirectory(tempDir);
    var jokerDir = Path.Combine(tempDir, ".joker-unity");
    Directory.CreateDirectory(jokerDir);
    await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
        JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

    // mock server 处理
    var serverTask = Task.Run(async () =>
    {
        var client = await listener.AcceptTcpClientAsync();
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
        var line = await reader.ReadLineAsync();
        // 回复成功
        var response = JsonSerializer.Serialize(new ExecResult
        {
            Type = "exec_result", Id = "test", Success = true,
            Result = "42", DurationMs = 5
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await writer.WriteLineAsync(response);
        client.Close();
    });

    var service = new ExecService();
    var result = await service.ExecuteAsync(tempDir, "6*7", 5000, CancellationToken.None);

    result.Success.Should().BeTrue();
    result.Result.Should().Be("42");

    listener.Stop();
    Directory.Delete(tempDir, true);
}

[Fact]
public void ReadServerPort_WhenFileMissing_ThrowsFileNotFoundException()
{
    var service = new ExecService();
    var act = () => ExecService.ReadServerPort(Path.GetTempPath());
    act.Should().Throw<FileNotFoundException>();
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd src && dotnet test Joker.UnityCli.Tests --filter ExecServiceTests --no-restore`
Expected: 编译失败

- [ ] **Step 3: 实现 IExecService 和 ExecService**

```csharp
// src/Joker.UnityCli/Services/IExecService.cs
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IExecService
{
    Task<ExecResult> ExecuteAsync(string projectPath, string code, int timeoutMs, CancellationToken ct);
}
```

```csharp
// src/Joker.UnityCli/Services/ExecService.cs
using System.Net.Sockets;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ExecService : IExecService
{
    public async Task<ExecResult> ExecuteAsync(string projectPath, string code, int timeoutMs, CancellationToken ct)
    {
        var port = ReadServerPort(projectPath);
        var request = new ExecRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Code = code,
            Mode = string.IsNullOrWhiteSpace(settings.FilePath) ? "script" : "compile",
            Timeout = timeoutMs
        };

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, ct);

        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream) { AutoFlush = true };
        using var reader = new StreamReader(stream);

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        await writer.WriteLineAsync(requestJson);

        var responseLine = await reader.ReadLineAsync(ct)
            ?? throw new IOException("Server closed connection without response");

        return JsonSerializer.Deserialize<ExecResult>(responseLine, JsonOptions)
            ?? throw new IOException("Failed to deserialize server response");
    }

    public static int ReadServerPort(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            throw new FileNotFoundException(
                "Unity server not running. Open the Unity Editor project first.", portFile);

        var json = File.ReadAllText(portFile);
        var info = JsonSerializer.Deserialize<ServerInfo>(json, JsonOptions)
            ?? throw new IOException("Failed to read server port file.");

        return info.Port;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private class ServerInfo
    {
        public int Port { get; set; }
        public int Pid { get; set; }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd src && dotnet test Joker.UnityCli.Tests --filter ExecServiceTests`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
git add src/Joker.UnityCli/Services/IExecService.cs src/Joker.UnityCli/Services/ExecService.cs src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs
git commit -m "feat: add ExecService with TCP client and port discovery"
```

---

## Task 3: CLI ExecCommand

**Files:**
- Create: `src/Joker.UnityCli/Commands/ExecCommand.cs`
- Modify: `src/Joker.UnityCli/Program.cs`（注册命令和服务）
- Create: `src/Joker.UnityCli.Tests/Commands/ExecCommandTests.cs`

- [ ] **Step 1: 写 ExecCommand 失败测试**

测试参数解析、项目检测、JSON 输出模式。参考现有 `JsonOutputTests.cs` 模式。

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现 ExecCommand**

```csharp
// src/Joker.UnityCli/Commands/ExecCommand.cs
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class ExecCommand : AsyncCommand<ExecCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly IExecService _execService;

    public ExecCommand(IProjectDetector projectDetector, IExecService execService)
    {
        _projectDetector = projectDetector;
        _execService = execService;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "[CODE]")]
        [Description("C# code to execute in Unity Editor.")]
        public string? Code { get; set; }

        [CommandOption("-f|--file <PATH>")]
        [Description("Read C# code from a file.")]
        public string? FilePath { get; set; }

        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project.")]
        public string? ProjectPath { get; set; }

        [CommandOption("-t|--timeout <MS>")]
        [Description("Execution timeout in milliseconds.")]
        [DefaultValue(30000)]
        public int Timeout { get; set; } = 30000;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        // 验证：CODE 或 --file 二选一
        var code = settings.Code;
        if (!string.IsNullOrWhiteSpace(settings.FilePath))
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                WriteError(settings, "Cannot specify both inline code and --file.");
                return 1;
            }
            if (!File.Exists(settings.FilePath))
            {
                WriteError(settings, $"File not found: {settings.FilePath}");
                return 1;
            }
            code = await File.ReadAllTextAsync(settings.FilePath, ct);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            WriteError(settings, "No code provided. Use 'joker-unity exec <CODE>' or '--file <PATH>'.");
            return 1;
        }

        // 检测项目路径
        var project = !string.IsNullOrWhiteSpace(settings.ProjectPath)
            ? _projectDetector.Detect(settings.ProjectPath)
            : _projectDetector.DetectFromCurrentDirectory(Environment.CurrentDirectory);

        if (project == null)
        {
            WriteError(settings, "No Unity project found. Specify --project <PATH>.");
            return 1;
        }

        try
        {
            var result = await _execService.ExecuteAsync(project.Path, code, settings.Timeout, ct);

            if (settings.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                return result.Success ? 0 : 1;
            }

            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]Execution succeeded[/] ({result.DurationMs}ms)");
                if (result.Result != null)
                    AnsiConsole.MarkupLine($"  Result: {result.Result}");
                if (result.Output != null)
                    AnsiConsole.MarkupLine($"  Output: {result.Output.TrimEnd()}");
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Execution failed[/]");
                if (result.Error != null)
                    AnsiConsole.MarkupLine($"  Error: {result.Error}");
                return 1;
            }
        }
        catch (FileNotFoundException ex)
        {
            WriteError(settings, ex.Message);
            return 1;
        }
        catch (SocketException)
        {
            WriteError(settings, "Cannot connect to Unity server. Is the Unity Editor open?");
            return 1;
        }
    }

    private void WriteError(Settings settings, string message)
    {
        if (settings.JsonOutput)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = message }, JsonOpts));
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
```

修改 `Program.cs` 注册：
```csharp
// 在 services 注册部分添加
services.AddSingleton<IExecService, ExecService>();

// 在 config.Configure 部分添加
config.AddCommand<ExecCommand>("exec")
    .WithDescription("Execute C# code in Unity Editor");
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: 提交**

```bash
git add src/Joker.UnityCli/Commands/ExecCommand.cs src/Joker.UnityCli/Program.cs src/Joker.UnityCli.Tests/Commands/ExecCommandTests.cs
git commit -m "feat: add exec command with TCP client integration"
```

---

## Task 4: Unity Editor Models + PortRegistry

**Files:**
- Create: `Editor/Models/ExecRequest.cs`
- Create: `Editor/Models/ExecResult.cs`
- Create: `Editor/ScriptServer/PortRegistry.cs`

- [ ] **Step 1: 创建 Unity 侧模型**

与 CLI 侧模型 JSON 格式一致，但使用 Unity 兼容的序列化方式（手动 JSON 或 JsonUtility）。

```csharp
// Editor/Models/ExecRequest.cs
namespace Joker.UnityCli.Editor.Models
{
    public class ExecRequest
    {
        public string Type = "exec";
        public string Id = "";
        public string Code = "";
        public string Mode = "script"; // "script" or "compile"
        public int Timeout = 30000;
    }
}
```

```csharp
// Editor/Models/ExecResult.cs
namespace Joker.UnityCli.Editor.Models
{
    public class ExecResult
    {
        public string Type = "exec_result";
        public string Id = "";
        public bool Success;
        public string Result = "";
        public string Output = "";
        public string Error = "";
        public long DurationMs;
    }
}
```

- [ ] **Step 2: 实现 PortRegistry**

```csharp
// Editor/ScriptServer/PortRegistry.cs
using System.IO;
using UnityEngine;
using System.Diagnostics;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class PortRegistry
    {
        private static string RegistryPath => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            ".joker-unity", "server.json");

        public static void Write(int port)
        {
            var dir = Path.GetDirectoryName(RegistryPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = $"{{\"port\":{port},\"pid\":{Process.GetCurrentProcess().Id}}}";
            File.WriteAllText(RegistryPath, json);
        }

        public static void Delete()
        {
            if (File.Exists(RegistryPath)) File.Delete(RegistryPath);
        }
    }
}
```

- [ ] **Step 3: 手动验证**

在 Unity Console 中确认 `PortRegistry.Write` 能正确生成 `.joker-unity/server.json`。

- [ ] **Step 4: 提交**

```bash
git add Editor/Models/ Editor/ScriptServer/PortRegistry.cs
git commit -m "feat: add Unity Editor models and port registry"
```

---

## Task 5: Unity ScriptExecutor（混合模式：Scripting + Compilation）

**Files:**
- Create: `Editor/ScriptExecution/ScriptGlobals.cs`
- Create: `Editor/ScriptExecution/ScriptExecutor.cs`

两种执行引擎：
- **script 模式**（inline code）：用 `CSharpScript.EvaluateAsync`，快速执行语句级代码
- **compile 模式**（--file）：用 `CSharpCompilation` 编译完整 .cs 文件，通过反射调用入口方法

**入口方法约定**（compile 模式）：代码中必须有一个 `public static void Execute()` 或 `public static object Execute()` 方法。脚本执行器自动查找并调用第一个匹配的类型和方法。

- [ ] **Step 1: 创建 ScriptGlobals**

```csharp
// Editor/ScriptExecution/ScriptGlobals.cs
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public class ScriptGlobals
    {
        public GameObject SelectedObject => Selection.activeGameObject;
        public string ProjectPath => Directory.GetParent(Application.dataPath).FullName;
    }
}
```

- [ ] **Step 2: 创建 ScriptExecutor**

```csharp
// Editor/ScriptExecution/ScriptExecutor.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public static class ScriptExecutor
    {
        public static Task<ExecResult> ExecuteAsync(ExecRequest request, CancellationToken ct)
        {
            return request.Mode == "compile"
                ? ExecuteCompileAsync(request.Code, request.Timeout, ct)
                : ExecuteScriptAsync(request.Code, request.Timeout, ct);
        }

        // script 模式：CSharpScript.EvaluateAsync
        private static async Task<ExecResult> ExecuteScriptAsync(string code, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var options = ScriptOptions.Default
                    .WithImports("UnityEngine", "UnityEditor", "System", "System.Linq", "System.Collections.Generic")
                    .AddReferences(
                        typeof(UnityEngine.Debug).Assembly,
                        typeof(UnityEditor.EditorApplication).Assembly,
                        typeof(object).Assembly,
                        typeof(Enumerable).Assembly
                    );

                var globals = new ScriptGlobals();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);

                var result = await CSharpScript.EvaluateAsync(code, options, globals, typeof(ScriptGlobals), cts.Token);
                sw.Stop();

                return new ExecResult
                {
                    Id = "", // 由调用者填充
                    Success = true,
                    Result = result?.ToString() ?? "null",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (CompilationErrorException ex)
            {
                sw.Stop();
                return new ExecResult
                {
                    Success = false,
                    Error = string.Join("\n", ex.Diagnostics),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new ExecResult
                {
                    Success = false,
                    Error = $"Execution timed out after {timeoutMs}ms",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ExecResult
                {
                    Success = false,
                    Error = ex.ToString(),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        // compile 模式：CSharpCompilation → in-memory assembly → 反射调用 Execute()
        private static async Task<ExecResult> ExecuteCompileAsync(string code, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);

                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(UnityEngine.Debug).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(UnityEditor.EditorApplication).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    // 添加常用 netstandard 引用
                    MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                };

                var compilation = CSharpCompilation.Create(
                    assemblyName: $"JokerExec_{Guid.NewGuid():N}",
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );

                using var ms = new System.IO.MemoryStream();
                var emitResult = compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    sw.Stop();
                    var errors = string.Join("\n", emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                    return new ExecResult
                    {
                        Success = false,
                        Error = errors,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                ms.Seek(0, System.IO.SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());

                // 查找入口方法：public static void/object Execute()
                var executeMethod = assembly.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .FirstOrDefault(m => m.Name == "Execute" && m.GetParameters().Length == 0);

                if (executeMethod == null)
                {
                    sw.Stop();
                    return new ExecResult
                    {
                        Success = false,
                        Error = "No 'public static void Execute()' method found in the compiled code.",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                object? execResult = null;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);

                // 在主线程执行（由调用者保证）
                execResult = executeMethod.Invoke(null, null);

                sw.Stop();
                return new ExecResult
                {
                    Success = true,
                    Result = execResult?.ToString() ?? "null",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new ExecResult
                {
                    Success = false,
                    Error = $"Execution timed out after {timeoutMs}ms",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ExecResult
                {
                    Success = false,
                    Error = ex.ToString(),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }
    }
}
```

- [ ] **Step 3: 手动验证**

在 Unity Editor 中测试两种模式：
```csharp
// script 模式
ScriptExecutor.ExecuteAsync(new ExecRequest { Mode = "script", Code = "1+1", Timeout = 5000 }, default);
// compile 模式
ScriptExecutor.ExecuteAsync(new ExecRequest { Mode = "compile", Code = "using UnityEngine; public class Test { public static void Execute() { Debug.Log(\"hello\"); } }", Timeout = 5000 }, default);
```

- [ ] **Step 4: 提交**

```bash
git add Editor/ScriptExecution/
git commit -m "feat: add ScriptExecutor with script and compile modes"
```

---

## Task 6: Unity ScriptServer（TCP 服务器 + Bootstrap）

**Files:**
- Create: `Editor/ScriptServer/ScriptServerSession.cs`
- Create: `Editor/ScriptServer/ScriptServer.cs`
- Create: `Editor/ScriptServerBootstrap.cs`
- Create: `Editor/Joker.UnityCli.Editor.asmdef`

- [ ] **Step 1: 创建 ScriptServerSession**

```csharp
// Editor/ScriptServer/ScriptServerSession.cs
// 处理单个 TCP 连接：读取请求 → 主线程调度执行 → 返回响应
```

核心逻辑：
- 后台线程读取一行 NDJSON → 反序列化为 `ExecRequest`
- 创建 `TaskCompletionSource<ExecResult>`
- 通过 `EditorApplication.delayCall` 在主线程执行 `ScriptExecutor.ExecuteAsync`
- 后台线程 `await tcs.Task` → 序列化为 JSON → 写回 → 关闭连接

- [ ] **Step 2: 创建 ScriptServer**

```csharp
// Editor/ScriptServer/ScriptServer.cs
// TcpListener 生命周期管理
```

核心逻辑：
- `Start()`: 创建 `TcpListener`（port 0，OS 分配），启动接受循环
- `Stop()`: 停止监听，删除端口文件
- 接受循环在后台线程，顺序处理连接（`SemaphoreSlim(1,1)`）
- 启动后调用 `PortRegistry.Write(port)`

- [ ] **Step 3: 创建 Bootstrap**

```csharp
// Editor/ScriptServerBootstrap.cs
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class ScriptServerBootstrap
    {
        static ScriptServerBootstrap()
        {
            ScriptServer.ScriptServer.Start();
            AssemblyReloadEvents.beforeAssemblyReload += () => ScriptServer.ScriptServer.Stop();
            EditorApplication.quitting += () => ScriptServer.ScriptServer.Stop();
        }
    }
}
```

- [ ] **Step 4: 创建 asmdef**

```json
{
    "name": "Joker.UnityCli.Editor",
    "rootNamespace": "Joker.UnityCli.Editor",
    "references": [],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false
}
```

- [ ] **Step 5: 手动验证**

打开 Unity Editor → Console 显示 "Joker ScriptServer started on port XXXXX" → `.joker-unity/server.json` 生成。

- [ ] **Step 6: 提交**

```bash
git add Editor/ScriptServer/ Editor/ScriptServerBootstrap.cs Editor/Joker.UnityCli.Editor.asmdef
git commit -m "feat: add TCP script server with auto-start and domain reload support"
```

---

## Task 7: Roslyn DLL 打包

**Files:**
- Create: `Editor/Plugins/Roslyn/` 目录 + 6 个 DLL + meta 文件

- [ ] **Step 1: 下载 NuGet 包**

```bash
# 下载 Microsoft.CodeAnalysis.CSharp.Scripting 3.8.0
nuget install Microsoft.CodeAnalysis.CSharp.Scripting -Version 3.8.0 -OutputDirectory /tmp/roslyn-pkg
```

- [ ] **Step 2: 提取 DLL 到 Editor/Plugins/Roslyn/**

从 `lib/netstandard2.0/` 提取：
- `Microsoft.CodeAnalysis.dll`
- `Microsoft.CodeAnalysis.CSharp.dll`
- `Microsoft.CodeAnalysis.CSharp.Scripting.dll`
- `Microsoft.CodeAnalysis.Scripting.dll`
- `System.Collections.Immutable.dll`
- `System.Reflection.Metadata.dll`

每个 DLL 需要 Unity meta 文件（设置 Editor-only platform）。

- [ ] **Step 3: 验证**

Unity Console 无 DLL 加载错误。

- [ ] **Step 4: 提交**

```bash
git add Editor/Plugins/Roslyn/
git commit -m "chore: add Roslyn Scripting 3.8.0 DLLs for Editor-side code execution"
```

---

## Task 8: 集成测试 + 文档更新

**Files:**
- Modify: `README.md`
- Modify: `.gitignore`
- Modify: `CLAUDE.md`

- [ ] **Step 1: 添加 `.joker-unity/` 到 .gitignore**

- [ ] **Step 2: 编译 CLI**

```bash
cd src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64
```

- [ ] **Step 3: 端到端测试**

```bash
CLI="Tools~/win-x64/joker-unity.exe"
PROJECT="Development"

# 验证服务启动
cat $PROJECT/.joker-unity/server.json

# 简单表达式
$CLI exec "1+1" --project $PROJECT --json
# 期望: {"success":true,"result":"2",...}

# Unity API 调用
$CLI exec "UnityEngine.Application.unityVersion" --project $PROJECT --json
# 期望: {"success":true,"result":"2021.3.46f1",...}

# 错误代码
$CLI exec "undefined_var" --project $PROJECT --json
# 期望: {"success":false,"error":"CS0103...",...}

# 从文件执行
echo "UnityEngine.Debug.Log(\"Hello from file\")" > /tmp/test.cs
$CLI exec --file /tmp/test.cs --project $PROJECT
# 期望: Unity Console 显示 "Hello from file"
```

- [ ] **Step 4: Domain Reload 恢复测试**

在 Unity 中创建/修改脚本触发重编译，等待完成后再次运行 CLI exec 命令。

- [ ] **Step 5: 更新 README.md**

添加 `exec` 命令文档到命令参考部分。

- [ ] **Step 6: 更新 CLAUDE.md**

在命令列表和架构概览中添加 exec 命令。

- [ ] **Step 7: 提交**

```bash
git add README.md CLAUDE.md .gitignore
git commit -m "docs: add exec command documentation and update project docs"
```

---

## 关键文件参考

| 文件 | 用途 |
|------|------|
| `src/Joker.UnityCli/Commands/BuildCommand.cs` | 命令模式参考（DI、Settings、JSON 输出） |
| `src/Joker.UnityCli/Commands/GlobalCommandSettings.cs` | Settings 基类（`--json` 标志） |
| `src/Joker.UnityCli/Services/BuildService.cs` | Service 实现模式参考 |
| `src/Joker.UnityCli/Program.cs` | 命令和服务注册位置 |
| `src/Joker.UnityCli.Tests/Commands/JsonOutputTests.cs` | 测试模式参考 |

## 验证清单

- [ ] `dotnet test` — CLI 侧全部测试通过
- [ ] Unity Editor 打开 → 服务自动启动 → `server.json` 生成
- [ ] `joker-unity exec "1+1" --json` → `{"success":true,"result":"2"}`（script 模式）
- [ ] `joker-unity exec "UnityEngine.Application.unityVersion" --json` → 返回版本号（script 模式）
- [ ] 错误代码 → `{"success":false,"error":"CS..."}`（script 模式）
- [ ] `--file` 完整 .cs 文件执行 → 编译模式正常（compile 模式）
- [ ] Domain reload 后服务自动恢复
- [ ] 文本模式输出正常（无 `--json`）
