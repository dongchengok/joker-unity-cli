# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指导。

## 基本规则

- 所有对话和回复必须使用中文
- 必须具备独立思考能力，有疑问时主动质疑，不盲目附和
- 发现用户方案有明显问题或风险时，明确指出并说明原因
- 存在更好的方案时，主动提出并解释理由
- 不确定时坦诚表达，不装懂

## 项目概述

joker-unity-cli 是一个 Unity UPM 插件包，安装后提供 CLI 功能，帮助 AI 智能体集成 Unity 开发流程。
- 用户通过 Unity Package Manager 添加 git URL 安装
- 插件提供 Editor 扩展 + 命令行工具

## 技术栈

- **语言：** C# / .NET 8+（与 Unity 兼容的 C# 版本）
- **CLI 框架：** Spectre.Console.Cli
- **测试框架：** xUnit（Tests/Editor/ 目录）
- **Unity 交互：** UnityEditor batch mode + 直接程序集引用

## 编码规范（摘要）

- 以 Unity 官方 C# 编码规范为基础
- 命名：类 PascalCase，私有字段 _camelCase，常量 PascalCase
- 一个文件一个类型，文件名与类型名一致
- 不写显而易见的注释，只在 WHY 不明显时加注释
- 完整规范见 `docs/coding-standards.md`，编码时必须先阅读

## 架构概览（摘要）

- UPM 包结构：Editor/（Editor + CLI 代码）、Runtime/（Runtime 代码）、Tests/
- CLI 入口 → Spectre 命令解析 → 功能模块 → Unity 交互层
- 开发测试在 Development/ Unity 工程中进行
- 详细架构见 `docs/architecture.md`，修改代码前须先了解当前架构

## 目录结构

```
├── package.json         # UPM 包清单
├── Editor/              # Editor + CLI 代码
├── Runtime/             # Runtime 代码
├── Tests/               # 测试
├── docs/                # 文档
└── Development/         # 开发用 Unity 工程（不发布）
```

## 构建与测试

- 在 Development/ Unity 工程中测试 Editor 功能
- 单元测试：在 Unity Test Runner 或 `dotnet test` 中运行
- （待脚手架搭建后补充具体命令）
