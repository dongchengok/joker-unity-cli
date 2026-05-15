---
name: unity-build
description: 构建和打包 Unity 项目。触发词：构建、打包、build、导出 APK/EXE/Xcode 项目、制作安装包、编译项目（指生成最终产物而非脚本编译）。当用户要将 Unity 项目构建为特定平台的可执行文件或安装包时使用。
---

# Unity Build

使用 joker-unity CLI 触发 Unity 项目构建，生成目标平台的可执行文件。

## 何时使用

- 用户要"构建"、"打包"、"导出" Unity 项目
- 用户要生成 APK、EXE、Xcode 项目等平台产物
- 用户提到 "build"、"导出"、"打包发布"

**注意区分：** 如果用户说的是"重新编译脚本"、"刷新代码"，应使用 `unity-compile` 技能而非本技能。

## 命令格式

```bash
joker-unity build <PLATFORM> --project <PATH> [--unity <PATH_OR_VERSION>] [--output <PATH>] [--scenes <SCENES>] [--json]
```

### 参数说明

| 参数 | 必填 | 说明 |
|------|------|------|
| `<PLATFORM>` | 是 | 构建目标平台：Win64、Android、iOS、WebGL 等 |
| `--project <PATH>` | 否 | Unity 项目路径，省略则自动检测当前目录 |
| `--unity <PATH_OR_VERSION>` | 否 | Unity 可执行文件路径或版本号（如 2022.3.20f1） |
| `--output <PATH>` | 否 | 构建输出路径，默认 `<项目>/Builds/<PLATFORM>` |
| `--scenes <SCENES>` | 否 | 逗号分隔的场景列表 |
| `--json` | 否 | JSON 格式输出，适合程序解析 |

### 示例

```bash
# 构建 Windows 64位
joker-unity build Win64 --project ./MyUnityProject

# 构建 Android APK，指定 Unity 版本
joker-unity build Android --project ./MyUnityProject --unity 2021.3.20f1 --json

# 构建指定场景
joker-unity build Win64 --project ./MyUnityProject --scenes "Assets/Scenes/Main.unity,Assets/Scenes/Menu.unity"
```

## 错误处理

| 错误信息 | 原因 | 解决方案 |
|----------|------|---------|
| "No Unity project found" | 项目路径无效 | 检查路径是否包含 Assets 目录和 ProjectSettings |
| "Could not locate Unity installation" | 未找到 Unity | 使用 --unity 指定路径或版本号 |
| "Build failed" | 构建过程中出错 | 查看 Log 路径下的日志文件定位具体错误 |

## 注意事项

- 构建通过 Unity.exe batch mode 执行，需要 Unity 安装在机器上
- 构建时间较长（通常几十秒到几分钟），建议使用 `--json` 模式便于程序化处理结果
- 首次构建某个平台需要该平台的 Build Support 模块已安装
