# compile 命令设计文档

## Context

AI 智能体在修改 Unity 项目中的 C# 文件后，需要一种可靠的方式触发 Unity 重新编译，并获取编译结果。当前 CLI 的 `exec` 命令可以执行任意代码，但没有专门的编译触发和结果检测能力。本命令提供一键触发编译 + 严格结果检测的功能。

## 需求

1. 强制触发 Unity 脚本重新编译
2. 已是最新不需要编译时返回成功
3. 编译报错返回失败（含错误列表）
4. 需要编译但无法触发时返回失败
5. 支持 Editor 已打开（TCP）和未打开（batchmode）两种场景
6. 默认超时 300 秒，可通过 `--timeout` 覆盖

## 命令接口

```
joker-unity compile [--project <PATH>] [--timeout <SECONDS>] [--json]
```

- 返回码：0 = 成功，1 = 失败
- JSON 输出示例：

```json
{"success": true, "status": "compiled", "errors": [], "durationMs": 5000}
{"success": true, "status": "up_to_date", "errors": [], "durationMs": 100}
{"success": false, "status": "failed", "errors": ["CS0234: The name 'foo' does not exist"], "durationMs": 3000}
{"success": false, "status": "timeout", "errors": [], "durationMs": 300000}
```

## 编译触发策略（按顺序执行）

### 步骤 1：AssetDatabase.Refresh(ForceSynchronousImport)

同步刷新资源数据库，确保 Unity 检测到所有文件变更。使用 `ForceSynchronousImport` 确保方法返回时刷新已完成，不会造成全量重导入。

### 步骤 2：CompilationPipeline.RequestScriptCompilation()

直接请求脚本重新编译。不依赖文件变更检测，Unity 会重新编译所有脚本。

### 步骤 3（兜底）：修改 scriptingDefineSymbols

修改 `PlayerSettings.SetScriptingDefineSymbolsForGroup`，添加一个无害的临时 define（如 `__JOKER_COMPILE_TRIGGER__`）再立即移除。改变编译条件迫使 Unity 必须重新编译。仅在步骤 1 和 2 都无法触发编译时使用。

## 两种执行路径

### 路径 A：TCP（Editor 已打开）

复用现有 `IExecService` 的 TCP 通道，通过 Roslyn 脚本执行触发编译的代码。

**触发：** 通过 TCP 发送编译触发脚本，脚本按顺序执行步骤 1、2。

**结果检测（关键设计）：**

编译成功后 Unity 会执行 Assembly Reload，TCP 服务器会断连并在新端口重启。

1. 触发后通过 TCP 轮询 `EditorApplication.isCompiling`（每 2 秒）
2. 三种结果判定：
   - **TCP 连接断开** → Assembly Reload 发生 → 等待 `server.json` 更新新端口 → **编译成功**
   - **`isCompiling` true → false，服务器仍存活** → 编译有错误 → 解析 Editor 日志获取错误列表 → **编译失败**
   - **`isCompiling` 始终 false**（超 10 秒）→ 触发失败 → 尝试步骤 3 define symbols 兜底

### 路径 B：Batchmode（Editor 未打开）

当 TCP 连接失败时自动降级。

1. 通过 `IUnityLocator` 查找 Unity 安装
2. 启动：`Unity.exe -batchmode -quit -projectPath <path> -logFile <temp_path>`
3. 等待进程退出（带超时）
4. 解析临时日志文件检测编译结果

### Editor 日志解析（两种路径共用）

解析 Unity Editor 日志中的编译错误，格式与现有 `LogService` 一致：

```
Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
```

复用 `LogService` 的正则匹配逻辑提取错误行。

## 文件结构

### 新增文件

| 文件 | 用途 |
|------|------|
| `.src/Joker.UnityCli/Commands/CompileCommand.cs` | compile 命令（参数解析 + 输出格式化） |
| `.src/Joker.UnityCli/Services/ICompileService.cs` | 编译服务接口 |
| `.src/Joker.UnityCli/Services/CompileService.cs` | 编译服务实现（TCP 优先 + batchmode 降级） |
| `.src/Joker.UnityCli/Models/CompileResult.cs` | 编译结果模型 |

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `.src/Joker.UnityCli/Program.cs` | 注册 `compile` 命令和 `ICompileService` |

### 无需修改

- `Editor/` 目录 — 编译触发通过现有 TCP exec 通道发送 Roslyn 脚本，无需新增 Editor 侧代码

## Model 定义

```csharp
public class CompileResult
{
    public bool Success { get; set; }
    public string Status { get; set; }  // "compiled" | "up_to_date" | "failed" | "timeout"
    public List<string> Errors { get; set; } = [];
    public long DurationMs { get; set; }
}
```

## 依赖复用

| 现有组件 | 复用方式 |
|----------|----------|
| `IExecService` | TCP 通信，发送编译触发脚本 |
| `IProjectDetector` | 检测项目路径 |
| `IUnityLocator` | 查找 Unity 安装路径（batchmode 路径） |
| `LogService` 日志解析 | 复用正则模式解析编译错误 |
| `GlobalCommandSettings` | `--json` 和 `--project` 参数继承 |

## 测试策略

### 单元测试（TDD）

- `CompileResult` 序列化/反序列化测试
- `CompileService` TCP 路径逻辑（mock `IExecService`）
- `CompileService` batchmode 路径逻辑（mock `IUnityLocator`）
- `CompileService` 日志解析逻辑
- `CompileCommand` 参数解析和输出格式化

### 集成测试

- 在 `.Unity2019/` 和 `.Unity2021/` Unity 工程中实际测试
- TCP 路径：Editor 打开时执行 `compile` 命令
- Batchmode 路径：Editor 关闭时执行 `compile` 命令
