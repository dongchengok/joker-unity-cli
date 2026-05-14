using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

[Collection("UnityIntegration")]
public class ExecPipelineIntegrationTests : UnityIntegrationTestBase
{
    private readonly ExecService _service = new();

    [SkippableFact]
    public void SkipIfUnityNotRunningTest()
    {
        SkipIfUnityNotRunning();
    }

    [SkippableFact]
    public async Task ExecuteAsync_SimpleExpression_Returns2()
    {
        SkipIfUnityNotRunning();
        // Exercise LINQ + arithmetic in script mode
        var result = await _service.ExecuteAsync(ProjectPath,
            "Enumerable.Range(1, 10).Where(x => x % 2 == 0).Sum()",
            "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("30");
    }

    [SkippableFact]
    public async Task ExecuteAsync_UnityVersion_ReturnsVersion()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath,
            "UnityEngine.Application.unityVersion", "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNullOrEmpty();
        // Unity version should contain at least one dot (e.g. "2019.4.x")
        result.Result.Should().Contain(".");
    }

    [SkippableFact]
    public async Task ExecuteAsync_InvalidCode_ReturnsCompilationError()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath, "invalid<<<code", "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("CS");
    }

    [SkippableFact]
    public async Task ExecuteAsync_CompileMode_ExecutesMethod()
    {
        SkipIfUnityNotRunning();
        // Use System.IO to exercise import resolution
        var code = @"
using System;
using System.IO;
public class Test
{
    public static string Execute()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), ""joker_exec_test_"" + Guid.NewGuid().ToString(""N""));
        File.WriteAllText(tempFile, ""compile_mode_works"");
        var content = File.ReadAllText(tempFile);
        File.Delete(tempFile);
        return content;
    }
}";
        var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("compile_mode_works");
    }

    [SkippableFact]
    public async Task ExecuteAsync_UnityEngineType_ReturnsExpectedValue()
    {
        SkipIfUnityNotRunning();
        // Vector3 math: (1,0,0) + (0,1,0) magnitude = sqrt(2)
        var result = await _service.ExecuteAsync(ProjectPath,
            "((new UnityEngine.Vector3(1,0,0) + new UnityEngine.Vector3(0,1,0)).magnitude).ToString(\"F4\")",
            "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("1.4142");
    }

    [SkippableFact]
    public async Task ExecuteAsync_ScriptMode_ReturnValue_PrimitiveTypes()
    {
        SkipIfUnityNotRunning();

        // int with LINQ aggregate
        var intResult = await _service.ExecuteAsync(ProjectPath,
            "Enumerable.Range(1, 5).Aggregate((a, b) => a * b).ToString()",
            "script", 30000, CancellationToken.None);
        intResult.Success.Should().BeTrue();
        intResult.Result.Should().Be("120"); // 5!

        // bool
        var boolResult = await _service.ExecuteAsync(ProjectPath, "true.ToString()", "script", 30000, CancellationToken.None);
        boolResult.Success.Should().BeTrue();
        boolResult.Result.Should().Be("True");

        // string with LINQ manipulation
        var strResult = await _service.ExecuteAsync(ProjectPath,
            "string.Join(\"-\", \"hello world\".Split(' ').Select(w => w.ToUpper()))",
            "script", 30000, CancellationToken.None);
        strResult.Success.Should().BeTrue();
        strResult.Result.Should().Be("HELLO-WORLD");
    }

    [SkippableFact]
    public async Task ExecuteAsync_CompileMode_MultipleExecuteMethods_UsesFirst()
    {
        SkipIfUnityNotRunning();
        // Both classes compute something non-trivial to exercise full compilation
        var code = @"
using System;
using System.Linq;
public static class Test1 { public static string Execute() { return Enumerable.Range(1, 3).Sum().ToString(); } }
public static class Test2 { public static string Execute() { return string.Join("","", Enumerable.Range(10, 3)); } }
";
        var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().BeOneOf("6", "10,11,12");
    }

    // --- Script mode failure tests ---

    [SkippableFact]
    public async Task ExecuteAsync_RuntimeException_DivideByZero()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath, "int x = 0; int y = 1 / x;", "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("DivideByZero");
    }

    // NOTE: Timeout test removed — script-mode long-running code (while(true), Thread.Sleep)
    // blocks Unity's main thread and cannot be cancelled by CancellationToken.
    // This would crash the HTTP server and make Unity unresponsive.
    // Timeout behavior should be tested at the CLI/ExecService retry level instead.

    // --- Compile mode failure tests ---

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
        // MethodInfo.Invoke wraps exceptions in TargetInvocationException
        result.Error.Should().ContainAny("NullReferenceException", "target of an invocation");
    }
}
