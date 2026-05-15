---
name: unity-code
description: 在 Unity Editor 中执行 C# 代码和查询服务器状态。触发词：执行代码、运行 C#、跑一下、exec、调用 Unity API、测试一下、Unity 里跑代码、检查状态、Unity 在线吗、服务器状态。当用户要在 Unity Editor 环境中执行 C# 代码片段或检查执行环境状态时使用。
---

# Unity Code Execution

在运行中的 Unity Editor 内执行 C# 代码，支持语句级执行和完整文件编译两种模式。

## 何时使用

- 用户要"执行代码"、"运行脚本"、"在 Unity 里跑代码"
- 用户要测试某段 C# 代码在 Unity 环境中的行为
- 用户要检查 Unity Editor 服务器状态（是否在线、是否编译中）
- 用户提到 "exec"、"运行这段代码"、"试一下"

## 前置条件：检查状态

**执行代码前必须先检查 Unity Editor 状态：**

```bash
joker-unity status --project <PATH> --json
```

返回的状态值：
- `"ready"` + `"serverResponding": true` → 可以执行代码
- `"compiling"` → 正在编译，等待后重试
- `"stopped"` 或 `"serverResponding": false` → Unity Editor 未运行或插件未加载，提示用户打开 Unity

## 执行代码

### Script 模式（语句级）

适合快速执行单条或少量语句：

```bash
joker-unity exec "1+1" --project <PATH> --json
joker-unity exec "UnityEngine.Debug.Log(\"Hello\")" --project <PATH> --json
```

### File 模式（完整文件）

适合执行包含多条语句、类定义的完整 C# 文件：

```bash
joker-unity exec --file <FILE_PATH> --project <PATH> --json
```

### 参数说明

| 参数 | 必填 | 说明 |
|------|------|------|
| `"CODE"` | 二选一 | C# 代码字符串（script 模式） |
| `--file <PATH>` | 二选一 | C# 文件路径（compile 模式） |
| `--project <PATH>` | 否 | Unity 项目路径，省略则自动检测 |
| `--timeout <MS>` | 否 | 执行超时（毫秒），默认 30000 |
| `--json` | 否 | JSON 格式输出 |

**注意：** `CODE` 和 `--file` 不能同时指定。

## 结果解读（--json 模式）

```json
{
  "success": true,
  "output": "控制台输出",
  "result": "表达式返回值",
  "durationMs": 42,
  "error": null,
  "errorDetail": null,
  "errorCode": null
}
```

### 错误码

| ErrorCode | 含义 | 处理方式 |
|-----------|------|---------|
| `CompilationError` | 代码编译失败 | 检查语法错误，查看 errorDetail 中的行列号 |
| `TimeoutError` | 执行超时 | 增加 --timeout 或优化代码 |
| `ServerError` | Unity 服务器内部错误 | 检查 Unity Console 中的错误 |
| null | 无错误 | — |

## 推荐工作流

```
1. status --json → 确认 ready
2. exec "代码" --json → 执行
3. 检查 success 字段 → 成功则读取 result/output，失败则读取 error
4. 如果是编译错误 → 修改代码后重新执行
```

## 注意事项

- 代码在 Unity Editor 主线程执行，长时间运行的操作会阻塞 UI
- Script 模式适合表达式和简单语句；复杂代码建议用 --file 模式
- 编译中的请求会被自动拒绝，需要等待编译完成
- 确保代码中引用的类型在 Unity 环境中可用（UnityEngine、UnityEditor 等）
