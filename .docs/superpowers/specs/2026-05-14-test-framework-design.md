# Joker Test Framework 设计方案

> 日期：2026-05-14
> 状态：草稿（待调整）

## 一、功能定位

为 AI 智能体提供 Unity 游戏内自动化测试能力。AI 通过 CLI 工具 dump UI 理解界面结构，生成测试脚本下发到 Unity 内执行，最后分析结构化报告。

**核心约束**：测试脚本在 Unity 内部执行（避免 HTTP 往返延迟），AI 只负责生成脚本和分析结果。

---

## 二、架构概览

```
┌────────────────────────────────────────────────────────────────┐
│  CLI (joker-unity test)                                        │
│                                                                │
│  test dump  ──→ HTTP /test/dump ──→ 返回 UI 树 JSON           │
│  test run   ──→ HTTP /test/run  ──→ 下发脚本，返回报告        │
│  test query ──→ HTTP /test/query → 单步节点查询               │
└───────────────────────┬────────────────────────────────────────┘
                        │ HTTP
┌───────────────────────▼────────────────────────────────────────┐
│  Unity Editor                                                  │
│                                                                │
│  ┌─ TestRunner ───────────── 管理测试生命周期，帧级调度       │
│  │     │                                                        │
│  │     ├→ TestContext ────── 唯一状态容器                      │
│  │     │                                                        │
│  │     ├→ TestReportBuilder ─ Context → Report                 │
│  │     │                                                        │
│  │     └→ TestCoroutine ──── 内部协程调度 + 公共等待 API       │
│  │                                                              │
│  ├─ TestAssert ──────────── 断言，结果写入 Context             │
│  │                                                              │
│  ├─ TestUI ──────────────── UI 查找/操作/Dump 入口             │
│  │     ├→ TestUINode ────── 节点代理                           │
│  │     └→ TestUIDump ────── UI 树数据                          │
│  │                                                              │
│  └─ TestReport ──────────── 报告数据结构                       │
│                                                                │
└────────────────────────────────────────────────────────────────┘

依赖方向（单向，无环）：
  TestRunner → TestContext ← TestAssert
  TestRunner → TestContext ← TestCoroutine（写入等待超时结果）
  TestAssert → TestUI（便捷断言内部调用 Find）
  TestCoroutine 不依赖 TestUI/TestAssert
  TestReportBuilder 只读 TestContext → 生成 TestReport
```

---

## 三、通信模式（方案 C 混合模式）

| 场景 | CLI 命令 | 模式 | 说明 |
|------|---------|------|------|
| 理解 UI 状态 | `test dump` | 单步查询 | 即时返回 UI 树 JSON |
| 查询单个节点 | `test query <path>` | 单步查询 | 即时返回节点属性 |
| 执行测试脚本 | `test run --script <code>` | 整段下发 | 脚本在 Unity 内执行，返回报告 |

**Phase 1**：复用现有 `/exec` 端点，测试脚本通过 Roslyn 编译执行。
**Phase 2**：独立 `/test/*` 端点，支持长连接等待测试完成。

---

## 四、CLI 命令设计

```
joker-unity test dump [--project <path>] [--json]
    → 输出当前 UI 层级树

joker-unity test query <path> [--project <path>] [--json]
    → 输出指定节点的属性信息

joker-unity test run --script <code|--file <path>> [--timeout <seconds>] [--project <path>] [--json]
    → 下发测试脚本并执行，返回测试报告

joker-unity test run --class <className> [--timeout <seconds>] [--project <path>] [--json]
    → 下发结构化测试类并执行，返回测试报告
```

---

## 五、HTTP 协议（Phase 2 独立端点）

### 5.1 Dump UI 树

```
GET /test/dump

Response 200:
{
  "type": "test_dump",
  "root": { ... },
  "node_count": 127,
  "duration_ms": 23
}
```

### 5.2 查询节点

```
GET /test/query?path=Panel.btn_ok

Response 200:
{
  "type": "test_query",
  "found": true,
  "node": {
    "name": "btn_ok",
    "type": "Button",
    "visible": true,
    "enabled": true,
    "text": "OK",
    "position": [0.5, 0.3],
    "size": [0.2, 0.06]
  },
  "duration_ms": 5
}
```

### 5.3 执行测试

```
POST /test/run
{
  "test_name": "LoginTest",
  "script": "public IEnumerator Execute() { ... }",
  "timeout": 60,
  "mode": "script"
}

Response 200:
{
  "type": "test_report",
  "test_name": "LoginTest",
  "status": "passed",
  "duration_ms": 3250,
  "assertions": {
    "total": 5,
    "passed": 5,
    "failed": 0
  },
  "details": [
    { "pass": true, "description": "节点 'btn_login' 应存在", "detail": "已找到", "timestamp_ms": 120 },
    { "pass": true, "description": "文本应为 'Welcome'", "detail": "实际值: 'Welcome'", "timestamp_ms": 2840 }
  ],
  "logs": [
    { "message": "开始执行 LoginTest", "timestamp_ms": 0 },
    { "message": "等待节点 panel_login (3s超时)", "timestamp_ms": 50 }
  ]
}
```

### 5.4 节点未找到

```
Response 200:
{
  "type": "test_query",
  "found": false,
  "node": null,
  "duration_ms": 3
}
```

### 5.5 测试超时

```
Response 200:
{
  "type": "test_report",
  "test_name": "BattleTest",
  "status": "timeout",
  "duration_ms": 60000,
  "assertions": { "total": 3, "passed": 2, "failed": 1 },
  "details": [
    { "pass": false, "description": "节点 'boss_reward' 应出现", "detail": "超时 10s 未出现", "timestamp_ms": 58200 }
  ]
}
```

---

## 六、测试脚本写法

### 6.1 统一 IEnumerator 风格

所有测试方法返回 `IEnumerator`，同步操作直接调用，异步操作 `yield return`。

```csharp
// 简单测试
public IEnumerator Execute()
{
    TestUI.Click("LoginPanel.btn_guest");
    yield return TestCoroutine.WaitForNode("HomePanel", 5f);

    TestAssert.Exists("HomePanel.label_username");
    TestAssert.Text("HomePanel.label_username", "Guest_001");
    TestAssert.IsTrue(PlayerManager.Instance.Level == 1, "初始等级应为1");
}
```

### 6.2 结构化测试类

```csharp
public class BattleTests
{
    private PlayerManager _player;

    [TestSetup]
    public IEnumerator Setup()
    {
        _player = PlayerManager.Instance;
        TestUI.Click("MainMenu.btn_battle");
        yield return TestCoroutine.WaitForNode("BattlePanel", 5f);
    }

    [TestMethod]
    public IEnumerator TestUsePotion()
    {
        int hpBefore = _player.CurrentHp;
        TestUI.Click("BattlePanel.SkillBar.btn_potion");
        yield return TestCoroutine.Delay(0.5f);
        TestAssert.Greater(_player.CurrentHp, hpBefore, "使用药水后HP应增加");
    }

    [TestMethod]
    public IEnumerator TestSkillCooldown()
    {
        TestUI.Click("BattlePanel.SkillBar.btn_skill_1");
        yield return TestCoroutine.Delay(0.1f);
        TestAssert.Disabled("BattlePanel.SkillBar.btn_skill_1");

        yield return TestCoroutine.WaitFor(
            () => TestUI.Find("BattlePanel.SkillBar.btn_skill_1").Enabled, 10f);
        TestAssert.Enabled("BattlePanel.SkillBar.btn_skill_1");
    }

    [TestTeardown]
    public IEnumerator Teardown()
    {
        TestUI.Click("BattlePanel.btn_exit");
        yield return TestCoroutine.WaitForNode("MainMenu", 3f);
    }
}
```

### 6.3 规则

- 返回 `void` 的方法 → 同步，当前帧执行
- 返回 `IEnumerator` 的方法 → 异步，`yield return` 跨帧等待
- 断言失败不中断执行，记录到 TestContext，最终统一报告

---

## 七、各类完整公共 API

### 7.1 TestRunner

```csharp
public class TestRunner
{
    void Run(string testName, IEnumerator testRoutine, float timeout = 60f);
    void Cancel();
    bool IsRunning { get; }
    string CurrentTestName { get; }
    void OnComplete(Action<TestReport> callback);
}
```

### 7.2 TestContext

```csharp
public class TestContext
{
    string TestName { get; }
    TestStatus Status { get; }          // Running | Passed | Failed | Timeout | Cancelled
    float Duration { get; }
    float StartTime { get; }

    IReadOnlyList<AssertionResult> Assertions { get; }
    void AddAssertion(AssertionResult result);
    int PassCount { get; }
    int FailCount { get; }

    IReadOnlyList<TestLogEntry> Logs { get; }
    void AddLog(string message);

    void MarkPassed();
    void MarkFailed(string reason);
    void MarkTimeout(float elapsed);
    void MarkCancelled();
}
```

### 7.3 辅助数据结构

```csharp
public enum TestStatus { Running, Passed, Failed, Timeout, Cancelled }

public class AssertionResult
{
    bool Pass { get; }
    string Description { get; }
    string Detail { get; }
    float Timestamp { get; }
}

public class TestLogEntry
{
    string Message { get; }
    float Timestamp { get; }
}
```

### 7.4 TestAssert

```csharp
public static class TestAssert
{
    // 通用
    void IsTrue(bool condition, string description = "");
    void IsFalse(bool condition, string description = "");

    // 相等
    void Equal<T>(T actual, T expected, string description = "");
    void NotEqual<T>(T actual, T expected, string description = "");

    // 比较
    void Greater<T>(T actual, T expected, string description = "") where T : IComparable;
    void Less<T>(T actual, T expected, string description = "") where T : IComparable;

    // 空值
    void IsNull(object value, string description = "");
    void NotNull(object value, string description = "");

    // UI 便捷（内部调用 TestUI.Find + 基础断言）
    void Exists(string path);
    void NotExists(string path);
    void Text(string path, string expected);
    void TextContains(string path, string substring);
    void Enabled(string path);
    void Disabled(string path);
}
```

### 7.5 TestCoroutine

```csharp
public static class TestCoroutine
{
    // 时间
    IEnumerator Delay(float seconds);

    // 帧数
    IEnumerator DelayFrames(int frameCount);

    // 条件等待
    IEnumerator WaitFor(Func<bool> condition, float timeout, string description = "");

    // UI 节点等待
    IEnumerator WaitForNode(string path, float timeout);

    // 节点消失等待
    IEnumerator WaitForNodeDisappear(string path, float timeout);
}
```

内部实现 `CoroutineScheduler`（栈式 IEnumerator 调度器），对外不暴露。

### 7.6 TestUI

```csharp
public static class TestUI
{
    // 查找（支持 . 路径：Panel.btn_ok）
    TestUINode Find(string path);
    TestUINode[] FindAll(string name);
    TestUINode FindByType<T>() where T : Component;

    // 快捷操作（查找 + 动作）
    TestUI Click(string path);
    TestUI LongClick(string path, float duration = 1f);
    TestUI InputText(string path, string text);

    // Dump
    TestUIDump Dump();

    // 全局坐标操作
    void ClickAt(Vector2 normalizedPosition);
    void LongClickAt(Vector2 normalizedPosition, float duration);
    IEnumerator SwipeFromTo(Vector2 from, Vector2 to, float duration);

    // 屏幕
    int ScreenWidth { get; }
    int ScreenHeight { get; }
}
```

### 7.7 TestUINode

```csharp
public class TestUINode
{
    // 属性
    string Name { get; }
    string TypeName { get; }
    bool Visible { get; }
    bool Enabled { get; }
    string Text { get; }
    Vector2 Position { get; }           // 归一化坐标 [0,1]
    Vector2 Size { get; }               // 归一化尺寸 [0,1]

    // 子路径查找（支持 . 路径）
    TestUINode Find(string path);

    // 快捷操作（找子节点 + 动作）
    TestUINode Click(string path);
    TestUINode InputText(string path, string text);
    TestUINode LongClick(string path, float duration = 1f);

    // 子节点查询
    bool Exists(string path);
    string GetText(string path);

    // 自身操作
    TestUINode Click();
    TestUINode LongClick(float duration = 1f);
    TestUINode InputText(string text);
    IEnumerator DragTo(TestUINode target, float duration = 0.3f);
    IEnumerator Swipe(Vector2 direction, float distance = 100f, float duration = 0.3f);

    // 层级
    TestUINode Parent();

    // 组件访问
    T GetComponent<T>() where T : Component;
    bool TryGetComponent<T>(out T component) where T : Component;
}
```

### 7.8 TestUIDump

```csharp
public class TestUIDump
{
    TestUIDumpNode Root { get; }
    int NodeCount { get; }
    string ToJson();

    TestUIDumpNode Find(string path);
    List<TestUIDumpNode> FindAll(string name);
}

public class TestUIDumpNode
{
    string Name { get; }
    string Type { get; }
    bool Visible { get; }
    bool Enabled { get; }
    string Text { get; }
    Vector2 Position { get; }
    Vector2 Size { get; }
    string[] Components { get; }
    List<TestUIDumpNode> Children { get; }
    string Path { get; }                // 层级路径 "Canvas/Panel/Button"
}
```

### 7.9 TestReport

```csharp
public class TestReport
{
    string TestName { get; }
    TestStatus Status { get; }
    float Duration { get; }
    string Timestamp { get; }

    int TotalAssertions { get; }
    int PassedAssertions { get; }
    int FailedAssertions { get; }

    List<AssertionResult> Assertions { get; }
    List<TestLogEntry> Logs { get; }

    string ToJson();
    bool IsPassed { get; }
}
```

### 7.10 TestReportBuilder

```csharp
public static class TestReportBuilder
{
    TestReport Build(TestContext context);
}
```

### 7.11 TestAttributes（结构化测试类）

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class TestSetupAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class TestMethodAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class TestTeardownAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class TestClassAttribute : Attribute { }
```

---

## 八、执行流程

### 8.1 完整测试流程（AI 视角）

```
Step 1: 理解界面
  AI → joker-unity test dump --project .Unity2021 --json
  ← { UI 树 JSON，包含所有节点名称、类型、层级 }

Step 2: 生成并执行测试
  AI → joker-unity test run --script "public IEnumerator Execute() { ... }" --project .Unity2021 --json
  ← { 测试报告 JSON }

Step 3: 分析结果
  AI 读取报告中的 status / assertions / details，决定下一步
```

### 8.2 Unity 内部执行链路

```
HTTP POST /test/run { script: "..." }
  │
  ├─ TestHttpHandler 接收请求
  │
  ├─ ScriptExecutor 编译脚本（Roslyn，复用 exec 基础设施）
  │   └─ 自动添加测试框架引用
  │
  ├─ 编译产物调用 TestRunner.Run()
  │
  ├─ TestRunner 注册 EditorApplication.update
  │   │
  │   ├─ 每帧: CoroutineScheduler.Tick()
  │   │   └─ 推进 IEnumerator 状态机
  │   │       ├─ 同步操作（Click、InputText）→ 当前帧执行
  │   │       ├─ yield return null → 等一帧
  │   │       ├─ yield return IEnumerator → 嵌套压栈执行
  │   │       └─ yield break → 当前协程结束
  │   │
  │   └─ 超时 / 完成 → 生成报告
  │
  ├─ TestReportBuilder.Build(context) → TestReport
  │
  └─ HTTP Response 返回报告 JSON
```

---

## 九、目录结构

### Unity Editor 侧

```
Editor/
├── TestFramework/
│   ├── TestRunner.cs
│   ├── TestContext.cs
│   ├── TestAssert.cs
│   ├── TestCoroutine.cs
│   ├── TestUI.cs
│   ├── TestUINode.cs
│   ├── TestUIDump.cs
│   ├── TestReport.cs
│   ├── TestReportBuilder.cs
│   ├── TestAttributes.cs
│   └── Models/
│       ├── AssertionResult.cs
│       ├── TestLogEntry.cs
│       └── TestUIDumpNode.cs
├── ScriptServer/
│   └── TestHttpHandler.cs           # 新增：/test/* 端点处理
│   ...
```

### CLI 侧

```
.src/Joker.UnityCli/
├── Commands/
│   └── TestCommand.cs               # 新增：test 命令
├── Services/
│   └── TestService.cs               # 新增：测试通信服务
└── Models/
    └── TestModels.cs                # 新增：CLI 侧请求/响应模型
```

---

## 十、渐进式实施路径

| 阶段 | 内容 | 通信方式 |
|------|------|---------|
| **Phase 1** | TestRunner + TestAssert + TestUI + TestCoroutine（核心框架） | 复用 `/exec` 端点 |
| **Phase 2** | CLI `test` 命令 + 独立 `/test/*` 端点 | 独立 HTTP 端点 |
| **Phase 3** | TestUIDump + `test dump` 命令 | 独立 HTTP 端点 |
| **Phase 4** | 结构化测试类 + `test run --class` | 独立 HTTP 端点 |
