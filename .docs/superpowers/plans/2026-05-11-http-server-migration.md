# HTTP Server 迁移实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Unity Editor 侧的 TCP Server 完全替换为 HTTP Server，CLI 侧从 TcpClient 改为 HttpClient。

**Architecture:** Unity Editor 侧用 `System.Net.HttpListener` 替代 `TcpListener`，用 `Newtonsoft.Json`（`com.unity.nuget.newtonsoft-json` 2.x UPM 包）替代手动 JSON 解析。CLI 侧用 `System.Net.Http.HttpClient` 替代 `TcpClient`，加入指数退避重试。端口发现机制（`server.json`）不变。

**Tech Stack:** C# / .NET Framework 4.x（Unity 侧）+ .NET 8（CLI 侧） / HttpListener / HttpClient / Newtonsoft.Json（Unity）/ System.Text.Json（CLI）

---

## 文件结构

| 操作 | 文件路径 | 职责 |
|------|---------|------|
| 修改 | `package.json` | 添加 Newtonsoft.Json UPM 依赖 |
| 创建 | `Editor/ScriptServer/HttpServer.cs` | HttpListener 服务器（替代 ScriptServer.cs） |
| 创建 | `Editor/ScriptServer/HttpServer.cs.meta` | Unity 资产元数据 |
| 创建 | `Editor/ScriptServer/HttpExecHandler.cs` | HTTP 请求处理（替代 ScriptServerSession.cs） |
| 创建 | `Editor/ScriptServer/HttpExecHandler.cs.meta` | Unity 资产元数据 |
| 修改 | `Editor/ScriptServerBootstrap.cs` | 引用改为 HttpServer |
| 删除 | `Editor/ScriptServer/ScriptServer.cs` | 旧 TCP 服务器 |
| 删除 | `Editor/ScriptServer/ScriptServer.cs.meta` | 旧元数据 |
| 删除 | `Editor/ScriptServer/ScriptServerSession.cs` | 旧 TCP 会话处理 |
| 删除 | `Editor/ScriptServer/ScriptServerSession.cs.meta` | 旧元数据 |
| 修改 | `.src/Joker.UnityCli/Services/ExecService.cs` | TCP → HTTP + 重试 |
| 修改 | `.src/Joker.UnityCli/Commands/ExecCommand.cs` | SocketException → HttpRequestException |
| 重写 | `.src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs` | TCP mock → HTTP mock |

---

### Task 1: 添加 Newtonsoft.Json UPM 依赖

**Files:**
- Modify: `package.json`

- [ ] **Step 1: 添加依赖到 package.json**

在 `package.json` 中添加 `dependencies` 字段：

```json
{
  "name": "com.joker.unity-cli",
  "displayName": "Joker Unity CLI",
  "version": "0.1.0",
  "unity": "2019.4",
  "description": "CLI tool for AI agents to integrate with Unity development workflows.",
  "license": "MIT",
  "author": {
    "name": "dongchengok"
  },
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "2.0.0"
  }
}
```

- [ ] **Step 2: 提交**

```bash
git add package.json
git commit -m "feat: add Newtonsoft.Json UPM dependency for HTTP server migration"
```

---

### Task 2: 创建 HttpServer.cs（替代 ScriptServer.cs）

**Files:**
- Create: `Editor/ScriptServer/HttpServer.cs`
- Create: `Editor/ScriptServer/HttpServer.cs.meta`
- Reference: `Editor/ScriptServer/PortRegistry.cs`（PortRegistry.Write / PortRegistry.Delete）

- [ ] **Step 1: 创建 HttpServer.cs.meta**

```
fileFormatVersion: 2
guid: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 2: 创建 HttpServer.cs**

```csharp
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpServer
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _port;

        public static int Port => _port;

        public static void Start()
        {
            Stop();

            _port = FindAvailablePort();
            _cts = new CancellationTokenSource();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            PortRegistry.Write(_port);
            Debug.Log($"[JokerUnity] HTTP server started on port {_port}");

            var ct = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var context = await _listener.GetContextAsync();
                        _ = Task.Run(() => HttpExecHandler.HandleAsync(context, ct), ct);
                    }
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Debug.LogError($"[JokerUnity] HTTP server error: {ex.Message}");
                }
            }, ct);
        }

        public static void Stop()
        {
            _cts?.Cancel();
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }
            _cts = null;
            PortRegistry.Delete();
        }

        private static int FindAvailablePort()
        {
            var random = new System.Random();
            for (int i = 0; i < 100; i++)
            {
                var port = random.Next(63000, 63100);
                try
                {
                    var testListener = new HttpListener();
                    testListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    testListener.Start();
                    testListener.Stop();
                    return port;
                }
                catch (HttpListenerException) { }
            }
            throw new InvalidOperationException("Failed to find available port in range 63000-63099");
        }
    }
}
```

- [ ] **Step 3: 提交**

```bash
git add Editor/ScriptServer/HttpServer.cs Editor/ScriptServer/HttpServer.cs.meta
git commit -m "feat: add HttpServer using HttpListener to replace TCP server"
```

---

### Task 3: 创建 HttpExecHandler.cs（替代 ScriptServerSession.cs）

**Files:**
- Create: `Editor/ScriptServer/HttpExecHandler.cs`
- Create: `Editor/ScriptServer/HttpExecHandler.cs.meta`
- Reference: `Editor/Models/ExecRequest.cs`（ExecRequest）
- Reference: `Editor/Models/ExecResult.cs`（ExecResult）
- Reference: `Editor/ScriptExecution/ScriptExecutor.cs`（ScriptExecutor.ExecuteAsync）

- [ ] **Step 1: 创建 HttpExecHandler.cs.meta**

```
fileFormatVersion: 2
guid: b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 2: 创建 HttpExecHandler.cs**

```csharp
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Joker.UnityCli.Editor.ScriptExecution;
using Newtonsoft.Json;
using UnityEditor;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpExecHandler
    {
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

                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                ExecRequest execRequest;
                try
                {
                    execRequest = JsonConvert.DeserializeObject<ExecRequest>(requestBody);
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

                var tcs = new TaskCompletionSource<ExecResult>();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(execRequest.Timeout);

                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        var task = ScriptExecutor.ExecuteAsync(execRequest, cts.Token);
                        task.ContinueWith(t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                                tcs.SetResult(t.Result);
                            else if (t.IsCanceled)
                                tcs.SetCanceled();
                            else
                                tcs.SetException(t.Exception.InnerException ?? t.Exception);
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                };

                var result = await tcs.Task;
                result.Id = execRequest.Id;

                response.StatusCode = 200;
                response.ContentType = "application/json";
                var responseJson = JsonConvert.SerializeObject(result);
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
                response.Close();
            }
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

- [ ] **Step 3: 提交**

```bash
git add Editor/ScriptServer/HttpExecHandler.cs Editor/ScriptServer/HttpExecHandler.cs.meta
git commit -m "feat: add HttpExecHandler with Newtonsoft.Json for HTTP request processing"
```

---

### Task 4: 更新 ScriptServerBootstrap.cs

**Files:**
- Modify: `Editor/ScriptServerBootstrap.cs`

- [ ] **Step 1: 修改引用从 ScriptServer 改为 HttpServer**

将 `Editor/ScriptServerBootstrap.cs` 替换为：

```csharp
using UnityEditor;
using HttpServerClass = Joker.UnityCli.Editor.ScriptServer.HttpServer;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class ScriptServerBootstrap
    {
        static ScriptServerBootstrap()
        {
            HttpServerClass.Start();
            AssemblyReloadEvents.beforeAssemblyReload += () => HttpServerClass.Stop();
            EditorApplication.quitting += () => HttpServerClass.Stop();
        }
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add Editor/ScriptServerBootstrap.cs
git commit -m "refactor: update bootstrap to use HttpServer instead of ScriptServer"
```

---

### Task 5: 删除旧 TCP 文件

**Files:**
- Delete: `Editor/ScriptServer/ScriptServer.cs`
- Delete: `Editor/ScriptServer/ScriptServer.cs.meta`
- Delete: `Editor/ScriptServer/ScriptServerSession.cs`
- Delete: `Editor/ScriptServer/ScriptServerSession.cs.meta`

- [ ] **Step 1: 删除文件并提交**

```bash
git rm Editor/ScriptServer/ScriptServer.cs Editor/ScriptServer/ScriptServer.cs.meta Editor/ScriptServer/ScriptServerSession.cs Editor/ScriptServer/ScriptServerSession.cs.meta
git commit -m "refactor: remove TCP server implementation (replaced by HTTP server)"
```

---

### Task 6: 重写 ExecService.cs（CLI 侧：TCP → HTTP + 重试）

**Files:**
- Modify: `.src/Joker.UnityCli/Services/ExecService.cs`
- Reference: `.src/Joker.UnityCli/Models/ExecRequest.cs`（ExecRequest）
- Reference: `.src/Joker.UnityCli/Models/ExecResult.cs`（ExecResult）
- Reference: `.src/Joker.UnityCli/Services/IExecService.cs`（IExecService 接口不变）

- [ ] **Step 1: 先写失败测试（见 Task 7）再回来写实现**

将 `.src/Joker.UnityCli/Services/ExecService.cs` 替换为：

```csharp
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ExecService : IExecService
{
    private static readonly HttpClient SharedClient = new();

    public async Task<ExecResult> ExecuteAsync(string projectPath, string code, string mode, int timeoutMs, CancellationToken ct)
    {
        var port = ReadServerPort(projectPath);
        var request = new ExecRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Code = code,
            Mode = mode,
            Timeout = timeoutMs
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        var retryDelay = TimeSpan.FromSeconds(1);
        var maxRetryDelay = TimeSpan.FromSeconds(5);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var cts = new CancellationTokenSource(Math.Max(5000, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                var response = await SharedClient.PostAsync($"http://127.0.0.1:{port}/exec", content, linkedCts.Token);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                return JsonSerializer.Deserialize<ExecResult>(responseBody, JsonOptions)
                    ?? throw new IOException("Failed to deserialize server response");
            }
            catch (HttpRequestException ex) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
                content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            }
        }
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

- [ ] **Step 2: 提交**

```bash
git add .src/Joker.UnityCli/Services/ExecService.cs
git commit -m "refactor: rewrite ExecService to use HttpClient with retry logic"
```

---

### Task 7: 更新 ExecCommand.cs — 错误类型适配

**Files:**
- Modify: `.src/Joker.UnityCli/Commands/ExecCommand.cs`

- [ ] **Step 1: 替换 SocketException catch 为 HttpRequestException**

将 `ExecCommand.cs` 中的两个 catch 块替换：

**旧代码（第 178-188 行）：**
```csharp
catch (SocketException)
{
    if (settings.JsonOutput)
    {
        WriteJsonError("Cannot connect to Unity server. Ensure the Editor is running with the Joker plugin.");
        return 1;
    }

    AnsiConsole.MarkupLine("[red]Error:[/] Cannot connect to Unity server. Ensure the Editor is running with the Joker plugin.");
    return 1;
}
```

**新代码：**
```csharp
catch (HttpRequestException)
{
    if (settings.JsonOutput)
    {
        WriteJsonError("Cannot connect to Unity server. Ensure the Editor is running with the Joker plugin.");
        return 1;
    }

    AnsiConsole.MarkupLine("[red]Error:[/] Cannot connect to Unity server. Ensure the Editor is running with the Joker plugin.");
    return 1;
}
```

同时移除文件顶部的 `using System.Net.Sockets;`（不再需要），因为 `System.Net.Http.HttpRequestException` 在全局 using 中。

- [ ] **Step 2: 提交**

```bash
git add .src/Joker.UnityCli/Commands/ExecCommand.cs
git commit -m "refactor: update ExecCommand error handling from SocketException to HttpRequestException"
```

---

### Task 8: 重写 ExecServiceTests.cs（TCP mock → HTTP mock）

**Files:**
- Modify: `.src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs`

- [ ] **Step 1: 编写失败测试**

将 `.src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs` 替换为：

```csharp
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class ExecServiceTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ExecServiceTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void ExecRequest_Serializes_To_CamelCase_Json()
    {
        var request = new ExecRequest
        {
            Id = "test-id-123",
            Code = "Debug.Log(\"Hello World\");",
            Timeout = 60000,
            Mode = "script"
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        json.Should().Contain("\"type\":\"exec\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"code\":\"Debug.Log(\\u0022Hello World\\u0022);\"");
        json.Should().Contain("\"timeout\":60000");
        json.Should().Contain("\"mode\":\"script\"");
    }

    [Fact]
    public void ExecRequest_Deserializes_From_CamelCase_Json()
    {
        var json = "{\"type\":\"exec\",\"id\":\"test-id-123\",\"code\":\"Debug.Log(\\u0022Hello World\\u0022);\",\"timeout\":60000,\"mode\":\"script\"}";
        var request = JsonSerializer.Deserialize<ExecRequest>(json, _jsonOptions);

        request.Should().NotBeNull();
        request!.Type.Should().Be("exec");
        request.Id.Should().Be("test-id-123");
        request.Code.Should().Be("Debug.Log(\"Hello World\");");
        request.Timeout.Should().Be(60000);
        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecResult_Serializes_To_CamelCase_Json()
    {
        var result = new ExecResult
        {
            Id = "test-id-123",
            Success = true,
            Result = "Success",
            Output = "Hello World",
            DurationMs = 1500
        };

        var json = JsonSerializer.Serialize(result, _jsonOptions);
        json.Should().Contain("\"type\":\"exec_result\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"result\":\"Success\"");
        json.Should().Contain("\"output\":\"Hello World\"");
        json.Should().Contain("\"durationMs\":1500");
    }

    [Fact]
    public void ExecResult_Deserializes_Error_Case()
    {
        var json = "{\"type\":\"exec_result\",\"id\":\"test-id-123\",\"success\":false,\"error\":\"Compilation error\",\"durationMs\":1000}";
        var result = JsonSerializer.Deserialize<ExecResult>(json, _jsonOptions);

        result.Should().NotBeNull();
        result!.Type.Should().Be("exec_result");
        result.Id.Should().Be("test-id-123");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Compilation error");
        result.DurationMs.Should().Be(1000);
        result.Result.Should().BeNull();
        result.Output.Should().BeNull();
    }

    [Fact]
    public void ExecRequest_Default_Mode_Is_Script()
    {
        var request = new ExecRequest();

        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecResult_Defaults()
    {
        var result = new ExecResult();

        result.Type.Should().Be("exec_result");
    }

    [Fact]
    public async Task ExecuteAsync_WithMockHttpServer_ReturnsResult()
    {
        var listener = new HttpListener();
        var port = FindAvailablePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true,
                Result = "42", DurationMs = 5
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        var service = new ExecService();
        var result = await service.ExecuteAsync(tempDir, "6*7", "script", 5000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().Be("42");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void ReadServerPort_WhenFileMissing_ThrowsFileNotFoundException()
    {
        var act = () => ExecService.ReadServerPort(Path.GetTempPath());
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_SendsCorrectMode()
    {
        var port = FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

        string? receivedBody = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true, DurationMs = 1
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        var service = new ExecService();
        await service.ExecuteAsync(tempDir, "code", "compile", 5000, CancellationToken.None);

        receivedBody.Should().NotBeNull();
        var request = JsonSerializer.Deserialize<ExecRequest>(receivedBody!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        request!.Mode.Should().Be("compile");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    private static int FindAvailablePort()
    {
        var random = new Random();
        for (int i = 0; i < 100; i++)
        {
            var port = random.Next(63000, 63100);
            try
            {
                var testListener = new HttpListener();
                testListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                testListener.Start();
                testListener.Stop();
                return port;
            }
            catch (HttpListenerException) { }
        }
        throw new InvalidOperationException("No available port found for test");
    }
}
```

- [ ] **Step 2: 运行测试验证**

```bash
cd .src && dotnet test --filter "FullyQualifiedName~ExecServiceTests" -v normal
```

Expected: ALL PASS

- [ ] **Step 3: 运行全部测试验证无回归**

```bash
cd .src && dotnet test -v normal
```

Expected: ALL PASS

- [ ] **Step 4: 提交**

```bash
git add .src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs
git commit -m "test: rewrite ExecService tests for HTTP server with HttpListener mock"
```

---

### Task 9: 最终验证和提交

**Files:**
- 所有变更文件

- [ ] **Step 1: 运行全部单元测试**

```bash
cd .src && dotnet test -v normal
```

Expected: ALL PASS，0 failed

- [ ] **Step 2: 编译 CLI 发布版本**

```bash
cd .src/Joker.UnityCli && dotnet build -c Release
```

Expected: BUILD SUCCEEDED，0 errors，0 warnings

- [ ] **Step 3: 检查所有文件变更**

```bash
git status
git diff --stat
```

Expected 变更文件列表：
- `package.json`（modified）
- `Editor/ScriptServer/HttpServer.cs`（new）
- `Editor/ScriptServer/HttpServer.cs.meta`（new）
- `Editor/ScriptServer/HttpExecHandler.cs`（new）
- `Editor/ScriptServer/HttpExecHandler.cs.meta`（new）
- `Editor/ScriptServerBootstrap.cs`（modified）
- `Editor/ScriptServer/ScriptServer.cs`（deleted）
- `Editor/ScriptServer/ScriptServer.cs.meta`（deleted）
- `Editor/ScriptServer/ScriptServerSession.cs`（deleted）
- `Editor/ScriptServer/ScriptServerSession.cs.meta`（deleted）
- `.src/Joker.UnityCli/Services/ExecService.cs`（modified）
- `.src/Joker.UnityCli/Commands/ExecCommand.cs`（modified）
- `.src/Joker.UnityCli.Tests/Services/ExecServiceTests.cs`（modified）

- [ ] **Step 4: 手动集成测试（需 Unity Editor）**

在 Unity Editor 打开 `.Unity2019` 项目后：
1. 确认 Console 输出 `[JokerUnity] HTTP server started on port XXXXX`
2. 用 curl 测试：`curl -X POST http://127.0.0.1:XXXXX/exec -H "Content-Type: application/json" -d "{\"type\":\"exec\",\"id\":\"test\",\"code\":\"1+1\",\"mode\":\"script\",\"timeout\":30000}"`
3. 用 CLI 测试：`cd .src/Joker.UnityCli && dotnet run -- exec "1+1" --project ../../.Unity2019 --json`

---

## 自审清单

- [x] **Spec 覆盖：** 设计文档中的每个需求都有对应 Task
  - HttpListener 替代 TcpListener → Task 2, 3
  - Newtonsoft.Json 替代手动解析 → Task 3
  - HttpClient 替代 TcpClient + 重试 → Task 6
  - 端口范围 63000-63099 → Task 2
  - package.json 依赖 → Task 1
  - Bootstrap 更新 → Task 4
  - 旧文件清理 → Task 5
  - ExecCommand 错误类型 → Task 7
  - 测试更新 → Task 8
  - 验证 → Task 9
- [x] **占位符扫描：** 无 TBD/TODO/模糊描述
- [x] **类型一致性：** ExecRequest/ExecResult 字段名和类型在所有 Task 中保持一致；IExecService 接口签名不变
