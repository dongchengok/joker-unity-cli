# 架构说明

## 系统概述

joker-unity-cli 是一个 Unity UPM 插件包，提供独立终端 CLI 工具，帮助 AI 智能体集成 Unity 开发流程。

## 核心能力

1. **项目检测/解析** — 自动检测 Unity 项目，解析版本和包依赖
2. **构建触发** — 通过 Unity batch mode 执行构建
3. **资源管理** — 列出、搜索项目中的资源

## 目录结构

```
仓库根/                          # = UPM 包根
├── package.json                 # UPM 包清单
├── .src/                        # CLI 源码（.NET 8 项目，. 前缀避免 Unity 扫描）
│   ├── Joker.UnityCli/          # 主程序
│   │   ├── Commands/            # Spectre CLI 命令
│   │   ├── Services/            # 业务逻辑
│   │   └── Models/              # 数据模型
│   └── Joker.UnityCli.Tests/    # 单元测试
├── Editor/                      # Unity Editor 集成（后续扩展）
├── Runtime/                     # Runtime 代码（后续扩展）
├── Tests/                       # Unity 测试（后续扩展）
├── Tools~/                      # 预编译 CLI 二进制（构建产物）
├── docs/                        # 文档
├── .Unity2019/                  # 测试用 Unity 2019.4 工程（最低兼容版本）
├── .Unity2021/                  # 测试用 Unity 2021.3 工程
```

## 三层架构

```
终端 → Commands 层（参数解析）→ Services 层（业务逻辑）→ 文件系统 / Unity.exe
```

- **Commands/** — Spectre 命令定义，只做参数解析和调用 Service
- **Services/** — 核心业务逻辑，可独立测试，不依赖 Spectre
- **Unity 交互** — 文件系统读写 + Unity.exe batch mode 调用

## 模块划分

### Services

| 服务 | 职责 | 关键文件 |
|------|------|---------|
| ProjectDetector | 检测 Unity 项目，解析版本和包依赖 | ProjectSettings/ProjectVersion.txt, Packages/manifest.json |
| UnityLocator | 查找本机 Unity 安装路径 | Hub/Editor/ 目录扫描 |
| AssetService | 列出/搜索 Assets 目录下的资源 | .meta 文件解析 GUID |
| BuildService | 拼接 Unity batch mode 参数并执行 | Process 调用 Unity.exe |
| ExecService | 通过 HTTP 执行 C# 代码，状态感知智能重试 | .joker-unity/server.json |
| CompileService | 触发脚本编译，TCP 优先 + batchmode 回退 | .joker-unity/server.json |
| StatusService | 查询 Unity Editor 服务器状态 | .joker-unity/server.json |

### Commands

| 命令 | 用途 |
|------|------|
| `joker-unity info [path]` | 显示项目信息（名称、版本、包列表） |
| `joker-unity build <platform>` | 触发构建 |
| `joker-unity assets [query]` | 列出/搜索资源 |
| `joker-unity exec <code>` | 在 Unity Editor 中执行 C# 代码 |
| `joker-unity compile` | 触发脚本重编译 |
| `joker-unity status` | 查询 Unity Editor 服务器状态 |
| `joker-unity logs` | 显示 Unity Editor 日志 |

### Models

| 模型 | 属性 |
|------|------|
| UnityProject | Path, Name, UnityVersion, PackageDependencies |
| UnityInstallation | Path, Version |
| AssetInfo | RelativePath, Guid, Extension |
| BuildResult | Success, LogPath, OutputPath, Duration |
| ExecResult | Success, ErrorCode, Error, ErrorDetail, Result, Output, DurationMs |
| ServerStatus | Status, Port, Pid, ServerResponding |
| CompileResult | Success, Status, Errors, DurationMs |

## 关键设计决策

| 决策 | 原因 |
|------|------|
| UPM 包分发 | 安装简单，生态兼容，CI/CD 方便 |
| Spectre.Console.Cli | CLI 解析 + 终端 UI 一体化 |
| 独立终端 CLI（非 Editor 内） | AI 智能体可直接在终端调用 |
| 文件系统直读（非 Unity API） | 独立 CLI 无法引用 UnityEditor.dll |
| 临时目录测试（非 mock） | 避免 mock 与真实文件系统行为不一致 |
| server.json 状态信号（ready/compiling/stopped） | CLI 能区分"Unity 编译中"和"未启动"，实现智能重试 |
| ExecResult 结构化错误码（ErrorCode + ErrorDetail） | AI 智能体可程序化处理错误，而非解析自由文本 |
| ScriptExecutor 引用过滤（排除 facade DLL） | 避免 CS0433 类型冲突，提高脚本执行成功率 |

## 测试

- 199+ 个单元测试，覆盖所有 Service 和 Command
- 44 个集成测试（需运行中 Unity Editor，本地开发时跳过）
- 使用 xUnit + FluentAssertions
- 测试通过临时目录模拟文件系统，不依赖 mock 框架（除 CompileService/Command 测试）

## 状态感知通信机制

CLI 与 Unity Editor 的通信通过 `server.json` 文件进行状态同步：

```
Unity Editor                          CLI (ExecService)
    │                                      │
    ├── [启动] Write(port, "ready")        │
    │                                      ├── ReadServerInfo() → status="ready"
    │                                      ├── POST /exec → 正常请求
    │                                      │
    ├── [编译] WriteStatus("compiling")    │
    │   └── HttpExecHandler.IsCompiling    ├── ReadServerInfo() → status="compiling"
    │       └── 返回 503 + ErrorCode       ├── 轮询等待 status 变化
    │                                      │
    ├── [域重载] 静态变量重置               │
    │   └── _isCompiling → false           │
    │   └── Write(port, "ready")           │
    │                                      ├── status="ready" → 继续请求
    │                                      │
    ├── [退出] WriteStatus("stopped")      │
    │                                      ├── ReadServerInfo() → status="stopped"
    │                                      └── 快速失败: ErrorCode="server_not_found"
```

### 错误码定义

| 错误码 | 含义 | AI 应对策略 |
|--------|------|------------|
| `server_not_found` | Unity 编辑器未运行 | 提示用户启动 Unity |
| `compiling` | Unity 正在编译 | 等待后重试 |
| `timeout` | 执行超时 | 检查是否有死循环 |
| `compilation_error` | 代码编译错误 | 修复代码 |
| `execution_error` | 运行时异常 | 检查逻辑 |
| `reference_conflict` | 类型引用冲突 | 简化代码 |
| `max_retries_exceeded` | 超过最大重试次数 | 检查 Unity 状态 |
