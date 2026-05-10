# joker-unity

Unity CLI 工具，帮助 AI 智能体集成 Unity 开发流程。

## 安装

在 Unity Package Manager 中添加 git URL：

```
https://github.com/dongchengok/joker-unity-cli.git
```

## 命令参考

### `joker-unity info`

显示 Unity 项目信息。

```
joker-unity info [选项]
```

**选项：**

| 选项 | 说明 |
|------|------|
| `-p, --project <PATH>` | Unity 项目路径（默认自动检测） |
| `--json` | 以 JSON 格式输出（适合 AI/程序调用） |

**文本输出示例：**

```
┌──────────────┬──────────────────────────────────┐
│ Property     │ Value                            │
├──────────────┼──────────────────────────────────┤
│ Name         │ MyGame                           │
│ Path         │ C:\Projects\MyGame               │
│ Unity Version│ 2022.3.20f1                      │
└──────────────┴──────────────────────────────────┘
Packages: com.unity.render-pipelines.universal, com.unity.modules.ai, ...
```

**JSON 输出示例（`--json`）：**

```json
{
  "path": "C:\\Projects\\MyGame",
  "name": "MyGame",
  "unityVersion": "2022.3.20f1",
  "packageDependencies": [
    "com.unity.render-pipelines.universal",
    "com.unity.modules.ai"
  ]
}
```

### `joker-unity assets`

列出或搜索项目资源。

```
joker-unity assets [查询] [选项]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `[QUERY]` | 搜索关键词（可选，不填则列出全部） |

**选项：**

| 选项 | 说明 |
|------|------|
| `-p, --project <PATH>` | Unity 项目路径 |
| `--json` | 以 JSON 格式输出 |

**示例：**

```bash
# 列出所有资源
joker-unity assets --project /path/to/project

# 搜索包含 "Player" 的资源
joker-unity assets Player --project /path/to/project

# JSON 输出（AI 调用推荐）
joker-unity assets --project /path/to/project --json
```

**JSON 输出示例（`--json`）：**

```json
[
  {
    "relativePath": "Scripts/Player.cs",
    "guid": "a1b2c3d4e5f67890",
    "extension": ".cs"
  },
  {
    "relativePath": "Scenes/Main.unity",
    "guid": "b2c3d4e5f6789012",
    "extension": ".unity"
  }
]
```

### `joker-unity build`

触发 Unity 构建。

```
joker-unity build <PLATFORM> [选项]
```

**参数：**

| 参数 | 说明 |
|------|------|
| `<PLATFORM>` | 目标平台：Win64, OSXUniversal, Linux64, Android, iOS, WebGL |

**选项：**

| 选项 | 说明 |
|------|------|
| `-p, --project <PATH>` | Unity 项目路径 |
| `-u, --unity <PATH>` | Unity 安装路径（默认自动检测） |
| `-o, --output <PATH>` | 输出路径 |
| `-s, --scenes <SCENES>` | 场景列表（逗号分隔） |
| `--json` | 以 JSON 格式输出 |

**示例：**

```bash
joker-unity build Win64 --project /path/to/project --output /path/to/build
```

**JSON 输出示例（`--json`）：**

```json
{
  "success": true,
  "logPath": "/tmp/unity-build-abc.log",
  "outputPath": "/path/to/build",
  "duration": "00:02:35"
}
```

### `joker-unity exec`

在 Unity Editor 中执行 C# 代码。

```
joker-unity exec <CODE> [选项]
joker-unity exec --file <PATH> [选项]
```

**前提条件：** Unity Editor 必须打开目标项目（CLI 自动通过 TCP 连接到 Editor 内置的脚本服务器）。

**参数：**

| 参数 | 说明 |
|------|------|
| `<CODE>` | 内联 C# 代码（与 `--file` 二选一） |

**选项：**

| 选项 | 说明 |
|------|------|
| `-f, --file <PATH>` | 从文件读取完整 C# 代码 |
| `-p, --project <PATH>` | Unity 项目路径 |
| `-t, --timeout <MS>` | 执行超时（默认 30000ms） |
| `--json` | 以 JSON 格式输出 |

**执行模式：**
- 内联代码（`<CODE>`）：使用 Roslyn Scripting 快速执行语句级代码
- 文件代码（`--file`）：使用 Roslyn Compilation 编译完整 .cs 文件，需包含 `public static void Execute()` 入口方法

**示例：**

```bash
# 简单表达式
joker-unity exec "1+1" --project /path/to/project --json

# 调用 Unity API
joker-unity exec "UnityEngine.Application.unityVersion" --project /path/to/project --json

# 从文件执行
joker-unity exec --file /path/to/script.cs --project /path/to/project
```

**JSON 输出示例（`--json`）：**

```json
{
  "type": "exec_result",
  "id": "a1b2c3d4",
  "success": true,
  "result": "2",
  "output": null,
  "error": null,
  "durationMs": 42
}
```

**错误输出示例：**

```json
{
  "type": "exec_result",
  "id": "e5f6g7h8",
  "success": false,
  "result": null,
  "output": null,
  "error": "(1,20): error CS0103: The name 'foo' does not exist in the current context",
  "durationMs": 0
}
```

### `joker-unity logs`

显示 Unity Editor 日志条目（编译错误和警告）。

```
joker-unity logs [选项]
```

**选项：**

| 选项 | 说明 |
|------|------|
| `--errors` | 只显示编译错误 |
| `--tail <N>` | 显示最近 N 条（默认 50） |
| `-p, --project <PATH>` | Unity 项目路径（不指定时自动检测） |
| `--json` | 以 JSON 格式输出 |

**示例：**

```bash
# 显示所有日志条目
joker-unity logs

# 只显示编译错误
joker-unity logs --errors

# 限制 10 条
joker-unity logs --tail 10 --json

# 指定项目路径
joker-unity logs --project /path/to/project --json
```

**JSON 输出示例（`--json`）：**

```json
[
  {
    "filePath": "Assets/Scripts/Player.cs",
    "line": 10,
    "column": 5,
    "severity": "error",
    "code": "CS0234",
    "message": "The name 'foo' does not exist in the current context"
  }
]
```

## 退出码

| 退出码 | 说明 |
|--------|------|
| 0 | 成功 |
| 1 | 失败（项目未找到、Unity 未找到、构建失败等） |

## AI 集成指南

使用 `--json` 标志获取结构化输出：

- 成功时：stdout 输出合法 JSON，stderr 无输出
- 失败时：stderr 输出 `{"error": "错误描述"}`，退出码为 1
- JSON 字段名使用 camelCase 命名

## 开发

```bash
# 运行测试
cd .src && dotnet test

# 运行 CLI（开发模式）
cd .src/Joker.UnityCli && dotnet run -- <command>

# 编译发布
cd .src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64
```

## 许可证

MIT
