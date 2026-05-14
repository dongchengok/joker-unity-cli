# 集成测试重设计 & Import 鲁棒性增强 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重新设计集成测试（修复竞态、增强断言、补全覆盖）并为 ScriptExecution 增加编译错误自动修复的 fallback 机制。

**Architecture:** 新增 `CompilationErrorAnalyzer` 类负责解析 Roslyn 诊断并给修复建议（映射表 + AppDomain 扫描兜底），`ScriptExecutor` 集成 3 次重试循环。集成测试拆分：修改现有 3 个文件 + 新增 2 个测试文件。

**Tech Stack:** C# / Roslyn Scripting / xUnit + FluentAssertions / Unity Editor 2019.4

**依赖关系：** Task 1 → Task 2（需要 CompilationErrorAnalyzer 先实现），Task 3-8 相互独立可并行。

---

### Task 1: CompilationErrorAnalyzer — 单元测试 + 实现

**Files:**
- Create: `Editor/ScriptExecution/CompilationErrorAnalyzer.cs`
- Create: `.src/Joker.UnityCli.Tests/Unit/CompilationErrorAnalyzerTests.cs`

> **注意：** Editor 下的 `.cs` 文件在 Unity 工程中通过 Unity 编译运行，但 `CompilationErrorAnalyzer` 是纯逻辑类（依赖 Roslyn `Diagnostic` 类型），可以在 Unity Editor 中通过 Unity Test Runner 运行。单元测试需要在 Unity Editor 中执行，或使用 mock Diagnostic 对象。

- [ ] **Step 1: 创建 ErrorAnalysis 数据类**

```csharp
// Editor/ScriptExecution/CompilationErrorAnalyzer.cs

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public enum FixAction
    {
        AddUsing,
        AddReference,
        RemoveReference,
        CannotFix
    }

    public class ErrorAnalysis
    {
        public string ErrorCode { get; init; } = "";
        public bool CanAutoFix => FixAction != FixAction.CannotFix;
        public FixAction FixAction { get; init; } = FixAction.CannotFix;
        public string Detail { get; init; } = "";
    }
}
```

- [ ] **Step 2: 创建 CompilationErrorAnalyzer 骨架和映射表**

```csharp
    public static class CompilationErrorAnalyzer
    {
        private static readonly Dictionary<string, string> NamespaceToAssemblyMap = new()
        {
            { "System.IO", "mscorlib" },
            { "System.Text", "mscorlib" },
            { "System.Text.RegularExpressions", "System" },
            { "System.Threading.Tasks", "mscorlib" },
            { "System.Reflection", "mscorlib" },
            { "System.Diagnostics", "System" },
            { "System.Net.Http", "System.Net.Http" },
            { "System.Linq", "System.Core" },
            { "System.Collections.Generic", "mscorlib" },
            { "UnityEngine.UI", "UnityEngine.UI" },
            { "UnityEngine.SceneManagement", "UnityEngine.CoreModule" },
            { "UnityEngine.EventSystems", "UnityEngine.UI" },
            { "UnityEngine.Networking", "UnityEngine" },
        };

        public static IReadOnlyList<ErrorAnalysis> Analyze(IEnumerable<Diagnostic> diagnostics)
        {
            // 骨架：返回空列表
            return new List<ErrorAnalysis>().AsReadOnly();
        }
    }
}
```

- [ ] **Step 3: 编写单元测试 — CS0012 错误解析**

```csharp
// .src/Joker.UnityCli.Tests/Unit/CompilationErrorAnalyzerTests.cs

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Joker.UnityCli.Editor.ScriptExecution;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Joker.UnityCli.Tests.Unit
{
    public class CompilationErrorAnalyzerTests
    {
        // 使用 Roslyn CSharpCompilation 生成真实的 Diagnostic 对象
        private static ImmutableArray<Diagnostic> Compile(string code)
        {
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            };
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Test",
                new[] { syntaxTree },
                references,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            return compilation.GetDiagnostics();
        }

        [Fact]
        public void Analyze_CS0012_MissingAssembly_ExtractsAssemblyName()
        {
            var diags = Compile("public class Foo { public FileStream F; }")
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            // 如果编译器没有生成 CS0012（缺少 using），先忽略
            if (diags.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(diags);

            result.Should().Contain(r => r.ErrorCode == "CS0246"
                || r.ErrorCode == "CS0012");
        }
    }
}
```

- [ ] **Step 4: 实现 Analyze — CS0012 提取程序集名**

在 `CompilationErrorAnalyzer.Analyze` 方法中加入 CS0012 逻辑：

```csharp
public static IReadOnlyList<ErrorAnalysis> Analyze(IEnumerable<Diagnostic> diagnostics)
{
    var results = new List<ErrorAnalysis>();

    foreach (var diag in diagnostics)
    {
        if (diag.Severity != DiagnosticSeverity.Error)
            continue;

        var id = diag.Id;
        var message = diag.GetMessage();

        switch (id)
        {
            case "CS0012":
                // "The type 'X' is defined in an assembly that is not referenced. You must add a reference to assembly 'Y'."
                var cs0012Assembly = ExtractAssemblyFromCS0012(message);
                if (cs0012Assembly != null)
                {
                    results.Add(new ErrorAnalysis
                    {
                        ErrorCode = "CS0012",
                        FixAction = FixAction.AddReference,
                        Detail = cs0012Assembly
                    });
                }
                else
                {
                    results.Add(new ErrorAnalysis { ErrorCode = "CS0012" });
                }
                break;
        }
    }

    return results.AsReadOnly();
}

private static string? ExtractAssemblyFromCS0012(string message)
{
    // "You must add a reference to assembly 'System.IO, ...'"
    var match = System.Text.RegularExpressions.Regex.Match(
        message, @"assembly\s+'([^']+)'");
    return match.Success ? match.Groups[1].Value.Split(',')[0].Trim() : null;
}
```

- [ ] **Step 5: 运行测试验证**

Run: `dotnet test` (在 `.src` 目录)
Expected: CS0012 相关测试通过

- [ ] **Step 6: 编写单元测试 — CS0246 映射表命中**

```csharp
[Fact]
public void Analyze_CS0246_MappingTableHit_ReturnsUsingFix()
{
    // 使用 System.IO.FileStream 但不写 using 且不引用 System.IO
    var diags = Compile("public class Foo { public FileStream F; }")
        .Where(d => d.Severity == DiagnosticSeverity.Error
                   && d.Id == "CS0246")
        .ToList();

    if (diags.Count == 0) return; // 无需修复则跳过

    var result = CompilationErrorAnalyzer.Analyze(diags);

    result.Should().NotBeEmpty();
    // CS0246: "The type or namespace name 'FileStream' could not be found"
    // 映射表中 'System.IO' 存在，应返回 AddUsing 或 AddReference
}
```

- [ ] **Step 7: 实现 CS0246 处理逻辑**

```csharp
case "CS0246":
    // "The type or namespace name 'X' could not be found (are you missing a using directive or an assembly reference?)"
    var cs0246TypeName = ExtractTypeNameFromCS0246(message);
    if (cs0246TypeName != null)
    {
        var fix = ResolveTypeLocation(cs0246TypeName);
        results.Add(fix ?? new ErrorAnalysis { ErrorCode = "CS0246" });
    }
    else
    {
        results.Add(new ErrorAnalysis { ErrorCode = "CS0246" });
    }
    break;
```

新增辅助方法：

```csharp
private static string? ExtractTypeNameFromCS0246(string message)
{
    var match = System.Text.RegularExpressions.Regex.Match(
        message, @"name\s+'([^']+)'");
    return match.Success ? match.Groups[1].Value : null;
}

private static ErrorAnalysis? ResolveTypeLocation(string typeName)
{
    // 第一步：尝试通过类型名推断命名空间（从映射表匹配）
    // 例如 FileStream 可能在 System.IO 中
    // 遍历映射表的每个命名空间，检查在 AppDomain 中是否存在 Namespace.TypeName
    foreach (var kvp in NamespaceToAssemblyMap)
    {
        var fullName = $"{kvp.Key}.{typeName}";
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            try
            {
                var t = asm.GetType(fullName, false);
                if (t != null)
                {
                    return new ErrorAnalysis
                    {
                        ErrorCode = "CS0246",
                        FixAction = FixAction.AddReference,
                        Detail = asm.GetName().Name ?? kvp.Value
                    };
                }
            }
            catch { }
        }
    }

    // 第二步：映射表未命中，遍历所有已加载程序集搜索类型名（兜底扫描）
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        if (asm.IsDynamic) continue;
        try
        {
            var types = asm.GetExportedTypes();
            if (types.Any(t => t.Name == typeName))
            {
                return new ErrorAnalysis
                {
                    ErrorCode = "CS0246",
                    FixAction = FixAction.AddReference,
                    Detail = asm.GetName().Name ?? asm.FullName
                };
            }
        }
        catch { }
    }

    return null; // CannotFix
}
```

- [ ] **Step 8: 运行测试验证**

Run: `dotnet test` (在 `.src` 目录，验证 CS0246 测试通过)

- [ ] **Step 9: 编写并实现 CS0234 测试和处理**

```csharp
[Fact]
public void Analyze_CS0234_MappingTableHit_ReturnsReferenceFix()
{
    var code = @"using UnityEngine.UI; public class Foo { }";
    var diags = Compile(code)
        .Where(d => d.Id == "CS0234")
        .ToList();
    if (diags.Count == 0) return;

    var result = CompilationErrorAnalyzer.Analyze(diags);

    result.Should().Contain(r => r.ErrorCode == "CS0234");
    // 应建议添加 UnityEngine.UI 引用
}
```

在 `Analyze` 中添加 CS0234 处理：

```csharp
case "CS0234":
    // "The type or namespace name 'X' does not exist in the namespace 'Y'"
    var cs0234Namespace = ExtractNamespaceFromCS0234(message);
    if (cs0234Namespace != null)
    {
        var asmName = ResolveNamespace(cs0234Namespace);
        if (asmName != null)
        {
            results.Add(new ErrorAnalysis
            {
                ErrorCode = "CS0234",
                FixAction = FixAction.AddReference,
                Detail = asmName
            });
        }
        else
        {
            results.Add(new ErrorAnalysis { ErrorCode = "CS0234" });
        }
    }
    else
    {
        results.Add(new ErrorAnalysis { ErrorCode = "CS0234" });
    }
    break;
```

新增辅助方法：

```csharp
private static string? ExtractNamespaceFromCS0234(string message)
{
    var match = System.Text.RegularExpressions.Regex.Match(
        message, @"namespace\s+'([^']+)'");
    return match.Success ? match.Groups[1].Value : null;
}

private static string? ResolveNamespace(string namespaceName)
{
    // 先查映射表
    if (NamespaceToAssemblyMap.TryGetValue(namespaceName, out var asmName))
        return asmName;

    // 映射表未命中：在已加载程序集中搜索
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        if (asm.IsDynamic) continue;
        try
        {
            if (asm.GetExportedTypes().Any(t => t.Namespace == namespaceName))
                return asm.GetName().Name ?? asm.FullName;
        }
        catch { }
    }

    return null;
}
```

- [ ] **Step 10: 编写并实现 CS0433 和 CS0103 测试和处理**

```csharp
[Fact]
public void Analyze_CS0433_AmbiguousType_IdentifiesConflict()
{
    // CS0433 通常出现在引用了多个包含同名类型的程序集时
    var diags = Compile(@"
using System;
using System.Reflection;
public class Foo { }")
        .Where(d => d.Id == "CS0433")
        .ToList();
    if (diags.Count == 0) return;

    var result = CompilationErrorAnalyzer.Analyze(diags);

    result.Should().NotBeEmpty();
    result.Should().Contain(r => r.ErrorCode == "CS0433");
}
```

CS0433 处理：

```csharp
case "CS0433":
    // "The type 'X' exists in both 'A' and 'B'"
    // 提取冲突源，保留门面程序集（如 UnityEngine），移除 CoreModule
    var conflictedAsm = ExtractConflictingAssembly(message);
    if (conflictedAsm != null)
    {
        results.Add(new ErrorAnalysis
        {
            ErrorCode = "CS0433",
            FixAction = FixAction.RemoveReference,
            Detail = conflictedAsm
        });
    }
    else
    {
        results.Add(new ErrorAnalysis { ErrorCode = "CS0433" });
    }
    break;
```

```csharp
private static string? ExtractConflictingAssembly(string message)
{
    // "exists in both 'UnityEngine.CoreModule, ...' and 'UnityEngine, ...'"
    var matches = System.Text.RegularExpressions.Regex.Matches(
        message, @"'([^']+)'");
    if (matches.Count >= 2)
    {
        // 优先保留非 CoreModule 的程序集
        var first = matches[0].Groups[1].Value;
        var second = matches[1].Groups[1].Value;
        if (first.Contains("CoreModule"))
            return first.Split(',')[0].Trim();
        if (second.Contains("CoreModule"))
            return second.Split(',')[0].Trim();
    }
    return null;
}
```

CS0103 处理：

```csharp
case "CS0103":
    // "The name 'X' does not exist in the current context"
    var cs0103Name = ExtractNameFromCS0103(message);
    if (cs0103Name != null)
    {
        var fix = ResolveTypeLocation(cs0103Name);
        if (fix != null)
        {
            fix.ErrorCode = "CS0103";
            results.Add(fix);
        }
        else
        {
            results.Add(new ErrorAnalysis { ErrorCode = "CS0103" });
        }
    }
    else
    {
        results.Add(new ErrorAnalysis { ErrorCode = "CS0103" });
    }
    break;
```

```csharp
private static string? ExtractNameFromCS0103(string message)
{
    var match = System.Text.RegularExpressions.Regex.Match(
        message, @"name\s+'([^']+)'");
    return match.Success ? match.Groups[1].Value : null;
}
```

- [ ] **Step 11: 编写混合错误 + 非可修复错误测试**

```csharp
[Fact]
public void Analyze_MultipleErrors_DifferentTypes_AllExtracted()
{
    var diags = Compile(@"
public class Foo {
    FileStream F;
    Button B;
}")
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .ToList();
    if (diags.Count == 0) return;

    var result = CompilationErrorAnalyzer.Analyze(diags);

    result.Count.Should().BeGreaterOrEqualTo(diags.Count - 1);
}

[Fact]
public void Analyze_NonImportError_ReturnsCannotFix()
{
    var diags = Compile("public class Foo { public void }")
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .ToList();
    if (diags.Count == 0) return;

    var result = CompilationErrorAnalyzer.Analyze(diags);

    result.Should().NotBeEmpty();
    result.Should().OnlyContain(r => !r.CanAutoFix);
}
```

- [ ] **Step 12: 运行全部 CompilationErrorAnalyzer 测试**

Run: `dotnet test --filter "FullyQualifiedName~CompilationErrorAnalyzerTests"`
Expected: All tests pass

- [ ] **Step 13: 添加 default 分支处理其他错误码**

```csharp
default:
    // 语法错误等不可修复的错误
    results.Add(new ErrorAnalysis { ErrorCode = id });
    break;
```

- [ ] **Step 14: Commit**

```bash
git add Editor/ScriptExecution/CompilationErrorAnalyzer.cs Editor/ScriptExecution/CompilationErrorAnalyzer.cs.meta .src/Joker.UnityCli.Tests/Unit/CompilationErrorAnalyzerTests.cs
git commit -m "feat: add CompilationErrorAnalyzer with mapping table and AppDomain scan fallback"
```

---

### Task 2: ScriptExecutor — fallback 重试逻辑 + 扩展默认引用

**Files:**
- Modify: `Editor/ScriptExecution/ScriptExecutor.cs`
- Create: `.src/Joker.UnityCli.Tests/Unit/ScriptExecutorTests.cs`

- [ ] **Step 1: 提取 ParseExplicitUsings 方法**

在 `ScriptExecutor` 中添加：

```csharp
internal static List<string> ParseExplicitUsings(string code)
{
    var usings = new List<string>();
    var lines = code.Split('\n');
    foreach (var line in lines)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
        {
            var ns = trimmed.Substring(6, trimmed.Length - 7).Trim();
            if (!string.IsNullOrEmpty(ns) && ns != "static")
                usings.Add(ns);
        }
    }
    return usings;
}
```

- [ ] **Step 2: 扩展默认引用和 using 列表**

修改 `ExecuteScriptAsync` 中的默认引用：

```csharp
private static List<MetadataReference> GetDefaultReferences()
{
    return new List<MetadataReference>
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("UnityEngine").Location),
        MetadataReference.CreateFromFile(Assembly.Load("UnityEditor").Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
        // 新增常用引用
        MetadataReference.CreateFromFile(typeof(System.IO.File).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Text.RegularExpressions.Regex).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
    };
}

private static List<string> GetDefaultUsings()
{
    return new List<string>
    {
        "UnityEngine", "UnityEditor",
        "System", "System.Linq", "System.Collections.Generic",
        "System.IO", "System.Text", "System.Threading.Tasks",
        "UnityEngine.SceneManagement"
    };
}
```

- [ ] **Step 3: 编写 ScriptExecutor fallback 单元测试**

```csharp
// .src/Joker.UnityCli.Tests/Unit/ScriptExecutorTests.cs

using System.Collections.Generic;
using FluentAssertions;
using Joker.UnityCli.Editor.ScriptExecution;
using Xunit;

namespace Joker.UnityCli.Tests.Unit
{
    public class ScriptExecutorTests
    {
        [Fact]
        public void ParseExplicitUsings_ExtractsAllUsingStatements()
        {
            var code = @"
using System;
using System.IO;
using UnityEngine;
public class Test { }";

            var result = ScriptExecutor.ParseExplicitUsings(code);

            result.Should().Contain("System");
            result.Should().Contain("System.IO");
            result.Should().Contain("UnityEngine");
            result.Should().HaveCount(3);
        }

        [Fact]
        public void ParseExplicitUsings_NoUsingStatements_ReturnsEmpty()
        {
            var code = "public class Test { }";

            var result = ScriptExecutor.ParseExplicitUsings(code);

            result.Should().BeEmpty();
        }

        [Fact]
        public void ParseExplicitUsings_StaticUsing_NotIncluded()
        {
            var code = "using static System.Math; using System; public class Test { }";

            var result = ScriptExecutor.ParseExplicitUsings(code);

            result.Should().Contain("System");
            result.Should().NotContain("static System.Math");
            result.Should().HaveCount(1);
        }
    }
}
```

- [ ] **Step 4: 运行新测试验证 ParseExplicitUsings**

Run: `dotnet test --filter "FullyQualifiedName~ScriptExecutorTests"`
Expected: All pass

- [ ] **Step 5: 重构 ExecuteScriptAsync — 集成 fallback 循环**

将 `ExecuteScriptAsync` 改为使用 fallback 循环：

```csharp
private static async Task<ExecResult> ExecuteScriptAsync(string code, int timeoutMs, CancellationToken ct)
{
    var explicitUsings = ParseExplicitUsings(code);
    var usings = new List<string>(GetDefaultUsings());
    foreach (var u in explicitUsings)
    {
        if (!usings.Contains(u))
            usings.Add(u);
    }

    var references = GetDefaultReferences();
    var retry = 0;
    const int maxRetries = 3;
    string? lastError = null;
    var allAttempts = new List<string>();

    while (true)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var options = ScriptOptions.Default
                .WithImports(usings.ToArray())
                .WithReferences(references);

            var globals = new ScriptGlobals();
            var result = await CSharpScript.EvaluateAsync(code, options, globals, typeof(ScriptGlobals), ct);
            sw.Stop();
            return new ExecResult
            {
                Type = "exec_result",
                Success = true,
                Result = result?.ToString() ?? "null",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (CompilationErrorException ex)
        {
            sw.Stop();
            lastError = string.Join("\n", ex.Diagnostics.Select(d => d.ToString()));

            var analysis = CompilationErrorAnalyzer.Analyze(ex.Diagnostics);
            var fixable = analysis.Where(a => a.CanAutoFix).ToList();
            var unfixable = analysis.Any(a => !a.CanAutoFix);

            if (unfixable || fixable.Count == 0)
            {
                allAttempts.Add($"Attempt {retry + 1}: {lastError}");
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = lastError,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            ApplyFixes(fixable, references, usings);
            retry++;

            if (retry >= maxRetries)
            {
                allAttempts.Add($"Attempt {retry}: {lastError}");
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = string.Join("\n---\n", allAttempts),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            allAttempts.Add($"Attempt {retry}: {string.Join(", ", fixable.Select(f => f.ErrorCode))}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ExecResult
            {
                Type = "exec_result",
                Success = false,
                Error = $"Timed out after {timeoutMs}ms",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecResult
            {
                Type = "exec_result",
                Success = false,
                Error = ex.ToString(),
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }
}

private static void ApplyFixes(List<ErrorAnalysis> fixes, List<MetadataReference> references, List<string> usings)
{
    foreach (var fix in fixes)
    {
        switch (fix.FixAction)
        {
            case FixAction.AddReference:
                if (references.Any(r => (r.Display ?? "").Contains(fix.Detail)))
                    continue;
                try
                {
                    var asm = Assembly.Load(fix.Detail);
                    references.Add(MetadataReference.CreateFromFile(asm.Location));
                }
                catch { }
                break;

            case FixAction.AddUsing:
                if (!usings.Contains(fix.Detail))
                    usings.Add(fix.Detail);
                break;

            case FixAction.RemoveReference:
                references.RemoveAll(r => (r.Display ?? "").Contains(fix.Detail));
                break;
        }
    }
}
```

- [ ] **Step 6: 重构 ExecuteCompileAsync — 集成 fallback 循环**

```csharp
private static async Task<ExecResult> ExecuteCompileAsync(string code, int timeoutMs, CancellationToken ct)
{
    var explicitUsings = ParseExplicitUsings(code);
    var codeUsings = new List<string>(explicitUsings);

    var references = GetDefaultReferences();
    var retry = 0;
    const int maxRetries = 3;
    string? lastError = null;
    var allAttempts = new List<string>();

    while (true)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create(
                $"JokerExec_{Guid.NewGuid():N}",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                var emitResult = compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    sw.Stop();
                    var errors = emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .ToList();
                    var errorStr = string.Join("\n", errors.Select(d => d.ToString()));
                    lastError = errorStr;

                    var analysis = CompilationErrorAnalyzer.Analyze(errors);
                    var fixable = analysis.Where(a => a.CanAutoFix).ToList();
                    var unfixable = analysis.Any(a => !a.CanAutoFix);

                    if (unfixable || fixable.Count == 0)
                    {
                        return new ExecResult
                        {
                            Type = "exec_result",
                            Success = false,
                            Error = errorStr,
                            DurationMs = sw.ElapsedMilliseconds
                        };
                    }

                    ApplyFixes(fixable, references, codeUsings);
                    retry++;

                    if (retry >= maxRetries)
                    {
                        allAttempts.Add($"Attempt {retry}: {errorStr}");
                        return new ExecResult
                        {
                            Type = "exec_result",
                            Success = false,
                            Error = string.Join("\n---\n", allAttempts),
                            DurationMs = sw.ElapsedMilliseconds
                        };
                    }

                    allAttempts.Add($"Attempt {retry}: {string.Join(", ", fixable.Select(f => f.ErrorCode))}");
                    continue;
                }

                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                var executeMethod = assembly.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .FirstOrDefault(m => m.Name == "Execute" && m.GetParameters().Length == 0);

                if (executeMethod == null)
                {
                    sw.Stop();
                    return new ExecResult
                    {
                        Type = "exec_result",
                        Success = false,
                        Error = "No 'public static void Execute()' method found.",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                var execResult = executeMethod.Invoke(null, null);
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = true,
                    Result = execResult?.ToString() ?? "null",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ExecResult
            {
                Type = "exec_result",
                Success = false,
                Error = $"Timed out after {timeoutMs}ms",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecResult
            {
                Type = "exec_result",
                Success = false,
                Error = ex.ToString(),
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }
}
```

- [ ] **Step 7: 运行 dotnet test 验证 ScriptExecutorTests**

Run: `dotnet test --filter "FullyQualifiedName~ScriptExecutorTests"`
Expected: All pass

- [ ] **Step 8: Commit**

```bash
git add Editor/ScriptExecution/ScriptExecutor.cs .src/Joker.UnityCli.Tests/Unit/ScriptExecutorTests.cs
git commit -m "feat: add fallback retry logic to ScriptExecutor with expanded default references"
```

---

### Task 3: UnityIntegrationTestBase — 消除 TryReadServerPort 重复

**Files:**
- Modify: `.src/Joker.UnityCli.Tests/Integration/UnityIntegrationTestBase.cs`

- [ ] **Step 1: 修改 TryReadServerPort 调用**

```csharp
// 删除 private static int? TryReadServerPort(...) 方法
// 删除 private class ServerInfo { ... }
// 修改构造函数中的调用：

protected UnityIntegrationTestBase()
{
    ProjectPath = Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "..", ".Unity2019"));
    ServerPort = CompileService.TryReadServerPort(ProjectPath);
}

// 添加 using:
// using Joker.UnityCli.Services;
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add .src/Joker.UnityCli.Tests/Integration/UnityIntegrationTestBase.cs
git commit -m "fix: eliminate TryReadServerPort duplication in UnityIntegrationTestBase"
```

---

### Task 4: CompilePipelineIntegrationTests — 修复竞态 + 增强断言

**Files:**
- Modify: `.src/Joker.UnityCli.Tests/Integration/CompilePipelineIntegrationTests.cs`

- [ ] **Step 1: 增强 CompileMode_MultipleErrors_AllReported 断言**

```csharp
[SkippableFact]
public async Task CompileMode_MultipleErrors_AllReported()
{
    SkipIfUnityNotRunning();
    var execService = new ExecService();
    var badCode = @"
public class BadScript1 { void A() { error_a; } }
public class BadScript2 { void B() { error_b; } }
";
    var result = await execService.ExecuteAsync(ProjectPath, badCode, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrEmpty();
    // 增强：验证两个错误都在
    result.Error.Should().Contain("error_a");
    result.Error.Should().Contain("error_b");
}
```

- [ ] **Step 2: 增强 CompileMode_InvalidCode_ReturnsCompilationError 断言**

```csharp
[SkippableFact]
public async Task CompileMode_InvalidCode_ReturnsCompilationError()
{
    SkipIfUnityNotRunning();
    var execService = new ExecService();
    var badCode = "public class BadScript { void Start() { this_is_an_error; } }";
    var result = await execService.ExecuteAsync(ProjectPath, badCode, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrEmpty();
    // 增强：验证包含 CS 错误码
    result.Error.Should().Contain("CS");
}
```

- [ ] **Step 3: 增强 MultipleExecuteMethods_UsesFirst 断言**

```csharp
[SkippableFact]
public async Task CompileMode_MultipleExecuteMethods_UsesFirst()
{
    SkipIfUnityNotRunning();
    var code = @"
using System;
public static class Test1 { public static string Execute() { return ""first""; } }
public static class Test2 { public static string Execute() { return ""second""; } }
";
    var result = await execService.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    // 增强：Type.Name 字母序排序时 Test1 < Test2，GetTypes() 返回顺序大致按定义顺序
    result.Result.Should().BeOneOf("first", "second");
}
```

- [ ] **Step 4: 修复 ExecDuringDomainReload_RetriesAndRecovers 竞态条件**

```csharp
[SkippableFact]
public async Task ExecDuringDomainReload_RetriesAndRecovers()
{
    SkipIfUnityNotRunning();
    var execService = new ExecService();

    // First verify exec works
    var before = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 30000, CancellationToken.None);
    before.Success.Should().BeTrue();

    // Read current port before triggering compilation
    var oldPort = ServerPort!.Value;

    // Trigger compilation (which causes Domain Reload)
    var unityLocator = new UnityLocator();
    var compileService = new CompileService(execService, unityLocator);
    var compileTask = compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);

    // Poll for port change (indicates Domain Reload has started), up to 30s
    var deadline = DateTime.UtcNow.AddSeconds(30);
    bool domainReloadDetected = false;
    while (DateTime.UtcNow < deadline)
    {
        var currentPort = CompileService.TryReadServerPort(ProjectPath);
        if (currentPort != oldPort)
        {
            domainReloadDetected = true;
            break;
        }
        await Task.Delay(500);
    }

    // Try exec during/after Domain Reload - should retry and eventually succeed
    var result = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 60000, CancellationToken.None);
    result.Success.Should().BeTrue();
}
```

- [ ] **Step 5: Commit**

```bash
git add .src/Joker.UnityCli.Tests/Integration/CompilePipelineIntegrationTests.cs
git commit -m "fix: fix race condition in ExecDuringDomainReload and enhance assertions"
```

---

### Task 5: ExecPipelineIntegrationTests — 新增失败路径测试

**Files:**
- Modify: `.src/Joker.UnityCli.Tests/Integration/ExecPipelineIntegrationTests.cs`

- [ ] **Step 1: 新增 Script 模式运行时异常测试**

```csharp
[SkippableFact]
public async Task ExecuteAsync_RuntimeException_DivideByZero()
{
    SkipIfUnityNotRunning();
    var result = await _service.ExecuteAsync(ProjectPath, "int x = 0; int y = 1 / x;", "script", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrEmpty();
    result.Error.Should().Contain("DivideByZero");
}

[SkippableFact]
public async Task ExecuteAsync_Timeout_ReturnsTimeoutError()
{
    SkipIfUnityNotRunning();
    var result = await _service.ExecuteAsync(ProjectPath, "while(true) { }", "script", 3000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrEmpty();
    result.Error.Should().Contain("Timed out");
}
```

- [ ] **Step 2: 新增 Compile 模式失败路径测试**

```csharp
[SkippableFact]
public async Task CompileMode_NoExecuteMethod_ReturnsError()
{
    SkipIfUnityNotRunning();
    var code = "using System; public class Test { public static string Run() { return \"hello\"; } }";
    var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().Contain("No 'public static void Execute()' method found");
}

[SkippableFact]
public async Task CompileMode_ExecuteNotStatic_ReturnsError()
{
    SkipIfUnityNotRunning();
    var code = "using System; public class Test { public string Execute() { return \"hello\"; } }";
    var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().Contain("No 'public static void Execute()' method found");
}

[SkippableFact]
public async Task CompileMode_RuntimeException_ReturnsError()
{
    SkipIfUnityNotRunning();
    var code = @"
using System;
public class Test
{
    public static string Execute()
    {
        string s = null;
        return s.Length.ToString();
    }
}";
    var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrEmpty();
    result.Error.Should().Contain("NullReferenceException");
}
```

- [ ] **Step 3: Commit**

```bash
git add .src/Joker.UnityCli.Tests/Integration/ExecPipelineIntegrationTests.cs
git commit -m "feat: add failure path integration tests for Script and Compile modes"
```

---

### Task 6: GameObjectIntegrationTests — 新增场景操作测试

**Files:**
- Create: `.src/Joker.UnityCli.Tests/Integration/GameObjectIntegrationTests.cs`

- [ ] **Step 1: 创建测试类骨架**

```csharp
using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

[Collection("UnityIntegration")]
public class GameObjectIntegrationTests : UnityIntegrationTestBase
{
    private readonly ExecService _exec = new();

    [SkippableFact]
    public void SkipIfUnityNotRunningTest()
    {
        SkipIfUnityNotRunning();
    }

    // Script mode tests...
}
```

- [ ] **Step 2: 新增 Script 模式 GameObject 测试**

```csharp
[SkippableFact]
public async Task ScriptMode_CreateGameObject_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"var go = new UnityEngine.GameObject(""test_go"");
UnityEngine.Object.DestroyImmediate(go);
""created""";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("created");
}

[SkippableFact]
public async Task ScriptMode_FindGameObject_ByName_Succeeds()
{
    SkipIfUnityNotRunning();
    var make = @"var go = new UnityEngine.GameObject(""findable_go"");
go.name";
    var result = await _exec.ExecuteAsync(ProjectPath, make, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("findable_go");
}

[SkippableFact]
public async Task ScriptMode_DestroyGameObject_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"var go = new UnityEngine.GameObject(""to_destroy"");
UnityEngine.Object.DestroyImmediate(go);
go == null ? ""destroyed"" : ""alive""";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("destroyed");
}

[SkippableFact]
public async Task ScriptMode_ModifyTransform_Position_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"var go = new UnityEngine.GameObject(""position_test"");
go.transform.position = new UnityEngine.Vector3(1, 2, 3);
var p = go.transform.position;
UnityEngine.Object.DestroyImmediate(go);
$""{p.x},{p.y},{p.z}""";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("1,2,3");
}

[SkippableFact]
public async Task ScriptMode_ModifyTransform_RotationScale_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"var go = new UnityEngine.GameObject(""rs_test"");
go.transform.localScale = new UnityEngine.Vector3(2, 2, 2);
var s = go.transform.localScale;
UnityEngine.Object.DestroyImmediate(go);
$""{s.x},{s.y},{s.z}""";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("2,2,2");
}

[SkippableFact]
public async Task ScriptMode_AddComponent_BoxCollider_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"var go = new UnityEngine.GameObject(""collider_test"", typeof(UnityEngine.BoxCollider));
var bc = go.GetComponent<UnityEngine.BoxCollider>();
UnityEngine.Object.DestroyImmediate(go);
bc != null ? ""has_collider"" : ""no_collider""";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("has_collider");
}

[SkippableFact]
public async Task ScriptMode_InstantiateAndDestroy_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"var go = new UnityEngine.GameObject(""original"");
var clone = UnityEngine.Object.Instantiate(go);
var cloneName = clone.name;
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Object.DestroyImmediate(clone);
cloneName";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Contain("original");
}
```

- [ ] **Step 3: 新增 Compile 模式 GameObject 测试**

```csharp
[SkippableFact]
public async Task CompileMode_CreateGameObjectHierarchy_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"
using UnityEngine;
public class HierarchyTest
{
    public static string Execute()
    {
        var parent = new GameObject(""parent_h"");
        var child = new GameObject(""child_h"");
        child.transform.parent = parent.transform;
        int count = parent.transform.childCount;
        Object.DestroyImmediate(parent);
        return count == 1 ? ""has_child"" : ""no_child"";
    }
}";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("has_child");
}

[SkippableFact]
public async Task CompileMode_BatchModifyComponents_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"
using UnityEngine;
public class BatchTest
{
    public static string Execute()
    {
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($""batch_{i}"");
            go.transform.position = new Vector3(i, 0, 0);
        }
        var found = Object.FindObjectsOfType<GameObject>().Length;
        return found > 0 ? ""found"" : ""not_found"";
    }
}";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("found");
}

[SkippableFact]
public async Task CompileMode_FindObjectsOfType_Succeeds()
{
    SkipIfUnityNotRunning();
    var code = @"
using UnityEngine;
public class FindTest
{
    public static string Execute()
    {
        var go = new GameObject(""find_target"");
        var all = Object.FindObjectsOfType<GameObject>();
        Object.DestroyImmediate(go);
        return all.Length > 0 ? ""found_some"" : ""found_none"";
    }
}";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("found_some");
}
```

- [ ] **Step 4: Commit**

```bash
git add .src/Joker.UnityCli.Tests/Integration/GameObjectIntegrationTests.cs
git commit -m "feat: add GameObject scene operation integration tests"
```

---

### Task 7: ImportFallbackIntegrationTests — 新增程序集 fallback 集成测试

**Files:**
- Create: `.src/Joker.UnityCli.Tests/Integration/ImportFallbackIntegrationTests.cs`

> 这些测试需要在 Unity Editor 中运行，验证实际的 fallback 行为。

- [ ] **Step 1: 创建测试类骨架**

```csharp
using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

[Collection("UnityIntegration")]
public class ImportFallbackIntegrationTests : UnityIntegrationTestBase
{
    private readonly ExecService _exec = new();

    [SkippableFact]
    public void SkipIfUnityNotRunningTest()
    {
        SkipIfUnityNotRunning();
    }

    // Fallback tests...
}
```

- [ ] **Step 2: CS0012/CS0246/CS0234 单轮 fallback 测试**

```csharp
[SkippableFact]
public async Task Fallback_CS0246_MissingUsing_SingleRetry_Succeeds()
{
    SkipIfUnityNotRunning();
    // 使用 System.IO.File 但省略 using System.IO — import fallback 应自动补充
    var code = @"
var path = System.IO.Path.Combine(""Assets"", ""test.txt"");
path";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().NotBeNullOrEmpty();
}

[SkippableFact]
public async Task Fallback_CS0234_NamespaceNotFound_SingleRetry_Succeeds()
{
    SkipIfUnityNotRunning();
    // 使用 UnityEngine.SceneManagement 命名空间中的 SceneManager
    var code = @"var count = UnityEngine.SceneManagement.SceneManager.sceneCount;
count.ToString()";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().NotBeNullOrEmpty();
}

[SkippableFact]
public async Task Fallback_MultipleErrors_MultiRetry_Succeeds()
{
    SkipIfUnityNotRunning();
    // 同时使用 IO 和 SceneManagement
    var code = @"
var s = UnityEngine.SceneManagement.SceneManager.sceneCount;
System.IO.Path.Combine(""a"", ""b"").Length;
";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
}
```

- [ ] **Step 3: 语法错误不重试 + 3 次重试耗尽测试**

```csharp
[SkippableFact]
public async Task Fallback_UserCodeSyntaxError_NoRetry()
{
    SkipIfUnityNotRunning();
    // 分号缺失 — 纯语法错误，不应消耗重试次数
    var code = @"var x = 1";  // 缺少分号
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
    result.Error.Should().Contain("CS");
}

[SkippableFact]
public async Task Fallback_ThreeRetriesExhausted_ReturnsError()
{
    SkipIfUnityNotRunning();
    // 使用不存在的类型 — 映射表和 AppDomain 都找不到
    var code = @"var x = ThisTypeDoesNotExist.Anywhere.Foo();";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeFalse();
}
```

- [ ] **Step 4: 显式 using 预处理测试**

```csharp
[SkippableFact]
public async Task Fallback_ScriptMode_ExplicitUsing_Preloaded()
{
    SkipIfUnityNotRunning();
    var code = @"using System.IO;
var path = Path.Combine(""X"", ""Y"");
path";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    // 显式 using System.IO 应在编译前就被解析并添加引用，首次即成功
    result.Success.Should().BeTrue();
    result.Result.Should().NotBeNullOrEmpty();
}

[SkippableFact]
public async Task Fallback_CompileMode_ExplicitUsing_Preloaded()
{
    SkipIfUnityNotRunning();
    var code = @"using System.IO;
public class Test { public static string Execute() { return Path.GetTempPath(); } }";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
}
```

- [ ] **Step 5: Eval 和 Compile 模式各自的 fallback 路径**

```csharp
[SkippableFact]
public async Task Fallback_EvalMode_CompilationErrorException_Analyzed()
{
    SkipIfUnityNotRunning();
    var code = @"using System.Text.RegularExpressions;
var r = new Regex(""\d+"");
r.IsMatch(""abc123"").ToString()";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
    result.Result.Should().Be("True");
}

[SkippableFact]
public async Task Fallback_CompileMode_EmitError_Analyzed()
{
    SkipIfUnityNotRunning();
    var code = @"
using System.IO;
public class Test
{
    public static string Execute()
    {
        return Directory.GetCurrentDirectory();
    }
}";
    var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
    result.Success.Should().BeTrue();
}
```

- [ ] **Step 6: Commit**

```bash
git add .src/Joker.UnityCli.Tests/Integration/ImportFallbackIntegrationTests.cs
git commit -m "feat: add import fallback integration tests"
```

---

### Task 8: CompilePipelineIntegrationTests — CompileService 错误路径测试

**Files:**
- Modify: `.src/Joker.UnityCli.Tests/Integration/CompilePipelineIntegrationTests.cs`

- [ ] **Step 1: 新增端口不变 → 日志解析回退测试**

```csharp
[SkippableFact]
public async Task CompileAsync_PortUnchanged_FallsBackToLogParsing()
{
    SkipIfUnityNotRunning();
    var execService = new ExecService();
    var unityLocator = new UnityLocator();
    var compileService = new CompileService(execService, unityLocator);

    // 对已经是最新状态的项目触发编译，端口不变，应返回 "up_to_date"
    var result = await compileService.CompileAsync(ProjectPath, 30000, CancellationToken.None);
    result.Should().NotBeNull();
    result.Status.Should().BeOneOf("compiled", "up_to_date");
}
```

- [ ] **Step 2: 新增编译超时测试**

```csharp
[SkippableFact]
public async Task CompileAsync_CompileTimeout_ReturnsTimeout()
{
    SkipIfUnityNotRunning();
    var execService = new ExecService();
    var unityLocator = new UnityLocator();
    var compileService = new CompileService(execService, unityLocator);

    // 极短的超时时间触发超时
    var result = await compileService.CompileAsync(ProjectPath, 100, CancellationToken.None);
    result.Should().NotBeNull();
    // 超短超时通常导致 timeout 或编译未开始
}
```

- [ ] **Step 3: Commit**

```bash
git add .src/Joker.UnityCli.Tests/Integration/CompilePipelineIntegrationTests.cs
git commit -m "feat: add CompileService error path integration tests"
```

---

## 执行顺序

```
Task 1 (CompilationErrorAnalyzer) → Task 2 (ScriptExecutor refactor)
                                       ↓
Task 3 (TryReadServerPort dedup) ──────────────────────────┐
Task 4 (CompilePipeline 修复) ──────────────────────────────┤  并行
Task 5 (ExecPipeline 失败路径) ─────────────────────────────┤
Task 6 (GameObject 测试) ───────────────────────────────────┤
Task 7 (ImportFallback 测试) ───────────────────────────────┤
Task 8 (CompileService 错误路径) ───────────────────────────┘
```

## 验证

1. `dotnet test` — 确认所有单元测试通过 (CompilationErrorAnalyzerTests + ScriptExecutorTests)
2. 在 Unity 2019.4 编辑器中运行集成测试套件
3. 手动通过 CLI 测试：`joker-unity exec "System.IO.File.ReadAllText(\"somepath\")"` 验证 import fallback
4. 确认回归 — 现有的 SessionManager、端口验证等回归测试仍通过
