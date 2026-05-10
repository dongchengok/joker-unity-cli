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
├── src/                         # CLI 源码（.NET 8 项目）
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
└── Development/                 # 开发测试用 Unity 工程
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

### Commands

| 命令 | 用途 |
|------|------|
| `joker-unity info [path]` | 显示项目信息（名称、版本、包列表） |
| `joker-unity build <platform>` | 触发构建 |
| `joker-unity assets [query]` | 列出/搜索资源 |

### Models

| 模型 | 属性 |
|------|------|
| UnityProject | Path, Name, UnityVersion, PackageDependencies |
| UnityInstallation | Path, Version |
| AssetInfo | RelativePath, Guid, Extension |
| BuildResult | Success, LogPath, OutputPath, Duration |

## 关键设计决策

| 决策 | 原因 |
|------|------|
| UPM 包分发 | 安装简单，生态兼容，CI/CD 方便 |
| Spectre.Console.Cli | CLI 解析 + 终端 UI 一体化 |
| 独立终端 CLI（非 Editor 内） | AI 智能体可直接在终端调用 |
| 文件系统直读（非 Unity API） | 独立 CLI 无法引用 UnityEditor.dll |
| 临时目录测试（非 mock） | 避免 mock 与真实文件系统行为不一致 |

## 测试

- 26 个单元测试，覆盖所有 Service 和 InfoCommand
- 使用 xUnit + FluentAssertions
- 测试通过临时目录模拟文件系统，不依赖 mock 框架（除 Command 层集成测试）
