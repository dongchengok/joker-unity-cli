# 集成测试重设计 & ScriptExecution Import 鲁棒性增强 — 设计文档

**日期：** 2026-05-12
**状态：** 已确认

---

## 一、背景

### 问题1：集成测试质量差

当前 16 个集成测试全部依赖真实 Unity Editor 运行（CI 中全部静默跳过），存在竞态条件、弱断言、关键路径缺失等问题：

- `ExecDuringDomainReload_RetriesAndRecovers` 使用 fire-and-forget + 硬编码 1 秒等待，时序不确定
- `TryReadServerPort` 在 `CompileService` 和 `UnityIntegrationTestBase` 中重复实现
- 批处理模式回退、超时轮询、端口不变时的日志解析回退等核心路径无覆盖
- Script/Compile 模式的失败路径（运行时异常、超时、无 Execute 方法）无覆盖
- 没有任何操作 Unity Scene GameObject 的测试

### 问题2：ScriptExecution Import 脆弱

`Editor/ScriptExecution/` 中：

- Eval 模式仅引用 4 个程序集、5 个命名空间
- Compile 模式引用 5 个程序集、0 个命名空间
- 缺少 `System.IO`、`System.Text`、`System.Threading.Tasks`、`UnityEngine.UI`、`UnityEngine.SceneManagement` 等常用引用
- 编译/执行失败后无任何 fallback 机制，直接返回错误

---

## 二、设计目标

1. 集成测试保持依赖真实 Unity Editor，修复竞态条件，增强断言，补全缺失覆盖
2. ScriptExecution 增加鲁棒的 Import fallback 机制：默认常用引用集 → 编译失败 → 分析错误日志 → 重试（最多3次，考虑递进依赖：添加 A 引用后 A 又依赖 B）
3. 消除重复代码，增强代码可维护性

---

## 三、方案设计

### 3.1 任务1：集成测试重新设计

#### 3.1.1 修改现有代码

**`UnityIntegrationTestBase.cs` — 消除重复**

- `TryReadServerPort` 改为直接调用 `CompileService.TryReadServerPort`（已通过 `InternalsVisibleTo` 对测试项目可见）

**`CompilePipelineIntegrationTests.cs` — 修复竞态 + 增强断言**

- `ExecDuringDomainReload_RetriesAndRecovers`：改为先读取旧端口 → 启动编译 → 轮询等端口变化（最多30秒）→ 确认域重载已开始 → 再调用 ExecService 验证重试
- `CompileMode_MultipleErrors_AllReported`：验证两个不同错误码都出现在 Error 中
- `CompileMode_InvalidCode_ReturnsCompilationError`：验证具体 CS 错误码
- `MultipleExecuteMethods_UsesFirst`：增强为确定性验证（验证第一个类的结果被使用）

#### 3.1.2 新增测试

**Script 模式失败路径（`ExecPipelineIntegrationTests.cs`）：**

- `ExecuteAsync_RuntimeException_DivideByZero` — 脚本除以零，验证 Success=false 且 Error 包含异常信息
- `ExecuteAsync_Timeout_ReturnsTimeoutError` — 死循环触发超时

**Compile 模式失败路径（`ExecPipelineIntegrationTests.cs`）：**

- `CompileMode_NoExecuteMethod_ReturnsError` — 类没有 Execute() 方法
- `CompileMode_ExecuteNotStatic_ReturnsError` — Execute() 不是 public static
- `CompileMode_RuntimeException_ReturnsError` — Execute() 内部抛出异常

**GameObject 场景操作（新增 `GameObjectIntegrationTests.cs`，每个操作独立为一个测试用例）：**

Script 模式：

| 测试 | 场景 |
|------|------|
| `ScriptMode_CreateGameObject_Succeeds` | 通过 `new GameObject()` 创建对象 → 验证存在于场景中 |
| `ScriptMode_FindGameObject_ByName_Succeeds` | 按名称查找已创建的对象 → 验证返回非空 |
| `ScriptMode_DestroyGameObject_Succeeds` | 创建后立即 Destroy → 验证对象被标记为 null |
| `ScriptMode_ModifyTransform_Position_Succeeds` | 修改 position → 验证新位置生效 |
| `ScriptMode_ModifyTransform_RotationScale_Succeeds` | 修改 rotation 和 scale → 验证生效 |
| `ScriptMode_AddComponent_BoxCollider_Succeeds` | 添加 BoxCollider 组件 → 验证 `GetComponent<BoxCollider>()` 非空 |
| `ScriptMode_InstantiateAndDestroy_Succeeds` | Instantiate 新对象 → Destroy → 验证已标记销毁 |

Compile 模式：

| 测试 | 场景 |
|------|------|
| `CompileMode_CreateGameObjectHierarchy_Succeeds` | 创建父子层级结构 → 验证 `transform.childCount` 和父子关系 |
| `CompileMode_BatchModifyComponents_Succeeds` | 批量创建 5 个对象，分别设置不同的 position |
| `CompileMode_FindObjectsOfType_Succeeds` | 创建多个对象后通过 `FindObjectsOfType<>` 查找 |

> **注意：** GameObject 操作均通过脚本代码中的静态 API（`GameObject.Find`、`Object.Instantiate`、`Object.Destroy` 等）实现，无需扩展 `ScriptGlobals`。

**CompileService 错误路径（`CompilePipelineIntegrationTests.cs`）：**

- `CompileAsync_PortUnchanged_FallsBackToLogParsing` — 端口不变时的日志解析回退
- `CompileAsync_CompileTimeout_ReturnsTimeout` — 编译阶段超时（注：仅覆盖 Roslyn 编译超时，Compile 模式 `Execute()` 运行时超时因 `MethodBase.Invoke` 同步阻塞限制暂不可测，见 3.2.5 说明）

**已知测试覆盖缺口：**
- `CompileViaBatchmodeAsync`（batchmode 回退路径）因需要启动真实 Unity.exe 进程，难以在自动化集成测试中覆盖。此路径通过 `CompileServiceTests` 单元测试（NSubstitute mock）覆盖。

---

### 3.2 任务2：Import 鲁棒性增强

#### 3.2.1 总体策略

```
解析代码显式 using → 合并默认引用集 → 首次编译
  ├─ 成功 → 返回
  └─ 失败 → 错误分析（基于累积引用集增量修复）
       ├─ 提取所有 CS 错误码和详情
       ├─ 按错误类型处理：
       │   ├─ CS0012: 提取缺失的程序集名 → 添加引用
       │   ├─ CS0246: ① 查映射表 → 命中：添加对应程序集引用/using
       │   │          ② 映射表未命中 → 遍历 AppDomain 已加载程序集搜索类型名
       │   │             ├─ 找到：添加对应程序集引用
       │   │             └─ 找不到：标记 CannotFix
       │   ├─ CS0234: ① 查映射表 → 命中：添加对应程序集引用
       │   │          ② 映射表未命中 → 遍历 AppDomain 搜索命名空间
       │   │             ├─ 找到：添加对应程序集引用
       │   │             └─ 找不到：标记 CannotFix
       │   ├─ CS0433: 识别冲突程序集 → 排除重复引用（优先保留门面程序集）
       │   ├─ CS0103: ① 查映射表 → 命中：尝试修复
       │   │          ② 遍历 AppDomain 搜索类型名 → 找到：添加引用
       │   │          ③ 都找不到：标记 CannotFix（可能是变量名拼写错误）
       │   └─ 其他(语法错误等): 标记不可修复 → 不消耗重试次数，直接返回错误
       ├─ 去掉重复引用（已存在的引用跳过）
       ├─ retry++ → 若 retry < 3 且还有可修复项 → 用累积引用集重新编译
       └─ 若 retry >= 3 或无可修复项 → 返回最终错误（附带所有尝试记录）
```

#### 3.2.2 默认引用集扩展

**程序集引用**（从 AppDomain 中筛选常用程序集）：

- `System.Runtime` / mscorlib
- `System.Core` / System.Linq
- `System.Collections`
- `System.IO`
- `System.Text.RegularExpressions`
- `System.Threading.Tasks`
- `UnityEngine`
- `UnityEditor`
- `UnityEngine.UI`
- `UnityEngine.SceneManagement`
- `UnityEngine.PhysicsModule`

**默认 using 导入**（同时适用于 Eval 模式）：

- `System`、`System.Linq`、`System.Collections.Generic`
- `System.IO`、`System.Text`、`System.Threading.Tasks`
- `UnityEngine`、`UnityEditor`、`UnityEngine.SceneManagement`

#### 3.2.3 Script / Compile 模式统一处理显式 using

两种模式在执行前都先解析代码中的显式 `using` 语句，提前补全对应的程序集引用：

- 解析代码提取所有 `using X.Y.Z;` 语句
- 将显式 using 的命名空间与默认列表合并
- 检查每个 using 命名空间对应的程序集是否已引用，未引用的自动补充
- 如果代码中有 `using System.IO;` 但 `System.IO.dll` 未引用，在首次编译前就添加该引用（减少不必要的 fallback 轮次）

#### 3.2.4 新增类：`CompilationErrorAnalyzer`

**文件：** `Editor/ScriptExecution/CompilationErrorAnalyzer.cs`

**职责：** 解析 Roslyn 编译诊断信息（`IEnumerable<Diagnostic>`，Eval 和 Compile 模式共用同一类型），分类错误，返回修复建议。

**输入/输出接口：**
```
输入: IEnumerable<Diagnostic>     // Roslyn 诊断信息（Eval/Compile 共用类型）
输出: IReadOnlyList<ErrorAnalysis>
  - ErrorCode: string             // CS0246 / CS0012 / CS0234 / CS0433 / CS0103 / ...
  - CanAutoFix: bool
  - FixAction: AddUsing | AddReference | RemoveReference | CannotFix
  - Detail: string                // 要添加/移除的具体程序集名或命名空间
```

**核心：命名空间→程序集映射表**

维护一个可扩展的静态映射字典，覆盖常见的命名空间到程序集的映射：

| 命名空间 | 程序集 |
|---------|--------|
| `System.IO` | `System.IO.dll` / `mscorlib` (取决于 Unity .NET 配置) |
| `System.Text` | `mscorlib` / `System.Runtime.dll` |
| `System.Text.RegularExpressions` | `System.Text.RegularExpressions.dll` / `System.dll` |
| `System.Threading.Tasks` | `System.Threading.Tasks.dll` / `mscorlib` |
| `System.Reflection` | `mscorlib` / `System.Reflection.dll` |
| `System.Diagnostics` | `System.dll` / `System.Diagnostics.dll` |
| `System.Net.Http` | `System.Net.Http.dll` |
| `UnityEngine.UI` | `UnityEngine.UI.dll` |
| `UnityEngine.SceneManagement` | `UnityEngine.CoreModule.dll` |
| `UnityEngine.EventSystems` | `UnityEngine.UI.dll` |
| `UnityEngine.Networking` | `UnityEngine.dll` |

**两层解析策略（快速路径 + 兜底扫描）：**

```
查找类型/命名空间对应的程序集:
  ① 查映射表（快速路径，O(1)）
     └─ 命中 → 返回对应程序集
  ② 映射表未命中 → 遍历 AppDomain.CurrentDomain.GetAssemblies()（兜底扫描）
     ├─ 在已加载程序集中搜索包含目标类型的程序集
     ├─ 找到 → 添加引用 + 可选：将映射关系写入日志供后续优化映射表
     └─ 找不到 → 标记 CannotFix
```

**设计理由：**
- 映射表覆盖 90%+ 的常见场景，一次查表解决，速度快
- 兜底扫描处理映射表未覆盖的第三方 Package、小众 Unity 模块等边缘情况
- 只在映射表查不到时才触发扫描（不是每次都扫描），避免性能浪费
- 扫描仅搜索**已加载**的程序集（`AppDomain.CurrentDomain.GetAssemblies()`），范围可控，不会引入运行时未加载的程序集导致新问题
- 与直接全量引用不同：发现缺失时才精准添加，不会引入无关程序集造成 CS0433 冲突

**映射失败时的处理：** 两层都找不到时，标记为 `CannotFix`，错误信息中给出明确提示：`"无法自动识别命名空间 'X.Y.Z' 对应的程序集，请手动添加引用"`。

#### 3.2.5 修改 `ScriptExecutor`

**引用集累积策略：** 每次重试在上一次引用集基础上增量添加，维护去重逻辑避免重复引用：

```
ExecuteAsync(request):
  var references = GetDefaultReferences()
  var usings = GetDefaultUsings() + ParseExplicitUsings(request.Code)
  var retry = 0
  
  while (true):
    result = TryCompileAndExecute(code, references, usings)
    if (result.Success) return result
    
    analysis = CompilationErrorAnalyzer.Analyze(result.Diagnostics)
    var fixable = analysis.Where(a => a.CanAutoFix).ToList()
    var unfixable = analysis.Where(a => !a.CanAutoFix).ToList()
    
    if (unfixable.Any() || fixable.Count == 0)
      return ErrorResult(analysis)    // 语法错误等，不重试
    
    foreach (var fix in fixable):
      switch (fix.FixAction):
        case AddReference:  references.Add(fix.Detail)  // 已存在则跳过
        case AddUsing:      usings.Add(fix.Detail)
        case RemoveReference: references.Remove(fix.Detail)
    
    retry++
    if (retry >= 3) return ErrorResult(analysis, allAttempts)
```

- `ExecuteScriptAsync`（Eval 模式）：解析代码中的显式 using → 合并默认 using → 失败时从 `CompilationErrorException.Diagnostics` 提取诊断
- `ExecuteCompileAsync`（Compile 模式）：同样流程，通过 `EmitResult.Diagnostics` 提取诊断。两种模式共用同一套 `IEnumerable<Diagnostic>` 分析逻辑

**关于 Compile 模式超时限制：** `MethodBase.Invoke` 是同步调用，传入的 `CancellationToken` 无法中断正在执行的用户代码。如果用户 `Execute()` 内有死循环，当前架构无法在运行时超时中断。后续版本考虑通过 `Task.Run` + `Wait(timeout)` 提供运行时超时保护，本设计暂不处理（编译阶段超时仍可通过 Roslyn 的 CancellationToken 正常中断）。

**新增 ScriptExecutor 单元测试（`Unit/ScriptExecutorTests.cs`）：**

| 测试 | 场景 |
|------|------|
| `Fallback_SingleMissingUsing_RetriesOnceAndSucceeds` | 缺少 1 个 using，第 1 次失败 → 分析 → 补充 → 第 2 次成功 |
| `Fallback_MultipleMissingAssemblies_AccumulatesReferences` | 验证引用在多次重试间累积而非重置 |
| `Fallback_UnfixableError_NoRetry_ReturnsError` | 语法错误不消耗重试次数，直接返回 |
| `Fallback_ThreeRetriesExhausted_ReturnsError` | 3 次重试耗尽后返回最终错误 |
| `ParseExplicitUsings_ExtractsAllUsingStatements` | 验证从代码中正确提取所有 using 语句 |
| `ParseExplicitUsings_NoUsingStatements_ReturnsDefaults` | 无 using 时代码仍使用默认引用集 |

#### 3.2.6 程序集 Fallback 测试

**单元测试（`CompilationErrorAnalyzerTests.cs`）：**

| 测试 | 场景 |
|------|------|
| `Analyze_CS0012_MissingAssembly_ExtractsAssemblyName` | CS0012 错误 → 正确提取目标程序集名 |
| `Analyze_CS0246_MappingTableHit_ReturnsUsingFix` | 映射表命中 → 返回正确的 using 修复建议 |
| `Analyze_CS0246_MappingTableMiss_AppDomainScanFound` | 映射表未命中 → 兜底扫描在已加载程序集中找到 → 返回引用修复 |
| `Analyze_CS0246_BothMiss_ReturnsCannotFix` | 映射表 + 兜底扫描都找不到 → 标记 CannotFix |
| `Analyze_CS0234_MappingTableHit_ReturnsReferenceFix` | CS0234 映射表命中 → 返回程序集引用 |
| `Analyze_CS0234_AppDomainScanFound_ReturnsReferenceFix` | CS0234 映射表未命中 → 兜底扫描找到 → 返回引用 |
| `Analyze_CS0433_AmbiguousType_IdentifiesConflict` | CS0433 歧义 → 正确识别冲突源 |
| `Analyze_MultipleErrors_DifferentTypes_AllExtracted` | 混合 CS0012 + CS0246 + CS0234 → 全部分类正确 |
| `Analyze_NonImportError_ReturnsCannotFix` | CS1002 语法错误 → 标记为不可修复 |

**集成测试（`ImportFallbackIntegrationTests.cs`，需 Unity Editor）：**

| 测试 | 场景 |
|------|------|
| `Fallback_CS0012_SingleRetry_Succeeds` | CS0012 错误 → 自动添加引用 → 重试成功 |
| `Fallback_CS0246_MissingUsing_SingleRetry_Succeeds` | CS0246 错误 → 补充 using → 重试成功 |
| `Fallback_CS0234_NamespaceNotFound_SingleRetry_Succeeds` | CS0234 错误 → 补全程序集 → 重试成功 |
| `Fallback_CS0433_AmbiguousType_ExcludesConflict_Succeeds` | CS0433 歧义 → 移除冲突引用 → 成功 |
| `Fallback_MultipleErrors_MultiRetry_Succeeds` | 多种错误混合 → 多轮 fallback 逐步修复 |
| `Fallback_ThreeRetriesExhausted_ReturnsError` | 始终无法修复 → 3 次重试耗尽 → 返回含所有尝试记录的错误 |
| `Fallback_UserCodeSyntaxError_NoRetry` | 语法错误 → 不消耗重试次数 → 直接返回 |
| `Fallback_EvalMode_CompilationErrorException_Analyzed` | Eval 模式异常 → 分析 Diagnostics → 补充引用 |
| `Fallback_CompileMode_EmitError_Analyzed` | Compile 模式 EmitResult → 分析 → 补充引用 |
| `Fallback_ScriptMode_ExplicitUsing_Preloaded` | Script 模式代码含 `using System.IO;` → 编译前自动补全引用 → 首次即成功 |
| `Fallback_CompileMode_ExplicitUsing_Preloaded` | Compile 模式同样验证显式 using 预处理 |

---

## 四、涉及文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `Integration/UnityIntegrationTestBase.cs` | 修改 | 消除 TryReadServerPort 重复 |
| `Integration/CompilePipelineIntegrationTests.cs` | 修改 | 修复竞态 + 增强断言 + 新增错误路径 |
| `Integration/ExecPipelineIntegrationTests.cs` | 修改 | 新增失败路径测试 |
| `Integration/GameObjectIntegrationTests.cs` | **新增** | GameObject 场景操作测试 |
| `Integration/ImportFallbackIntegrationTests.cs` | **新增** | 程序集 fallback 集成测试 |
| `Unit/CompilationErrorAnalyzerTests.cs` | **新增** | 错误分析器单元测试 |
| `Unit/ScriptExecutorTests.cs` | **新增** | ScriptExecutor fallback 行为单元测试 |
| `Editor/ScriptExecution/CompilationErrorAnalyzer.cs` | **新增** | 编译错误分析器 |
| `Editor/ScriptExecution/ScriptExecutor.cs` | 修改 | 集成 fallback 重试逻辑 + 扩展默认引用 |

---

## 五、验证方法

1. **单元测试**：`dotnet test` 验证：
   - `CompilationErrorAnalyzer` 的错误分析逻辑（各 CS 错误码识别、映射表查找、混合错误分类）
   - `ScriptExecutor` 的 fallback 行为（重试累积、次数限制、语法错误短路）
2. **集成测试**（在 Unity 2019.4 编辑器中运行）：
   - 现有 16 个测试仍然通过
   - 新增 GameObject 操作测试（Script + Compile 模式，共 10 个）通过
   - 新增 Import fallback 测试（11 个）通过
   - 新增失败路径测试通过
   - 竞态条件修复后测试稳定
3. **手动验证 Import 鲁棒性**：在 Unity Editor 中通过 CLI 执行不写 using 的代码，验证自动 fallback 成功；执行有语法错误的代码，验证不浪费重试直接返回错误
4. **回归检查**：确保 SessionManager 去重、端口验证等已有回归测试仍通过
