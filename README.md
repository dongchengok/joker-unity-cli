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
cd src && dotnet test

# 运行 CLI（开发模式）
cd src/Joker.UnityCli && dotnet run -- <command>

# 编译发布
cd src/Joker.UnityCli && dotnet publish -c Release -o ../../Tools~/win-x64
```

## 许可证

MIT
