---
name: unity-project
description: 查询 Unity 项目信息和资源管理。触发词：项目信息、Unity 版本、包依赖、查看资源、搜索资源、列出 Prefab、查找材质、info、asset、list。当用户要查看 Unity 项目的元数据（版本、名称、依赖包）或管理项目中的资源文件时使用。
---

# Unity Project Info & Assets

查询 Unity 项目的基本信息（版本、名称、依赖包）以及列出和搜索项目资源。

## 何时使用

- 用户要"查看项目信息"、"Unity 版本是多少"、"项目用了哪些包"
- 用户要"列出资源"、"搜索 Prefab"、"找某个材质"
- 用户要了解项目的整体结构
- 用户提到 "info"、"asset"、"list"、"搜索资源"

这些都是只读操作，不会修改项目。

## 项目信息

### 命令格式

```bash
joker-unity info --project <PATH> [--json]
```

### 返回信息

| 字段 | 说明 |
|------|------|
| `name` | 项目名称 |
| `path` | 项目路径 |
| `unityVersion` | Unity 版本号 |
| `packageDependencies` | UPM 包依赖列表 |

### 示例

```bash
# 查看项目信息
joker-unity info --project ./MyUnityProject

# JSON 输出
joker-unity info --project ./MyUnityProject --json
```

## 资源管理

### 列出所有资源

```bash
joker-unity asset --project <PATH> [--json]
```

### 搜索资源

```bash
joker-unity asset <QUERY> --project <PATH> [--json]
```

### 参数说明

| 参数 | 必填 | 说明 |
|------|------|------|
| `<QUERY>` | 否 | 搜索关键词，省略则列出所有资源 |
| `--project <PATH>` | 否 | Unity 项目路径，省略则自动检测 |
| `--json` | 否 | JSON 格式输出 |

### 示例

```bash
# 列出所有资源
joker-unity asset --project ./MyUnityProject

# 搜索包含 "Player" 的资源
joker-unity asset Player --project ./MyUnityProject

# 搜索 Prefab
joker-unity asset .prefab --project ./MyUnityProject --json
```

## 资源输出字段（--json 模式）

```json
[
  {
    "extension": ".prefab",
    "relativePath": "Assets/Prefabs/Player.prefab"
  }
]
```

## 常用场景

| 场景 | 命令 |
|------|------|
| 查看项目 Unity 版本 | `info --project <PATH>` |
| 查看项目依赖了哪些包 | `info --project <PATH> --json` |
| 列出所有 Prefab | `asset .prefab --project <PATH>` |
| 搜索特定名称的资源 | `asset <关键词> --project <PATH>` |
| 列出所有场景文件 | `asset .unity --project <PATH>` |

## 注意事项

- 所有操作都是只读的，不会修改项目
- 资源搜索基于文件路径匹配，不是基于资源内容
- 搜索时扩展名可以作为关键词使用（如 `.prefab`、`.mat`、`.cs`）
- 不需要 Unity Editor 运行，直接读取文件系统
