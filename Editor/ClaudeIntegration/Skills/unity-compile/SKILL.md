---
name: unity-compile
description: 触发 Unity 脚本重编译和域重载。触发词：重新编译、刷新脚本、域重载、compile、脚本更新、重载代码、编译脚本（指 Unity Editor 脚本重编译，不是构建最终产物）。当用户修改了 C# 脚本文件后需要 Unity Editor 重新编译加载时使用。
---

# Unity Compile

触发 Unity Editor 的脚本重编译（域重载），让修改后的 C# 脚本生效。

## 何时使用

- 用户修改了 C# 脚本文件后要"重新编译"、"刷新代码"
- 用户要触发 Unity 的域重载（Domain Reload）
- 用户提到"脚本没生效"、"需要重编译"
- 用户提到"compile"但不是指构建最终产物

**注意区分：** 如果用户要"打包"、"构建 APK/EXE"，应使用 `unity-build` 技能。

## 命令格式

```bash
joker-unity compile --project <PATH> [--timeout <SECONDS>] [--json]
```

### 参数说明

| 参数 | 必填 | 说明 |
|------|------|------|
| `--project <PATH>` | 否 | Unity 项目路径，省略则自动检测当前目录 |
| `--timeout <SECONDS>` | 否 | 超时时间（秒），默认 300 |
| `--json` | 否 | JSON 格式输出 |

### 示例

```bash
# 触发编译
joker-unity compile --project ./MyUnityProject

# 带超时的编译
joker-unity compile --project ./MyUnityProject --timeout 600 --json
```

## 结果解读（--json 模式）

```json
{
  "success": true,
  "status": "completed",
  "errors": []
}
```

### 状态值

| Status | 含义 |
|--------|------|
| `completed` | 编译成功完成 |
| `up_to_date` | 没有需要编译的变更 |
| `server_not_found` | Unity Editor 未运行 |
| `timeout` | 编译超时 |

## 编译期间的行为

- 编译触发后，Unity HTTP 服务器会暂时停止响应（域重载期间）
- 编译完成后服务器自动重启并恢复 `ready` 状态
- 编译期间发送的 exec 请求会被自动拒绝

## 推荐工作流

```
1. compile --json → 触发编译
2. 等待编译完成（自动等待，无需手动轮询）
3. status --json → 确认服务器恢复 ready 状态
4. 可以继续执行其他操作（如 exec）
```

## 注意事项

- 需要 Unity Editor 正在运行且已加载目标项目
- 编译过程中 Unity Editor 会短暂无响应，这是正常的域重载行为
- 如果编译失败，errors 数组会包含具体的编译错误信息
- 大型项目编译可能需要较长时间，可适当增加 --timeout
