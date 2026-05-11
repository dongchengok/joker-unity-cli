# CLI 基础框架设计

## 背景

joker-unity-cli 是一个 Unity UPM 插件包，提供独立终端 CLI 工具，帮助 AI 智能体集成 Unity 开发流程。本文档定义 CLI 基础框架的架构和模块设计。

## 决策记录

| 决策项 | 结论 | 理由 |
|--------|------|------|
| 运行方式 | 独立终端 CLI | 用户在终端直接运行，不依赖 Unity Editor |
| 分发方式 | 预编译二进制嵌入 Tools~/ | 装包即用，无需 .NET SDK |
| CLI 框架 | Spectre.Console.Cli | 解析 + 终端 UI 一体 |
| 测试框架 | xUnit | .NET 生态主流 |
| Unity 交互 | 文件系统读写 + batch mode 调用 | 独立 CLI 无法引用 UnityEditor.dll |

## 目录结构

```
仓库根/                              # = UPM 包根
├── package.json                     # UPM 包清单
├── src/                             # CLI 源码（.NET 项目）
│   ├── Joker.UnityCli.sln
│   ├── Joker.UnityCli/
│   │   ├── Joker.UnityCli.csproj
│   │   ├── Program.cs               # CLI 入口
│   │   ├── Commands/                # Spectre 命令定义
│   │   │   ├── InfoCommand.cs
│   │   │   ├── BuildCommand.cs
│   │   │   └── AssetsCommand.cs
│   │   └── Services/                # 业务逻辑
│   │       ├── ProjectDetector.cs
│   │       ├── BuildService.cs
│   │       ├── AssetService.cs
│   │       └── UnityLocator.cs
│   └── Joker.UnityCli.Tests/
│       ├── Joker.UnityCli.Tests.csproj
│       ├── Services/
│       │   ├── ProjectDetectorTests.cs
│       │   ├── BuildServiceTests.cs
│       │   ├── AssetServiceTests.cs
│       │   └── UnityLocatorTests.cs
│       └── Commands/
├── Editor/                          # Unity Editor 集成（后续扩展）
├── Runtime/
├── Tests/
├── Tools~/                          # 预编译 CLI 二进制（构建产物）
├── docs/
├── .Unity2019/                     # 测试用 Unity 2019.4 工程（最低兼容版本）
├── .Unity2021/                     # 测试用 Unity 2021.3 工程
```

## 架构

### 三层结构

```
用户终端 → joker-unity <command>
              ↓
         Commands 层（Spectre 命令定义）
              ↓
         Services 层（核心业务逻辑）
              ↓
         Unity 交互层（文件系统 + Unity.exe）
```

- **Commands/** — Spectre 命令定义，只做参数解析和调用 Service
- **Services/** — 核心业务逻辑，可独立测试，不依赖 Spectre
- **Unity 交互** — 文件系统读写（解析项目文件）+ Unity.exe batch mode 调用

### 数据流

```
CLI 启动
  → Spectre 解析命令和参数
  → Command 调用对应 Service
  → Service 操作文件系统或调用 Unity.exe
  → 结果通过 Spectre 格式化输出到终端
```

## 核心模块

### 1. UnityLocator（Unity 安装检测）

**职责：** 查找本机 Unity 安装路径

**检测顺序：**
1. `--unity` 命令行参数
2. 配置文件（`~/.joker-unity/config.json`）
3. 环境变量 `UNITY_HOME`
4. 自动扫描默认安装路径：
   - Windows: `C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe`
   - macOS: `/Applications/Unity/Hub/Editor/*/Unity.app`
   - Linux: `~/Unity/Hub/Editor/*/Editor/Unity`

**输出：** UnityInstallation 对象（路径、版本号）

### 2. ProjectDetector（项目检测/解析）

**职责：** 检测路径是否为 Unity 项目，解析项目信息

**输入：** 文件系统路径（默认从 CLI 所在位置向上遍历查找）

**输出：** UnityProject 对象
- 项目根路径
- Unity 版本（解析 ProjectSettings/ProjectVersion.txt）
- 包依赖列表（解析 Packages/manifest.json）
- 项目名称

**判断逻辑：** 路径下同时存在 `Assets/` 和 `ProjectSettings/` 目录即为有效 Unity 项目

### 3. BuildService（构建触发）

**职责：** 通过 Unity batch mode 执行构建

**输入：** UnityProject + UnityInstallation + 构建参数
- 目标平台（Windows/macOS/Linux/Android/iOS/WebGL）
- 场景列表（可选，默认全部）
- 输出路径
- 额外参数

**输出：** BuildResult 对象
- 成功/失败
- 构建日志路径
- 输出产物路径
- 耗时

**实现：** 拼接命令行参数，通过 Process 启动 Unity.exe：
```
Unity.exe -batchmode -quit -projectPath <path> -executeMethod <method> -buildTarget <platform>
```

### 4. AssetService（资源管理）

**职责：** 列出/搜索/查询项目中的资源

**输入：** UnityProject + 查询条件

**输出：** 资源列表
- 资源路径
- 资源类型（根据扩展名或 meta 文件的 guid 判断）
- GUID（从 .meta 文件解析）

**实现：** 扫描 Assets/ 目录，解析 .meta 文件获取 GUID

## CLI 命令

```
joker-unity                              显示帮助
joker-unity info [path]                  显示项目信息
joker-unity build <platform> [options]   触发构建
joker-unity assets list [path]           列出资源
joker-unity assets search <query>        搜索资源
```

### 全局选项

```
--project, -p    Unity 项目路径（默认自动检测）
--unity, -u      Unity 安装路径（默认自动检测）
--verbose, -v    详细输出
```

### build 命令选项

```
--output, -o     输出路径
--scenes, -s     场景列表（逗号分隔）
--clean          构建前清理
```

## 测试策略

- Services 层：完整的单元测试（xUnit），使用临时目录模拟文件系统
- Commands 层：集成测试，验证参数解析和 Service 调用
- 不 mock 文件系统——使用真实的临时目录，避免 mock 与真实行为不一致

## 后续扩展（不在本次范围内）

- `joker-unity package list/install/remove` — 包管理
- `joker-unity scene list/open` — 场景管理
- Unity Editor 内集成（菜单项调用 CLI）
- CI/CD 自动构建和发布流程
