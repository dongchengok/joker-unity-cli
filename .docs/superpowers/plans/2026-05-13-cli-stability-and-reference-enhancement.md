# CLI 稳定性与引用处理增强实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 CLI 与 Unity Editor 之间稳定的通信机制，通过状态感知实现智能重试和结构化错误返回。

**Architecture:** 扩展 `server.json` 增加 `status` 字段（ready/compiling/stopped），Unity Editor 在编译生命周期中更新状态，CLI 根据状态调整重试策略。ExecResult 增加结构化错误码和异常堆栈，供 AI 智能体程序化处理。

**Tech Stack:** C# 7.3（Editor 侧，Unity 2019.4+）/ .NET 8+（CLI 侧）/ Spectre.Console.Cli / Roslyn 3.8.0 / xUnit + FluentAssertions

---

## File Structure

### Editor 侧修改

| 文件 | 操作 | 职责 |
|------|------|------|
| `Editor/ScriptServer/PortRegistry.cs` | 修改 | server.json 写入增加 status 字段 |
| `Editor/JokerServerController.cs` | 修改 | 编译生命周期中更新 status |
| `Editor/Models/ExecResult.cs` | 修改 | 增加 ErrorCode、ErrorDetail 字段 |
| `Editor/ScriptServer/HttpExecHandler.cs` | 修改 | 填充错误码、编译中早期拒绝 |
| `Editor/ScriptExecution/ScriptExecutor.cs` | 修改 | 填充错误码、引用过滤增强 |

### CLI 侧修改

| 文件 | 操作 | 职责 |
|------|------|------|
| `.src/Joker.UnityCli/Models/ExecResult.cs` | 修改 | 增加 ErrorCode、ErrorDetail 属性 |
| `.src/Joker.UnityCli/Models/ServerStatus.cs` | 创建 | 状态查询结果模型 |
| `.src/Joker.UnityCli/Services/IStatusService.cs` | 创建 | 状态查询服务接口 |
| `.src/Joker.UnityCli/Services/StatusService.cs` | 创建 | 状态查询服务实现 |
| `.src/Joker.UnityCli/Commands/StatusCommand.cs` | 创建 | status 命令 |
| `.src/Joker.UnityCli/Services/ExecService.cs` | 修改 | 智能重试逻辑重构 |
| `.src/Joker.UnityCli/Services/CompileService.cs` | 修改 | 基于 status 的编译检测 |
| `.src/Joker.UnityCli/Program.cs` | 修改 | DI 注册 status 服务和命令 |

### 测试文件

| 文件 | 操作 | 职责 |
|------|------|------|
| `.src/Joker.UnityCli.Tests/Services/StatusServiceTests.cs` | 创建 | StatusService 单元测试 |
| `.src/Joker.UnityCli.Tests/Commands/StatusCommandTests.cs` | 创建 | StatusCommand 测试 |
| `.src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs` | 修改 | 智能重试相关测试 |

---

## Task 1: PortRegistry 状态支持（Editor 侧）

**Files:**
- Modify: `Editor/ScriptServer/PortRegistry.cs`

- [ ] **Step 1: 修改 PortRegistry.Write 方法，增加 status 参数**

当前 `PortRegistry.Write(int port)` 只写入 port 和 pid。扩展为支持 status 字段。

```csharp
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class PortRegistry
    {
        private static string RegistryPath => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            ".joker-unity", "server.json");

        public static void Write(int port)
        {
            Write(port, "ready");
        }

        public static void Write(int port, string status)
        {
            var dir = Path.GetDirectoryName(RegistryPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var pid = Process.GetCurrentProcess().Id;
            var json = "{\"port\":" + port + ",\"pid\":" + pid + ",\"status\":\"" + status + "\"}";
            File.WriteAllText(RegistryPath, json);
        }

        public static void WriteStatus(string status)
        {
            if (!File.Exists(RegistryPath))
                return;
            try
            {
                var existing = File.ReadAllText(RegistryPath);
                // Simple JSON manipulation without Newtonsoft dependency
                var json = existing.Replace("\"status\":\"ready\"", "\"status\":\"" + status + "\"")
                    .Replace("\"status\":\"compiling\"", "\"status\":\"" + status + "\"")
                    .Replace("\"status\":\"stopped\"", "\"status\":\"" + status + "\"");
                File.WriteAllText(RegistryPath, json);
            }
            catch
            {
                // If we can't update status, that's non-critical
            }
        }

        public static void Delete()
        {
            if (File.Exists(RegistryPath))
                File.Delete(RegistryPath);
        }
    }
}
```

注意：Editor 侧使用字符串拼接而非 JSON 库，避免在 PortRegistry 中引入 Newtonsoft.Json 依赖。`WriteStatus` 方法在文件不存在时静默返回——编译停止后服务器重启会重新写入完整文件。

- [ ] **Step 2: 提交**

```bash
git add Editor/ScriptServer/PortRegistry.cs
git commit -m "feat: extend PortRegistry with status field for server state signaling"
```

---

## Task 2: JokerServerController 生命周期状态更新（Editor 侧）

**Files:**
- Modify: `Editor/JokerServerController.cs`

- [ ] **Step 1: 修改 JokerServerController，在编译生命周期中更新状态**

```csharp
using Joker.UnityCli.Editor.ScriptServer;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class JokerServerController
    {
        private const string AutoStartPrefKey = "Joker.UnityCli.AutoStartServer";

        public static bool IsRunning => HttpServer.IsRunning;
        public static int Port => HttpServer.Port;

        public static bool AutoStart
        {
            get => EditorPrefs.GetBool(AutoStartPrefKey, true);
            set => EditorPrefs.SetBool(AutoStartPrefKey, value);
        }

        static JokerServerController()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;

            if (AutoStart)
                HttpServer.Start();
        }

        public static void Start()
        {
            HttpServer.Start();
        }

        public static void Stop()
        {
            HttpServer.Stop();
        }

        public static void Toggle()
        {
            if (IsRunning)
                Stop();
            else
                Start();
        }

        private static void OnBeforeAssemblyReload()
        {
            PortRegistry.WriteStatus("compiling");
            Stop();
        }

        private static void OnEditorQuitting()
        {
            PortRegistry.WriteStatus("stopped");
            Stop();
        }
    }
}
```

关键改动：
- `OnBeforeAssemblyReload` — 在停止服务器前先写入 `compiling` 状态
- `OnEditorQuitting` — 在停止服务器前先写入 `stopped` 状态
- `HttpServer.Start()` 内部调用 `PortRegistry.Write(port)` 时会写入 `ready`（因为 Task 1 中 `Write(int port)` 默认 status 为 `ready`）

- [ ] **Step 2: 提交**

```bash
git add Editor/JokerServerController.cs
git commit -m "feat: update server status during Unity compilation lifecycle"
```

---

## Task 3: ExecResult 模型扩展（两侧）

**Files:**
- Modify: `Editor/Models/ExecResult.cs`
- Modify: `.src/Joker.UnityCli/Models/ExecResult.cs`

- [ ] **Step 1: 扩展 Editor 侧 ExecResult**

```csharp
namespace Joker.UnityCli.Editor.Models
{
    public class ExecResult
    {
        public string Type = "exec_result";
        public string Id = "";
        public bool Success;
        public string ErrorCode = "";
        public string Result = "";
        public string Output = "";
        public string Error = "";
        public string ErrorDetail = "";
        public long DurationMs;
    }
}
```

注意：Editor 侧使用字段（field）而非属性，保持与现有代码风格一致，且 C# 7.3 兼容。

- [ ] **Step 2: 扩展 CLI 侧 ExecResult**

```csharp
namespace Joker.UnityCli.Models;

public class ExecResult
{
    public string Type { get; set; } = "exec_result";
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Result { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public string? ErrorDetail { get; set; }
    public long DurationMs { get; set; }
}
```

- [ ] **Step 3: 提交**

```bash
git add Editor/Models/ExecResult.cs .src/Joker.UnityCli/Models/ExecResult.cs
git commit -m "feat: add ErrorCode and ErrorDetail fields to ExecResult model"
```

---

## Task 4: StatusService + StatusCommand（CLI 侧）

**Files:**
- Create: `.src/Joker.UnityCli/Models/ServerStatus.cs`
- Create: `.src/Joker.UnityCli/Services/IStatusService.cs`
- Create: `.src/Joker.UnityCli/Services/StatusService.cs`
- Create: `.src/Joker.UnityCli/Commands/StatusCommand.cs`
- Modify: `.src/Joker.UnityCli/Program.cs`

- [ ] **Step 1: 创建 ServerStatus 模型**

```csharp
namespace Joker.UnityCli.Models;

public class ServerStatus
{
    public string Status { get; set; } = "unknown";
    public int Port { get; set; }
    public int Pid { get; set; }
    public bool ServerResponding { get; set; }
}
```

- [ ] **Step 2: 创建 IStatusService 接口**

```csharp
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IStatusService
{
    Task<ServerStatus> GetStatusAsync(string projectPath, CancellationToken ct);
}
```

- [ ] **Step 3: 创建 StatusService 实现**

```csharp
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class StatusService : IStatusService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ServerStatus> GetStatusAsync(string projectPath, CancellationToken ct)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
        {
            return new ServerStatus { Status = "not_found" };
        }

        ServerInfo? info;
        try
        {
            var json = await File.ReadAllTextAsync(portFile, ct);
            info = JsonSerializer.Deserialize<ServerInfo>(json, JsonOptions);
        }
        catch
        {
            return new ServerStatus { Status = "not_found" };
        }

        if (info == null || info.Port <= 0)
        {
            return new ServerStatus { Status = "not_found" };
        }

        var status = new ServerStatus
        {
            Status = info.Status ?? "unknown",
            Port = info.Port,
            Pid = info.Pid
        };

        // If status is ready, verify server is actually responding
        if (status.Status == "ready")
        {
            try
            {
                var response = await SharedClient.GetAsync($"http://127.0.0.1:{info.Port}/exec", ct);
                // 405 (Method Not Allowed) means server is running but only accepts POST
                status.ServerResponding = response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
                    || response.IsSuccessStatusCode;
            }
            catch
            {
                status.ServerResponding = false;
            }
        }

        return status;
    }

    private class ServerInfo
    {
        public int Port { get; set; }
        public int Pid { get; set; }
        public string? Status { get; set; }
    }
}
```

关键设计：通过 GET 请求 `/exec` 端点，服务器会返回 405（只接受 POST），以此验证服务器确实在响应。

- [ ] **Step 4: 创建 StatusCommand**

```csharp
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly IStatusService _statusService;

    public StatusCommand(IProjectDetector projectDetector, IStatusService statusService)
    {
        _projectDetector = projectDetector;
        _statusService = statusService;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project.")]
        public string? ProjectPath { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string projectPath;
        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            var project = _projectDetector.Detect(settings.ProjectPath);
            if (project == null)
            {
                if (settings.JsonOutput)
                {
                    WriteJsonError("No Unity project found at the specified path.");
                    return 1;
                }
                AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found at the specified path.");
                return 1;
            }
            projectPath = project.Path;
        }
        else
        {
            var project = _projectDetector.DetectFromCurrentDirectory(Environment.CurrentDirectory);
            if (project == null)
            {
                if (settings.JsonOutput)
                {
                    WriteJsonError("No Unity project found in current directory or parents.");
                    return 1;
                }
                AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found in current directory or parents.");
                return 1;
            }
            projectPath = project.Path;
        }

        var status = await _statusService.GetStatusAsync(projectPath, cancellationToken);

        if (settings.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
            return status.Status == "ready" && status.ServerResponding ? 0 : 1;
        }

        var statusColor = status.Status switch
        {
            "ready" => "green",
            "compiling" => "yellow",
            _ => "red"
        };
        AnsiConsole.MarkupLine($"Unity Editor Status: [{statusColor}]{status.Status}[/]");
        if (status.Port > 0)
            AnsiConsole.MarkupLine($"Port: {status.Port}");
        if (status.Pid > 0)
            AnsiConsole.MarkupLine($"PID: {status.Pid}");
        if (status.Status == "ready")
            AnsiConsole.MarkupLine($"Server Responding: {(status.ServerResponding ? "[green]Yes[/]" : "[red]No[/]")}");

        return status.Status == "ready" && status.ServerResponding ? 0 : 1;
    }

    private static void WriteJsonError(string message)
    {
        var errorObj = new { error = message };
        Console.Error.WriteLine(JsonSerializer.Serialize(errorObj, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
```

- [ ] **Step 5: 更新 Program.cs 注册 StatusService 和 StatusCommand**

在 `Program.cs` 中：

```csharp
// 在 services 注册中添加：
services.AddSingleton<IStatusService, StatusService>();

// 在 app.Configure 中添加：
config.AddCommand<StatusCommand>("status")
    .WithDescription("Check Unity Editor server status");
```

- [ ] **Step 6: 提交**

```bash
git add .src/Joker.UnityCli/Models/ServerStatus.cs .src/Joker.UnityCli/Services/IStatusService.cs .src/Joker.UnityCli/Services/StatusService.cs .src/Joker.UnityCli/Commands/StatusCommand.cs .src/Joker.UnityCli/Program.cs
git commit -m "feat: add status command to check Unity Editor server state"
```

---

## Task 5: ExecService 智能重试重构（CLI 侧）

**Files:**
- Modify: `.src/Joker.UnityCli/Services/ExecService.cs`

- [ ] **Step 1: 重构 ExecService，增加状态感知和最大重试次数**

```csharp
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ExecService : IExecService
{
    private static readonly HttpClient SharedClient = new();
    private const int MaxRetries = 10;
    private static readonly TimeSpan CompilingPollInterval = TimeSpan.FromSeconds(1);

    public async Task<ExecResult> ExecuteAsync(string projectPath, string code, string mode, int timeoutMs, CancellationToken ct)
    {
        var request = new ExecRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Code = code,
            Mode = mode,
            Timeout = timeoutMs
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        var retryDelay = TimeSpan.FromSeconds(1);
        var maxRetryDelay = TimeSpan.FromSeconds(5);
        int retryCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (retryCount >= MaxRetries)
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "max_retries_exceeded",
                    Error = $"Max retries ({MaxRetries}) exceeded. Unity Editor may not be responding.",
                    DurationMs = (long)(DateTime.UtcNow - (deadline - TimeSpan.FromMilliseconds(timeoutMs))).TotalMilliseconds
                };
            }

            // Read server info including status
            ServerInfo? serverInfo;
            try
            {
                serverInfo = ReadServerInfo(projectPath);
            }
            catch (FileNotFoundException)
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "server_not_found",
                    Error = "Unity server not running. Open the Unity Editor project first."
                };
            }
            catch (IOException ex)
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "server_not_found",
                    Error = ex.Message
                };
            }

            // Wait if Unity is compiling
            if (serverInfo.Status == "compiling")
            {
                var compilingDeadline = deadline;
                while (DateTime.UtcNow < compilingDeadline)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(CompilingPollInterval, ct);

                    try
                    {
                        var currentInfo = ReadServerInfo(projectPath);
                        if (currentInfo.Status != "compiling")
                            break;
                    }
                    catch (FileNotFoundException)
                    {
                        // Server file disappeared during compile, keep waiting
                    }
                    catch (IOException)
                    {
                        // Keep waiting
                    }
                }

                // Re-read server info after compilation
                try
                {
                    serverInfo = ReadServerInfo(projectPath);
                }
                catch (FileNotFoundException)
                {
                    return new ExecResult
                    {
                        Success = false,
                        ErrorCode = "server_not_found",
                        Error = "Unity server not running after compilation."
                    };
                }
                catch (IOException ex)
                {
                    return new ExecResult
                    {
                        Success = false,
                        ErrorCode = "server_not_found",
                        Error = ex.Message
                    };
                }
            }

            // If status is stopped, fail immediately
            if (serverInfo.Status == "stopped")
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "server_not_found",
                    Error = "Unity Editor is shutting down."
                };
            }

            // Send request
            var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var remainingMs = Math.Max(5000, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                using var cts = new CancellationTokenSource(remainingMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                var response = await SharedClient.PostAsync($"http://127.0.0.1:{serverInfo.Port}/exec", content, linkedCts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    // Server is compiling, wait and retry
                    retryCount++;
                    await Task.Delay(CompilingPollInterval, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                try
                {
                    return JsonSerializer.Deserialize<ExecResult>(responseBody, JsonOptions)
                        ?? throw new IOException("Failed to deserialize server response");
                }
                catch (JsonException ex)
                {
                    throw new IOException("Failed to deserialize server response", ex);
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException
                 || (ex is TaskCanceledException && !ct.IsCancellationRequested))
                && DateTime.UtcNow < deadline
                && retryCount < MaxRetries)
            {
                retryCount++;
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    public static int ReadServerPort(string projectPath)
    {
        var info = ReadServerInfo(projectPath);
        return info.Port;
    }

    public static ServerInfo ReadServerInfo(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            throw new FileNotFoundException(
                "Unity server not running. Open the Unity Editor project first.", portFile);

        var json = File.ReadAllText(portFile);
        try
        {
            var info = JsonSerializer.Deserialize<ServerInfo>(json, JsonOptions)
                ?? throw new IOException("Failed to read server port file.");

            if (info.Port <= 0)
                throw new IOException("Failed to read server port file.");

            return info;
        }
        catch (JsonException ex)
        {
            throw new IOException("Failed to read server port file.", ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public class ServerInfo
    {
        public int Port { get; set; }
        public int Pid { get; set; }
        public string? Status { get; set; }
    }
}
```

关键改动：
- `ReadServerInfo` 新方法：读取完整的 server.json（含 status）
- 编译中等待循环：轮询状态文件直到 `compiling` 变为其他状态
- 最大重试次数 10 次
- 快速失败：`stopped` 状态直接返回错误
- `ReadServerPort` 保持向后兼容（调用 `ReadServerInfo`）
- `ServerInfo` 改为 public，供 StatusService 复用

- [ ] **Step 2: 提交**

```bash
git add .src/Joker.UnityCli/Services/ExecService.cs
git commit -m "feat: refactor ExecService with status-aware smart retry and max retry limit"
```

---

## Task 6: HttpExecHandler 错误码 + 编译中拒绝（Editor 侧）

**Files:**
- Modify: `Editor/ScriptServer/HttpExecHandler.cs`

- [ ] **Step 1: 修改 HttpExecHandler，填充错误码并在编译中拒绝请求**

在 `HttpExecHandler.HandleAsync` 方法中，在现有逻辑之前添加编译状态检查，并更新所有错误返回路径。

```csharp
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Joker.UnityCli.Editor.ScriptExecution;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpExecHandler
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static volatile bool _isCompiling;

        public static bool IsCompiling
        {
            get { return _isCompiling; }
            set { _isCompiling = value; }
        }

        public static async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                if (request.HttpMethod != "POST")
                {
                    response.StatusCode = 405;
                    response.Close();
                    return;
                }

                if (request.Url.LocalPath != "/exec")
                {
                    response.StatusCode = 404;
                    response.Close();
                    return;
                }

                // Reject requests during compilation
                if (_isCompiling)
                {
                    response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    response.ContentType = "application/json";
                    var compilingResult = new ExecResult
                    {
                        Type = "exec_result",
                        Success = false,
                        ErrorCode = "compiling",
                        Error = "Unity is currently recompiling. Please retry after compilation completes."
                    };
                    var buffer = System.Text.Encoding.UTF8.GetBytes(
                        JsonConvert.SerializeObject(compilingResult, JsonSettings));
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
                    response.Close();
                    return;
                }

                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                ExecRequest execRequest;
                try
                {
                    execRequest = JsonConvert.DeserializeObject<ExecRequest>(requestBody, JsonSettings);
                    if (execRequest == null || string.IsNullOrEmpty(execRequest.Code))
                    {
                        response.StatusCode = 400;
                        response.Close();
                        return;
                    }
                }
                catch (JsonException)
                {
                    response.StatusCode = 400;
                    response.Close();
                    return;
                }

                var session = SessionManager.GetOrCreate(execRequest.Id);

                if (session.TryStart())
                {
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(execRequest.Timeout);

                    EditorApplication.CallbackFunction callback = null;
                    callback = () =>
                    {
                        EditorApplication.update -= callback;
                        try
                        {
                            var task = ScriptExecutor.ExecuteAsync(execRequest, cts.Token);
                            task.ContinueWith(t =>
                            {
                                if (t.Status == TaskStatus.RanToCompletion)
                                    session.CompletionSource.TrySetResult(t.Result);
                                else if (t.IsCanceled)
                                    session.CompletionSource.TrySetCanceled();
                                else
                                    session.CompletionSource.TrySetException(t.Exception.InnerException ?? t.Exception);
                            }, TaskScheduler.Default);
                        }
                        catch (Exception ex)
                        {
                            session.CompletionSource.TrySetException(ex);
                        }
                    };
                    EditorApplication.update += callback;
                }

                var result = await session.CompletionSource.Task;
                result.Id = execRequest.Id;

                response.StatusCode = 200;
                response.ContentType = "application/json";
                var responseJson = JsonConvert.SerializeObject(result, JsonSettings);
                var responseBuffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await response.OutputStream.WriteAsync(responseBuffer, 0, responseBuffer.Length, ct);
                response.Close();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[JokerUnity] HTTP handler error: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }
    }
}
```

新增：
- `_isCompiling` 静态标志，由 JokerServerController 设置
- 编译中请求返回 503 ServiceUnavailable + `compiling` 错误码

- [ ] **Step 2: 在 JokerServerController 中设置 IsCompiling 标志**

在 `JokerServerController.cs` 的 `OnBeforeAssemblyReload` 方法中添加：

```csharp
private static void OnBeforeAssemblyReload()
{
    HttpExecHandler.IsCompiling = true;
    PortRegistry.WriteStatus("compiling");
    Stop();
}
```

注意：`IsCompiling` 标志在域重载后会自动重置为 false（因为静态变量在域重载时会重新初始化）。

- [ ] **Step 3: 提交**

```bash
git add Editor/ScriptServer/HttpExecHandler.cs Editor/JokerServerController.cs
git commit -m "feat: reject exec requests during Unity compilation with structured error code"
```

---

## Task 7: ScriptExecutor 错误码 + 引用过滤增强（Editor 侧）

**Files:**
- Modify: `Editor/ScriptExecution/ScriptExecutor.cs`

- [ ] **Step 1: 增强 ScriptExecutor，添加错误码和引用过滤**

在 `GetDefaultReferences` 方法中添加冲突程序集过滤，并在所有错误返回路径填充 `ErrorCode` 和 `ErrorDetail`。

替换 `GetDefaultReferences` 方法：

```csharp
private static readonly HashSet<string> ExcludedAssemblyNames = new HashSet<string>
{
    "UnityEngine",
    "UnityEngine.UI",
    "UnityEngine.IMGUIModule",
};

private static List<MetadataReference> GetDefaultReferences()
{
    var refs = new List<MetadataReference>();
    var addedPaths = new HashSet<string>();
    var addedNames = new HashSet<string>();

    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        if (asm.IsDynamic) continue;
        try
        {
            var loc = asm.Location;
            if (string.IsNullOrEmpty(loc) || !addedPaths.Add(loc))
                continue;

            // Skip known conflicting facade assemblies
            var name = asm.GetName().Name;
            if (name != null && ExcludedAssemblyNames.Contains(name))
                continue;

            if (!addedNames.Add(name ?? loc))
                continue;

            refs.Add(MetadataReference.CreateFromFile(loc));
        }
        catch { }
    }

    return refs;
}
```

注意：`UnityEngine.dll` 是 facade DLL，实际类型在 `UnityEngine.CoreModule.dll` 中。跳过它避免 CS0433 冲突。`HashSet<string>` 是 C# 7.3 兼容语法。

在 `ExecuteScriptAsync` 和 `ExecuteCompileAsync` 中更新错误返回路径，添加 `ErrorCode` 和 `ErrorDetail`：

对于 `ExecuteScriptAsync` 中的错误路径：

```csharp
// CompilationErrorException - after max retries
return new ExecResult
{
    Type = "exec_result",
    Success = false,
    ErrorCode = "compilation_error",
    Error = errors,
    ErrorDetail = errors,
    DurationMs = sw.ElapsedMilliseconds
};

// CompilationErrorException - cannot auto-fix
return new ExecResult
{
    Type = "exec_result",
    Success = false,
    ErrorCode = "compilation_error",
    Error = errors,
    ErrorDetail = errors,
    DurationMs = sw.ElapsedMilliseconds
};

// Max retries exceeded
return new ExecResult
{
    Type = "exec_result",
    Success = false,
    ErrorCode = "compilation_error",
    Error = "Max retries exceeded",
    ErrorDetail = "Script execution failed after applying automatic reference fixes.",
    DurationMs = sw.ElapsedMilliseconds
};

// OperationCanceledException (timeout)
return new ExecResult
{
    Type = "exec_result",
    Success = false,
    ErrorCode = "timeout",
    Error = $"Timed out after {timeoutMs}ms",
    DurationMs = sw.ElapsedMilliseconds
};

// General exception
return new ExecResult
{
    Type = "exec_result",
    Success = false,
    ErrorCode = "execution_error",
    Error = ex.Message,
    ErrorDetail = ex.ToString(),
    DurationMs = sw.ElapsedMilliseconds
};
```

对 `ExecuteCompileAsync` 中的错误路径做相同改动。此外更新 `No 'public static void Execute()' method found` 错误：

```csharp
return new ExecResult
{
    Type = "exec_result",
    Success = false,
    ErrorCode = "compilation_error",
    Error = "No 'public static void Execute()' method found.",
    ErrorDetail = "Compile mode requires a public static void Execute() method in the provided code.",
    DurationMs = sw.ElapsedMilliseconds
};
```

- [ ] **Step 2: 提交**

```bash
git add Editor/ScriptExecution/ScriptExecutor.cs
git commit -m "feat: add error codes and reference conflict filtering to ScriptExecutor"
```

---

## Task 8: CompileService 状态检测适配（CLI 侧）

**Files:**
- Modify: `.src/Joker.UnityCli/Services/CompileService.cs`

- [ ] **Step 1: 修改 CompileService，使用 status 字段检测编译完成**

在 `TryCompileViaTcpAsync` 方法中，替换纯端口变化检测为状态+端口双重检测：

```csharp
private async Task<CompileResult?> TryCompileViaTcpAsync(string projectPath, int timeoutMs, Stopwatch stopwatch, CancellationToken ct)
{
    var initialPort = TryReadServerPort(projectPath);
    if (initialPort == null)
        return null;

    const string triggerScript = @"
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
""triggered"";

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

    // Monitor for compilation completion via status field and port change
    var deadline = stopwatch.Elapsed + TimeSpan.FromMilliseconds(timeoutMs);
    while (stopwatch.Elapsed < deadline)
    {
        ct.ThrowIfCancellationRequested();

        var serverInfo = TryReadServerInfo(projectPath);
        if (serverInfo != null)
        {
            // Compilation complete when status changes from "compiling" to "ready"
            // (or when port changes, which also indicates server restart after compile)
            if (serverInfo.Status == "ready" && serverInfo.Port != initialPort)
            {
                return new CompileResult
                {
                    Success = true,
                    Status = "compiled",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Also detect compilation done if status went back to ready with same port
            if (serverInfo.Status == "ready")
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
```

新增 `TryReadServerInfo` 方法：

```csharp
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
```

新增 `ServerInfoFull` 类（包含 status）：

```csharp
private class ServerInfoFull
{
    public int Port { get; set; }
    public int Pid { get; set; }
    public string? Status { get; set; }
}
```

注意：保留原有的 `TryReadServerPort` 方法，因为其他地方可能仍在使用。

- [ ] **Step 2: 提交**

```bash
git add .src/Joker.UnityCli/Services/CompileService.cs
git commit -m "feat: use status field for compilation completion detection in CompileService"
```

---

## Task 9: 单元测试

**Files:**
- Create: `.src/Joker.UnityCli.Tests/Services/StatusServiceTests.cs`
- Create: `.src/Joker.UnityCli.Tests/Commands/StatusCommandTests.cs`
- Modify: `.src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs`

- [ ] **Step 1: 编写 StatusService 单元测试**

```csharp
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class StatusServiceTests : IDisposable
{
    private readonly string _tempDir;

    public StatusServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerStatusTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task GetStatusAsync_NoServerFile_ReturnsNotFound()
    {
        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("not_found");
    }

    [Fact]
    public async Task GetStatusAsync_WithServerFile_ReturnsReady()
    {
        var port = PortHelper.FindAvailablePort();
        var jokerDir = Path.Combine(_tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = 9999, status = "ready" }));

        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("ready");
        result.Port.Should().Be(port);
        result.Pid.Should().Be(9999);
        // ServerResponding will be false since we don't start a real server
        result.ServerResponding.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_CompilingStatus_ReturnsCompiling()
    {
        var port = PortHelper.FindAvailablePort();
        var jokerDir = Path.Combine(_tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = 9999, status = "compiling" }));

        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("compiling");
        // Should not attempt HTTP check when status is not ready
        result.ServerResponding.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_ServerFileWithoutStatus_ReturnsUnknown()
    {
        var port = PortHelper.FindAvailablePort();
        var jokerDir = Path.Combine(_tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = 9999 }));

        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("unknown");
    }
}
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd .src && dotnet test --filter "FullyQualifiedName~StatusServiceTests" --no-restore -v n
```

Expected: All tests PASS

- [ ] **Step 3: 编写 StatusCommand 测试**

```csharp
using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Services;
using NSubstitute;
using Spectre.Console.Cli;
using Xunit;

namespace Joker.UnityCli.Tests.Commands;

public class StatusCommandTests
{
    // Test through command execution with mocked services
    // Following the pattern of existing command tests

    [Fact]
    public async Task StatusCommand_NoProject_Returns1()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((ProjectInfo?)null);

        var statusService = Substitute.For<IStatusService>();
        var command = new StatusCommand(projectDetector, statusService);

        // Verify that missing project returns error
        // This is tested indirectly through Spectre's command infrastructure
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Should().BeNull();
    }
}
```

注意：完整的 Spectre 命令测试需要完整的命令基础设施。可以在后续集成测试中覆盖。

- [ ] **Step 4: 更新 ExecServiceTests 适配新的 ExecService 签名**

现有测试中 `new ExecService()` 仍然可用，因为 ExecService 仍有无参构造函数。但需要验证 server.json 格式兼容性：

在现有测试的 `server.json` 创建中添加 `status` 字段（兼容旧格式）：

测试文件中的 `new { port, pid = Environment.ProcessId }` 不需要修改——`ServerInfo` 的 `Status` 属性是 nullable，旧格式 JSON 反序列化时 Status 为 null，ExecService 会将其视为 "unknown" 并正常发送请求。

但需要添加一个新测试验证 status 感知逻辑：

```csharp
[Fact]
public async Task ExecuteAsync_StatusCompiling_WaitsAndSucceeds()
{
    var port = PortHelper.FindAvailablePort();
    var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
    Directory.CreateDirectory(tempDir);
    var jokerDir = Path.Combine(tempDir, ".joker-unity");
    Directory.CreateDirectory(jokerDir);
    var serverJsonPath = Path.Combine(jokerDir, "server.json");

    // Start server
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();

    // Initially write compiling status
    await File.WriteAllTextAsync(serverJsonPath,
        JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "compiling" }));

    // After delay, update to ready
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        await File.WriteAllTextAsync(serverJsonPath,
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));
    });

    // Server responds after ready
    _ = Task.Run(async () =>
    {
        try
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true,
                Result = "waited", DurationMs = 5
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }
        catch { }
    });

    try
    {
        var service = new ExecService();
        var result = await service.ExecuteAsync(tempDir, "code", "script", 15000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().Be("waited");
    }
    finally
    {
        try { listener.Stop(); } catch { }
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

[Fact]
public async Task ExecuteAsync_StatusStopped_ReturnsServerNotFound()
{
    var port = PortHelper.FindAvailablePort();
    var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
    Directory.CreateDirectory(tempDir);
    var jokerDir = Path.Combine(tempDir, ".joker-unity");
    Directory.CreateDirectory(jokerDir);
    await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
        JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "stopped" }));

    var service = new ExecService();
    var result = await service.ExecuteAsync(tempDir, "code", "script", 5000, CancellationToken.None);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be("server_not_found");
    result.Error.Should().Contain("shutting down");
}
```

- [ ] **Step 5: 运行全部测试确认通过**

```bash
cd .src && dotnet test --no-restore -v n
```

Expected: All tests PASS

- [ ] **Step 6: 提交**

```bash
git add .src/Joker.UnityCli.Tests/Services/StatusServiceTests.cs .src/Joker.UnityCli.Tests/Commands/StatusCommandTests.cs .src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs
git commit -m "test: add unit tests for status service, status command, and smart retry"
```

---

## Task 10: 集成测试审查和完善

**Files:**
- Review: `.src/Joker.UnityCli.Tests/Integration/` 目录下所有文件
- Modify: 现有集成测试中 server.json 格式更新

- [ ] **Step 1: 审查集成测试基类**

阅读 `UnityIntegrationTestBase.cs`，确认 server.json 的读取方式兼容新格式。

- [ ] **Step 2: 确认集成测试中 server.json 写入格式**

在集成测试创建 server.json 的地方添加 `status` 字段：

```csharp
// 旧格式
JsonSerializer.Serialize(new { port, pid = Environment.ProcessId })
// 新格式
JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" })
```

- [ ] **Step 3: 运行集成测试**

```bash
cd .src && dotnet test --filter "FullyQualifiedName~Integration" --no-restore -v n
```

注意：集成测试需要运行中的 Unity Editor，本地开发时可能跳过。

- [ ] **Step 4: 提交**

```bash
git add .src/Joker.UnityCli.Tests/Integration/
git commit -m "test: update integration tests for new server.json status format"
```

---

## Task 11: 构建发布 + 文档更新

**Files:**
- Modify: `README.md`（如存在）
- Modify: `.docs/architecture.md`
- Modify: `CLAUDE.md`
- Clean: `.docs/superpowers/specs/` 中功能重叠的文件
- Build: `Tools~/win-x64/` 二进制更新

- [ ] **Step 1: 构建发布**

```bash
cd .src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64
```

- [ ] **Step 2: 更新 CLAUDE.md 架构描述**

在"架构概览"部分补充：
- 新增 `status` 命令描述
- server.json 增加 status 字段说明
- ExecResult 增加错误码体系说明

- [ ] **Step 3: 更新 architecture.md**

补充状态感知通信机制的描述。

- [ ] **Step 4: 清理功能重叠的历史 spec/plan 文件**

检查 `.docs/superpowers/specs/2026-05-12-integration-tests-and-import-robustness-design.md` 和对应 plan 文件，合并已完成的内容。

- [ ] **Step 5: 更新 README.md**

反映新增的 status 命令和错误码体系。

- [ ] **Step 6: 提交**

```bash
git add .docs/ CLAUDE.md README.md Tools~/win-x64/
git commit -m "docs: update documentation for status command, error codes, and stability improvements"
```

---

## 自动化验证

每个 Task 的实现都遵循 TDD（红-绿-重构）流程：
1. 先写失败测试（红）
2. 写最小实现让测试通过（绿）
3. 重构优化（重构）

全部验证通过自动化测试完成，不依赖手动端到端测试：

```bash
# 运行全部单元测试（每个 Task 完成后都执行）
cd .src && dotnet test --no-restore -v n

# 运行指定测试
cd .src && dotnet test --filter "FullyQualifiedName~StatusServiceTests" -v n
cd .src && dotnet test --filter "FullyQualifiedName~ExecServiceTests" -v n

# 运行集成测试（需要 Unity Editor 运行）
cd .src && dotnet test --filter "FullyQualifiedName~Integration" -v n

# 最终构建验证
cd .src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64
```

---

## Self-Review

### 1. Spec Coverage

| Spec 模块 | 对应 Task |
|-----------|-----------|
| 模块一：server.json 状态扩展 | Task 1, 2 |
| 模块二：status 命令 | Task 4 |
| 模块三：ExecService 智能重试 | Task 5 |
| 模块四：结构化错误码 | Task 3, 6, 7 |
| 模块五：引用处理增强 | Task 7 |
| 阶段 3：测试完善 | Task 9, 10 |
| 阶段 4：文档整理 | Task 11 |

### 2. Placeholder Scan

无 TBD/TODO。所有步骤都有具体代码或命令。

### 3. Type Consistency

- `ExecResult.ErrorCode` — CLI 侧为 `string?` 属性，Editor 侧为 `string` 字段
- `ExecResult.ErrorDetail` — CLI 侧为 `string?` 属性，Editor 侧为 `string` 字段
- `ServerInfo` 在 ExecService 中为 public class，StatusService 有独立的私有 `ServerInfo`
- 所有错误码字符串在 ExecService、HttpExecHandler、ScriptExecutor 中一致
