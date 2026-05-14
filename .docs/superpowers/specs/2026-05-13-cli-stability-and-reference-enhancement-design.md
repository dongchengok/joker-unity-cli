# CLI 稳定性与引用处理增强设计

日期：2026-05-13

## Context

AI 智能体通过 joker-unity-cli 自动化操作 Unity Editor 时，经常遇到以下不稳定场景：

1. **exec 命令超时/无响应** — CLI 发送请求后无返回
2. **Unity 重编译后断连** — Unity 触发编译时 HTTP 服务器停止，CLI 连接失败
3. **编译错误/引用冲突** — 执行代码时遇到 CS0433 类型冲突等引用问题

根本原因：CLI 无法区分"Unity 正在编译"和"Unity 没启动"，只能盲目重试；ScriptExecutor 加载所有程序集导致类型冲突。

## 方案概述

通过扩展现有 `server.json` 增加 `status` 字段，让 CLI 能感知 Unity Editor 状态，实现智能重试和结构化错误返回。同时增强引用处理，减少类型冲突。

## 模块一：server.json 状态扩展

### 当前状态

`PortRegistry` 写入 `{ port, pid }` 到 `.joker-unity/server.json`。

### 改动

扩展为 `{ port, pid, status }`，`status` 取值：

| 值 | 含义 | 写入时机 |
|---|---|---|
| `ready` | 服务器正常运行 | `JokerServerController.Initialize()` 启动服务器后 |
| `compiling` | Unity 正在重新编译 | `beforeAssemblyReload` 回调 |
| `stopped` | 服务器已停止 | `EditorApplication.quitting` 回调 |

### 修改文件

- `Editor/ScriptServer/PortRegistry.cs` — `ServerInfo` 模型增加 `status` 字段，写入方法增加状态参数
- `Editor/JokerServerController.cs` — 在编译生命周期回调中更新状态

## 模块二：CLI 侧 status 命令

### 新增文件

- `.src/Joker.UnityCli/Commands/StatusCommand.cs` — Spectre 命令定义
- `.src/Joker.UnityCli/Services/StatusService.cs` — 状态查询服务

### 功能

- 读取 `server.json`，返回端口、PID、服务器状态
- 支持 `--json` 输出（AI 调用）和文本输出
- `ready` 状态下可选发轻量 HTTP 请求验证服务器响应

### 输出格式

文本模式：
```
Unity Editor Status: ready
Port: 63001
PID: 12345
```

JSON 模式（`--json`）：
```json
{
  "status": "ready",
  "port": 63001,
  "pid": 12345,
  "serverResponding": true
}
```

## 模块三：ExecService 智能重试改造

### 改动文件

- `.src/Joker.UnityCli/Services/ExecService.cs` — 重构重试逻辑

### 重试策略

请求前读取 `server.json` 的 `status` 字段：

| 状态 | 行为 |
|---|---|
| `compiling` | 等待 + 轮询状态文件直到 `ready`（带超时），然后正常发送请求 |
| `stopped` 或文件不存在 | 快速失败，返回 `ErrorCode: server_not_found` |
| `ready` | 正常发送请求，失败时重试 |

重试参数：
- 最大重试次数：10 次
- 指数退避：初始 1 秒，最大 5 秒
- 每次重试前重新读取 `server.json`（端口可能因重编译改变）

### CompileService 适配

- `.src/Joker.UnityCli/Services/CompileService.cs`
- 编译触发后通过 `status` 字段判断编译完成，替代纯端口变化检测

## 模块四：结构化错误码

### ExecResult 模型扩展

```csharp
public class ExecResult
{
    public string Type { get; set; }
    public string Id { get; set; }
    public bool Success { get; set; }

    // 新增字段
    public string ErrorCode { get; set; }    // 结构化错误码
    public string ErrorDetail { get; set; }  // 完整异常堆栈或编译错误详情

    public string Result { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }        // 简短错误摘要
    public long DurationMs { get; set; }
}
```

### 错误码定义

| 错误码 | 含义 | AI 应对策略 |
|---|---|---|
| `server_not_found` | Unity 编辑器未运行或未安装包 | 提示用户启动 Unity |
| `compiling` | Unity 正在编译，请求被拒绝 | 等待后重试 |
| `timeout` | 执行超时 | 检查是否有死循环 |
| `reference_conflict` | 类型引用冲突（CS0433） | 简化代码，减少依赖 |
| `compilation_error` | 代码编译错误 | 修复代码 |
| `execution_error` | 运行时异常 | 检查逻辑 |

### 修改文件

- `.src/Joker.UnityCli/Models/ExecResult.cs` — 增加 `ErrorCode`、`ErrorDetail` 字段
- `Editor/ScriptServer/HttpExecHandler.cs` — 在错误响应中填充 `ErrorCode` 和 `ErrorDetail`
- `Editor/ScriptExecution/ScriptExecutor.cs` — 执行结果中包含完整错误信息

## 模块五：引用处理增强

### 改动文件

- `Editor/ScriptExecution/ScriptExecutor.cs`

### 改动内容

1. **程序集优先级过滤**：
   - `UnityEngine.CoreModule` 优先于 `UnityEngine`（旧 facade DLL）
   - 建立已知冲突程序集黑名单
   - 过滤 Unity 旧版本中已合并到 CoreModule 的模块

2. **提前应用 CompilationErrorAnalyzer**：
   - 第 1 次编译失败时立即应用修复，而不是等第 2 次重试
   - 重试次数保持 3 次，但每次都应用分析器修复

3. **HttpExecHandler 编译中早期拒绝**：
   - `Editor/ScriptServer/HttpExecHandler.cs`
   - 检查服务器状态，编译中直接返回 `{ errorCode: "compiling" }`

## 分阶段实施计划

### 阶段 1：CLI 稳定性（基础）

1. 扩展 `PortRegistry` 和 `ServerInfo` 模型支持 `status` 字段
2. 改造 `JokerServerController` 编译生命周期状态更新
3. 新增 `StatusCommand` / `StatusService`
4. 改造 `ExecService` 智能重试逻辑
5. `ExecResult` 增加 `ErrorCode` 和 `ErrorDetail`
6. 编写对应单元测试

### 阶段 2：执行场景增强

1. 改造 `HttpExecHandler` 编译中早期拒绝
2. 增强 `ScriptExecutor` 引用处理（优先级过滤、冲突排除）
3. `CompileService` 适配状态文件

### 阶段 3：测试完善

1. 审查现有测试用例
2. 补充集成测试覆盖各种场景
3. 跑通全部测试

### 阶段 4：文档整理

1. 更新 architecture.md
2. 更新 CLAUDE.md 架构描述
3. 更新 README.md（反映新增 status 命令、错误码体系等变更）
4. 清理功能重叠的历史 plan/spec 文件（如 2026-05-12 的集成测试文档，已完成的部分合并归档）

## 验证方式

1. **单元测试**：`cd .src && dotnet test` 全部通过
2. **手动验证**：
   - 启动 Unity Editor，运行 `joker-unity status --project <path> --json` 确认返回 `ready`
   - 执行 `joker-unity exec "1+1" --project <path> --json` 确认正常返回
   - 在 Unity 中触发编译（修改脚本文件），同时执行 exec 命令，确认返回 `compiling` 错误码并自动等待重试
   - 执行包含冲突引用的代码，确认返回 `reference_conflict` 错误码
3. **AI 智能体验证**：通过 `--json` 模式确认所有错误码和堆栈信息正确返回
