# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指导。

## 基本规则

- 所有对话和回复必须使用中文
- 必须具备独立思考能力，有疑问时主动质疑，不盲目附和
- 发现用户方案有明显问题或风险时，明确指出并说明原因
- 存在更好的方案时，主动提出并解释理由
- 不确定时坦诚表达，不装懂

## 开发流程（红线要求）

所有功能开发、Bug 修复、架构变更的计划和实施，**必须**使用以下 Superpowers 技能，不得跳过：

1. **`superpowers:brainstorming`** — 协作式需求分析和方案设计，任何新功能或变更必须先经过 brainstorming 流程
2. **`superpowers:writing-plans`** — 编写详细实施计划，计划文件保存到 `.docs/superpowers/plans/` 目录
3. **`superpowers:subagent-driven-development`** — 通过子代理分任务执行计划，每个任务完成后进行规范合规审查和代码质量审查
4. **`superpowers:test-driven-development`** — 严格 TDD 流程（红-绿-重构），先写失败测试，再写最小实现

**不得跳过的原因：** 这些技能确保了设计充分讨论、计划完整可执行、代码质量有保障、测试覆盖到位。跳过任何环节都会导致返工和质量下降。

## 项目概述

joker-unity-cli 是一个 Unity UPM 插件包，提供独立终端 CLI 工具（`joker-unity`），帮助 AI 智能体集成 Unity 开发流程。
- 用户通过 Unity Package Manager 添加 git URL 安装
- CLI 支持文本输出（人类）和 JSON 输出（`--json`，AI/程序调用）

## 技术栈

- **语言：** C# / .NET 8+
- **CLI 框架：** Spectre.Console.Cli
- **测试框架：** xUnit + FluentAssertions
- **Unity 交互：** 文件系统读写 + Unity.exe batch mode + HTTP 脚本执行（Roslyn Scripting 3.8.0）

## 编码规范（摘要）

- 以 Unity 官方 C# 编码规范为基础
- 命名：类 PascalCase，私有字段 _camelCase，常量 PascalCase
- 一个文件一个类型，文件名与类型名一致
- 不写显而易见的注释，只在 WHY 不明显时加注释
- 完整规范见 `.docs/coding-standards.md`，编码时必须先阅读

## 架构概览（摘要）

- CLI 入口（Program.cs）→ Spectre 命令解析 → Service 层 → 文件系统/Unity.exe/TCP
- Commands 层：参数解析 + 输出格式化（文本/JSON）
- Services 层：核心业务逻辑，可独立测试
- Unity Editor 侧：TCP 服务器（`[InitializeOnLoad]` 自启动）+ Roslyn 脚本执行
- exec 命令通过 TCP 连接 Unity Editor 内置服务器，支持 script（语句级）和 compile（完整文件）两种模式
- status 命令查询 Unity Editor 服务器状态（ready/compiling/stopped），支持 `--json` 输出
- server.json（`.joker-unity/server.json`）记录端口、PID 和状态（ready/compiling/stopped）
- ExecResult 包含结构化错误码（ErrorCode）和异常堆栈（ErrorDetail），供 AI 智能体程序化处理
- ExecService 具备状态感知智能重试：编译中等待、服务器不存在快速失败、最大 10 次重试
- 开发测试在 `.Unity2019/`（2019.4）和 `.Unity2021/`（2021.3）两个 Unity 工程中进行
- 详细架构见 `.docs/architecture.md`，修改代码前须先了解当前架构

## 目录结构

```
├── .src/                  # CLI 源码（.NET 项目，. 前缀避免 Unity 扫描）
│   ├── Joker.UnityCli/   # 主程序（Commands, Services, Models）
│   └── Tests/            # 单元测试
├── .docs/                 # 项目文档
├── .Unity2019/            # 测试用 Unity 2019.4 工程（最低兼容版本）
├── .Unity2021/            # 测试用 Unity 2021.3 工程
├── package.json           # UPM 包清单
├── Editor/                # Unity Editor 集成
│   ├── Models/            # ExecRequest, ExecResult（含 ErrorCode, ErrorDetail）
│   ├── ScriptServer/      # HTTP 服务器 + 端口注册 + 编译状态信号
│   ├── ScriptExecution/   # Roslyn 脚本执行器（含引用过滤和错误分析）
│   ├── Plugins/Roslyn/    # Roslyn 3.8.0 DLL（Editor-only）
│   └── JokerServerController.cs  # [InitializeOnLoad] 自启动 + 编译生命周期管理
├── Runtime/               # Runtime 代码
├── Tests/                 # Unity 测试
└── Tools~/                # 预编译 CLI 二进制（构建产物）
```

注意：`.` 前缀目录会被 Unity 资产管线忽略，这是 UPM 包开发的标准约定。

## 构建与测试

```bash
# 运行单元测试
cd .src && dotnet test

# 运行 CLI（开发模式）
cd .src/Joker.UnityCli && dotnet run -- info --project ../../.Unity2019

# 编译发布
cd .src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64

# 执行代码（需 Unity Editor 打开项目）
Tools~/win-x64/joker-unity exec "1+1" --project ../../.Unity2019 --json
```
