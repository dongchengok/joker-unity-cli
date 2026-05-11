# HTTP Server 迁移设计

## Context

当前 Unity Editor 侧使用自定义 TCP 服务器（TcpListener + 行分隔 JSON 协议）处理 CLI 的代码执行请求。该实现是 one-shot 请求-响应模式：每次 CLI 调用建立 TCP 连接，发送 JSON 请求，接收 JSON 响应后关闭。

**问题：**
- TCP 上跑请求-响应协议，语义上就是 HTTP，但缺少 HTTP 的标准化好处
- 自定义协议需要手动维护分帧、编码、错误语义
- 无法用 curl/Postman 等标准工具调试
- 不利于未来第三方集成

**目标：** 将 TCP Server 完全替换为 HTTP Server，提升架构合理性和可扩展性。

## 约束

- **最低兼容 Unity 2019.4**（.NET Framework 4.x），使用 `System.Net.HttpListener`
- **完全替换**，不保留 TCP 向后兼容
- 只绑定 loopback（127.0.0.1），不受 Windows 管理员权限限制

## 方案

使用 `System.Net.HttpListener`（Unity Editor 侧）和 `System.Net.Http.HttpClient`（CLI 侧）。

**JSON 序列化方案：**
- **Unity Editor 侧：** 使用 `Newtonsoft.Json`（通过 `com.unity.nuget.newtonsoft-json` 2.x UPM 包）。替代当前脆弱的手动 JSON 解析，正确处理 code 字段中的引号、换行、Unicode 转义等边界情况。
- **CLI 侧：** 使用 `System.Text.Json`（.NET 8 内置）。

## HTTP 协议设计

### 端点

```
POST http://127.0.0.1:{port}/exec
Content-Type: application/json
```

### 请求体

与现有 ExecRequest 完全一致：

```json
{
  "type": "exec",
  "id": "abc12345",
  "code": "1+1",
  "mode": "script",
  "timeout": 30000
}
```

### 响应

**成功和业务失败（200）：**

```json
{
  "type": "exec_result",
  "id": "abc12345",
  "success": true,
  "result": "2",
  "output": "",
  "error": "",
  "durationMs": 15
}
```

脚本执行失败仍返回 200，`success: false`，错误信息在 `error` 字段。

**HTTP 错误：**
- `400 Bad Request` — JSON 解析失败或必填字段缺失
- `404 Not Found` — 访问非 `/exec` 路径
- `405 Method Not Allowed` — 使用非 POST 方法
- `500 Internal Server Error` — 服务器内部异常

### 超时和重试

- `HttpClient.Timeout` 设为 `request.Timeout`
- 连接失败时，在剩余超时时间内用指数退避重试（初始 1s，最大 5s）
- 覆盖 Unity Domain Reload 短暂不可用的窗口
- 脚本执行超时仍由 Unity 侧 CancellationToken 控制

## 架构变更

### 通信模型

```
之前: CLI --[TCP]--> TcpListener --> ScriptServerSession --> ScriptExecutor
之后: CLI --[HTTP POST /exec]--> HttpListener --> HttpExecHandler --> ScriptExecutor
```

### 文件变更

**删除：**
- `Editor/ScriptServer/ScriptServer.cs`
- `Editor/ScriptServer/ScriptServerSession.cs`

**新增：**
- `Editor/ScriptServer/HttpServer.cs` — HttpListener 服务器
- `Editor/ScriptServer/HttpExecHandler.cs` — HTTP 请求处理（解析请求、调度执行、序列化响应）

**修改：**
- `package.json` — 添加 `com.unity.nuget.newtonsoft-json` 2.x 依赖
- `Editor/ScriptServerBootstrap.cs` — 引用改为 `HttpServer.Start/Stop`
- `.src/Joker.UnityCli/Services/ExecService.cs` — TcpClient → HttpClient，加入连接重试
- `.src/Tests/Joker.UnityCli.Tests/Services/ExecServiceTests.cs` — 测试适配 HTTP

**不变：**
- `Editor/ScriptServer/PortRegistry.cs` — 端口注册机制不变
- `Editor/ScriptExecution/ScriptExecutor.cs` — 脚本执行逻辑不变
- `Editor/ScriptExecution/ScriptGlobals.cs`
- `Editor/Models/ExecRequest.cs` / `ExecResult.cs`
- CLI 侧 Commands、其他 Models、其他 Services

## 组件设计

### HttpServer.cs（替代 ScriptServer.cs）

职责：
- 在 loopback 上启动 HttpListener，使用随机端口
- 后台 Task 循环接收请求
- 将请求分发给 HttpExecHandler
- 将端口写入 PortRegistry
- 支持 CancellationToken 优雅停止

关键实现：
- `HttpListener` 绑定 `http://127.0.0.1:0/`（随机端口）
- 注意：HttpListener 不支持 port 0 自动分配，需要手动找可用端口
- 后台 Task 运行 `GetContextAsync()` 循环
- 每个请求在一个独立 Task 中处理（不阻塞监听循环）

**端口分配策略：** HttpListener 不支持 port 0 自动分配。从 63000-63099 范围内随机选取端口尝试绑定，失败则换一个重试，最多 10 次。找到可用端口后写入 PortRegistry。

### HttpExecHandler.cs（替代 ScriptServerSession.cs）

职责：
- 读取 HTTP 请求 body，解析 JSON 为 ExecRequest
- 通过 `EditorApplication.delayCall` 调度到 Unity 主线程执行
- 将 ExecResult 序列化为 JSON 写入 HTTP 响应
- 处理 HTTP 错误（400/404/405/500）

关键实现：
- 使用 `Newtonsoft.Json.JsonConvert` 反序列化请求和序列化响应
- 复用 `ScriptExecutor.ExecuteAsync` 执行脚本
- 使用 TaskCompletionSource 等待主线程执行结果

### ExecService.cs（CLI 侧改造）

职责：
- 从 server.json 读取端口
- 使用 HttpClient 发送 POST 请求
- 连接失败时在超时窗口内重试
- 解析 JSON 响应为 ExecResult

关键实现：
- `HttpClient.Timeout` = `request.Timeout`
- 连接失败重试：指数退避（1s, 2s, 4s, 5s, 5s...），在超时窗口内
- 使用 `System.Text.Json` 反序列化响应（CLI 侧是 .NET 8，有这个库）

## 鲁棒性

### Domain Reload 场景

- **Compile 命令：** 不受影响，仍通过轮询 server.json 端口变化检测 Domain Reload 完成
- **Exec 命令：** 连接失败时在超时窗口内重试，覆盖 Domain Reload 短暂不可用的窗口

### 错误处理

- HTTP 标准错误码提供清晰的错误语义
- CLI 侧根据 HttpRequestException 区分 "连接失败"（服务器不可用）和 "请求失败"（业务错误）
- 超时统一由 HttpClient.Timeout 控制

## 验证方案

1. **单元测试（TDD）：**
   - HttpServer 端口分配和启停
   - HttpExecHandler JSON 解析和错误处理
   - ExecService HTTP 客户端和重试逻辑

2. **集成测试（手动）：**
   - 在 Unity Editor 中启动 HttpServer
   - 用 `curl -X POST http://127.0.0.1:{port}/exec -d '{"type":"exec","id":"test","code":"1+1","mode":"script","timeout":30000}'` 测试
   - 用 `joker-unity exec "1+1" --project ../../.Unity2019 --json` 测试
   - 在 Unity 编译期间执行 CLI 命令，验证重试行为

3. **回归测试：**
   - Compile 命令仍能通过端口变化检测编译完成
   - 所有现有测试通过
