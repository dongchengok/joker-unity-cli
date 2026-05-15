# Claude Code 技能自动配置设计

## 背景

joker-unity-cli 是一个 Unity UPM 插件包，提供 CLI 工具帮助 AI 智能体集成 Unity 开发流程。目前 AI 智能体需要手动了解 CLI 命令的使用方式。通过将 CLI 能力封装为 Claude Code 技能，AI 智能体可以更自然、准确地调用 Unity 开发操作。

## 目标

- 将 joker-unity CLI 的核心能力封装为 4 个 Claude Code 技能
- 支持自动配置（项目已有 `.claude/` 目录时）和手动配置（Editor 面板按钮）
- 包更新时自动更新已安装的技能文件

## 技能划分

4 个技能，按 AI 触发条件清晰不重叠的原则划分：

| 技能名 | 覆盖命令 | 触发意图 | AI 输出 |
|--------|----------|----------|---------|
| `unity-build` | build | "构建项目"、"打包 APK/EXE" | 构建命令调用指导 |
| `unity-code` | exec + status | "执行代码"、"运行这段 C#" | 状态检查 + 代码执行流程 |
| `unity-compile` | compile | "重新编译脚本"、"刷新代码" | 编译触发指导 |
| `unity-project` | info + asset | "项目信息"、"查找资源" | 查询命令调用指导 |

**为什么 build 和 compile 分开？** 中文语境下"构建"和"编译"容易混淆，分开让 AI 根据用户意图精准路由。

**为什么 exec 和 status 合并？** exec 执行前必须检查 status（编译中/服务器离线），它们有前置依赖关系。

## 目录结构

```
Editor/
├── ClaudeIntegration/
│   ├── Skills/                        # 技能源文件（随包发布）
│   │   ├── unity-build/
│   │   │   └── skill.md
│   │   ├── unity-code/
│   │   │   └── skill.md
│   │   ├── unity-compile/
│   │   │   └── skill.md
│   │   ├── unity-project/
│   │   │   └── skill.md
│   │   └── skills-manifest.json
│   └── ClaudeSkillInstaller.cs
├── UI/
│   ├── JokerServerSettingsWindow.cs   # 现有面板，新增 Claude Code 配置区域
│   └── JokerToolbarButton.cs          # 不改动
```

## 安装流程

### 自动安装（[InitializeOnLoad]）

```
Unity 启动 → JokerServerController [InitializeOnLoad]
  │
  ├─ 检测 <项目根>/.claude/ 是否存在
  │   ├─ 存在 → ClaudeSkillInstaller.AutoInstall()
  │   │   ├─ 读取 <项目>/.claude/skills/.joker-unity-manifest.json
  │   │   ├─ 对比 Editor/ClaudeIntegration/Skills/skills-manifest.json 版本号
  │   │   ├─ 版本不同或未安装 → 删除旧文件夹 → 复制新文件夹 → 更新 manifest
  │   │   └─ 版本相同 → 跳过
  │   │
  │   └─ 不存在 → 不自动安装，等待手动触发
```

### 手动安装（Editor 面板）

在 `JokerServerSettingsWindow` 面板底部新增 "Claude Code 技能" 区域：
- 状态标签：已安装 v0.1.0 / 未安装
- "安装技能" 按钮：创建 `.claude/skills/` + 复制技能文件夹 + 写入 manifest
- "卸载技能" 按钮：删除已安装的技能文件夹 + 删除 manifest

### 版本对比机制

**源 manifest**（包内）：`Editor/ClaudeIntegration/Skills/skills-manifest.json`
```json
{
  "version": "0.1.0",
  "skills": [
    {"name": "unity-build", "directory": "unity-build"},
    {"name": "unity-code", "directory": "unity-code"},
    {"name": "unity-compile", "directory": "unity-compile"},
    {"name": "unity-project", "directory": "unity-project"}
  ]
}
```

**目标 manifest**（项目内）：`<项目>/.claude/skills/.joker-unity-manifest.json`
- 安装时从源 manifest 复制
- 版本号与 `package.json` 保持同步

**对比逻辑：** 字符串相等比较。installedVersion == null → 全量安装；== sourceVersion → 跳过；!= sourceVersion → 先删后装。

## ClaudeSkillInstaller API

```csharp
public static class ClaudeSkillInstaller
{
    public static bool IsInstalled { get; }
    public static string InstalledVersion { get; }
    public static string SourceVersion { get; }

    public static void AutoInstall();   // [InitializeOnLoad] 调用，仅在 .claude/ 存在时执行
    public static void Install();       // 手动安装（面板按钮）
    public static void Uninstall();     // 卸载（面板按钮）
}
```

## 技能内容设计

每个 `skill.md` 包含：
- **frontmatter**：name, description, trigger（触发条件）
- **使用指导**：何时使用、命令参数、输出解读
- **错误处理**：常见错误码和排查步骤
- **注意事项**：安全约束、前置条件

### unity-build/skill.md

触发：用户要构建/打包 Unity 项目
- 指导 AI 使用 `joker-unity build --project <path> --platform <target>`
- 构建配置参数说明
- 构建失败时的排查流程（检查 Unity 路径、项目路径、平台支持）

### unity-code/skill.md

触发：用户要在 Unity 中执行/调试 C# 代码
- 第一步：`joker-unity status --project <path> --json` 检查状态
- 根据状态决策：ready → 执行；compiling → 等待重试；stopped → 提示用户打开 Unity
- `joker-unity exec` 的 script 模式（语句级）和 file 模式（完整文件）区别
- ExecResult 结构化错误码解读

### unity-compile/skill.md

触发：用户要重新编译 Unity 脚本
- `joker-unity compile --project <path>` 触发域重载
- 编译期间服务器不可用的预期行为
- 编译后重新检查状态的流程

### unity-project/skill.md

触发：用户要查看项目信息/资源
- `joker-unity info --project <path>` 查看项目元数据
- `joker-unity asset list/search` 资源管理操作
- 只读操作，无副作用风险

## 权限策略

**不自动添加 CLI 执行权限**。用户首次通过技能触发 CLI 命令时，Claude Code 会弹出权限提示，用户手动批准即可。

## 不做的事情

- 不创建独立的 EditorWindow 或 SettingsProvider
- 不自动创建 `.claude/` 目录
- 不修改全局 `~/.claude/` 配置
- 不添加 CLI `setup-claude` 命令
- 不自动添加 CLI 执行权限

## 验证方案

1. **安装验证**：在测试 Unity 工程（.Unity2019/.Unity2021）中，手动创建 `.claude/` 目录，验证自动安装是否正常
2. **面板验证**：删除 `.claude/` 目录，通过 Editor 面板手动安装，验证按钮和状态显示
3. **版本更新验证**：修改 manifest 版本号，重新加载 Unity，验证技能文件被正确更新
4. **技能功能验证**：在 Claude Code 中测试每个技能是否能被正确触发和执行
5. **卸载验证**：通过面板卸载，验证技能文件被完整清理
