# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指导。

## 基本规则

- 所有对话和回复必须使用中文
- 必须具备独立思考能力，有疑问时主动质疑，不盲目附和
- 发现用户方案有明显问题或风险时，明确指出并说明原因
- 存在更好的方案时，主动提出并解释理由
- 不确定时坦诚表达，不装懂

## 项目概述

joker-unity-cli 是一个 Unity UPM 插件包，提供独立终端 CLI 工具（`joker-unity`），帮助 AI 智能体集成 Unity 开发流程。
- 用户通过 Unity Package Manager 添加 git URL 安装
- CLI 支持文本输出（人类）和 JSON 输出（`--json`，AI/程序调用）

## 技术栈

- **语言：** C# / .NET 8+
- **CLI 框架：** Spectre.Console.Cli
- **测试框架：** xUnit + FluentAssertions
- **Unity 交互：** 文件系统读写 + Unity.exe batch mode

## 编码规范（摘要）

- 以 Unity 官方 C# 编码规范为基础
- 命名：类 PascalCase，私有字段 _camelCase，常量 PascalCase
- 一个文件一个类型，文件名与类型名一致
- 不写显而易见的注释，只在 WHY 不明显时加注释
- 完整规范见 `docs/coding-standards.md`，编码时必须先阅读

## 架构概览（摘要）

- CLI 入口（Program.cs）→ Spectre 命令解析 → Service 层 → 文件系统/Unity.exe
- Commands 层：参数解析 + 输出格式化（文本/JSON）
- Services 层：核心业务逻辑，可独立测试
- 开发测试在 Development/ Unity 工程中进行
- 详细架构见 `docs/architecture.md`，修改代码前须先了解当前架构

## 目录结构

```
├── src/                  # CLI 源码（.NET 项目）
│   ├── Joker.UnityCli/   # 主程序（Commands, Services, Models）
│   └── Tests/            # 单元测试
├── package.json          # UPM 包清单
├── Editor/               # Unity Editor 集成
├── Runtime/              # Runtime 代码
├── Tests/                # Unity 测试
├── Tools~/               # 预编译 CLI 二进制（构建产物）
├── docs/                 # 文档
└── Development/          # 开发用 Unity 工程（不发布）
```

## 构建与测试

```bash
# 运行单元测试
cd src && dotnet test

# 运行 CLI（开发模式）
cd src/Joker.UnityCli && dotnet run -- info --project ../../Development

# 编译发布
cd src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64
```
