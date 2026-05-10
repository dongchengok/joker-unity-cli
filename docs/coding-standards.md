# 编码规范

以 Unity 官方 C# 编码规范为基础，以下仅列出项目特有约定和差异。

## 命名约定

- 类、接口、方法、属性：PascalCase
- 私有字段：_camelCase（带下划线前缀）
- 局部变量、参数：camelCase
- 常量：PascalCase（不使用 SCREAMING_CASE）
- 布尔变量/属性以 is/has/can 开头
- 接口以 I 开头
- 事件处理器以 On 开头（如 OnBuildCompleted）

## 文件组织

- 一个文件一个类型，文件名与类型名一致
- 成员排列：字段 → 构造函数 → 属性 → 公有方法 → 私有方法
- using 放文件顶部，System 命名空间优先

## 目录结构约定

- 按功能模块组织目录，不按类型分
- Editor/ 下放所有 Editor only 的代码
- Runtime/ 下放运行时可用的代码
- 不要在 Runtime/ 引用 UnityEditor 命名空间

## 注释规范

- 不写显而易见的注释
- 只在 WHY 不明显时加注释
- 公共 API 使用 XML 文档注释（/// summary）

## 异步编程

- 异步方法返回 Task 或 Task<T>，不用 void（事件处理器除外）
- 异步方法名以 Async 后缀
- 支持 CancellationToken

## 错误处理

- 不捕获无法处理的异常
- 系统边界（用户输入、外部调用）做验证
- 使用具体异常类型
- 日志使用 Spectre.Console 的 AnsiConsole.WriteException

## 依赖管理

- 优先使用 NuGet 包
- 新增依赖需评估维护状态、许可证
- 避免功能重叠的包
