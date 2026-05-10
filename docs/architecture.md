# 架构说明

## 系统概述

joker-unity-cli 是一个 Unity UPM 插件包，提供 CLI 功能帮助 AI 智能体集成 Unity 开发流程。

## 核心能力

1. **编排 Unity 命令** — 通过 Unity Editor batch mode 执行构建、打包等
2. **操作 Unity 资源** — 直接引用 Unity 程序集，解析/生成/修改资源

## 模块划分（待实现后更新）

| 目录 | 职责 | 依赖 |
|------|------|------|
| Editor/ | Editor 工具 + CLI 入口 + 命令定义 | UnityEditor, Spectre.Console.Cli |
| Runtime/ | 运行时可用组件 | UnityEngine |
| Tests/Editor/ | Editor 测试 | xUnit, NSubstitute |
| Tests/Runtime/ | Runtime 测试 | xUnit |

## 关键设计决策

| 决策 | 原因 |
|------|------|
| UPM 包分发 | 安装简单，生态兼容，CI/CD 方便 |
| Spectre.Console.Cli | CLI 解析 + 终端 UI 一体化 |
| 直接引用 Unity 程序集 | 避免桥接层复杂度 |

（本文档随项目开发持续更新）
