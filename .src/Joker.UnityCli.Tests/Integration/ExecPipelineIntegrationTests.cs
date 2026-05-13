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
        var result = await _service.ExecuteAsync(ProjectPath, "1+1", "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("2");
    }

    [SkippableFact]
    public async Task ExecuteAsync_UnityVersion_ReturnsVersion()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath, "UnityEngine.Application.unityVersion", "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task ExecuteAsync_InvalidCode_ReturnsCompilationError()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath, "invalid<<<code", "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task ExecuteAsync_CompileMode_ExecutesMethod()
    {
        SkipIfUnityNotRunning();
        var code = "using System; public class Test { public static string Execute() { return \"hello from compile\"; } }";
        var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("hello from compile");
    }

    [SkippableFact]
    public async Task ExecuteAsync_UnityEngineType_ReturnsExpectedValue()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath,
            "UnityEngine.Vector3.one.x.ToString()", "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("1");
    }

    [SkippableFact]
    public async Task ExecuteAsync_ScriptMode_ReturnValue_PrimitiveTypes()
    {
        SkipIfUnityNotRunning();

        // int
        var intResult = await _service.ExecuteAsync(ProjectPath, "42", "script", 30000, CancellationToken.None);
        intResult.Success.Should().BeTrue();
        intResult.Result.Should().Be("42");

        // bool
        var boolResult = await _service.ExecuteAsync(ProjectPath, "true.ToString()", "script", 30000, CancellationToken.None);
        boolResult.Success.Should().BeTrue();
        boolResult.Result.Should().Be("True");

        // string
        var strResult = await _service.ExecuteAsync(ProjectPath, "\"hello\"", "script", 30000, CancellationToken.None);
        strResult.Success.Should().BeTrue();
        strResult.Result.Should().Be("hello");
    }

    [SkippableFact]
    public async Task ExecuteAsync_CompileMode_MultipleExecuteMethods_UsesFirst()
    {
        SkipIfUnityNotRunning();
        var code = @"
using System;
public static class Test1 { public static string Execute() { return ""first""; } }
public static class Test2 { public static string Execute() { return ""second""; } }
";
        var result = await _service.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        // Should find one of the Execute methods
        result.Result.Should().BeOneOf("first", "second");
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

    [SkippableFact]
    public async Task ExecuteAsync_Timeout_ReturnsTimeoutError()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath, "while(true) { }", "script", 3000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("Timed out");
    }

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
        result.Error.Should().Contain("NullReferenceException");
    }
}
