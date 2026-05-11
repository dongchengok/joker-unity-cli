using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

public class ExecServiceIntegrationTests : UnityIntegrationTestBase
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
    public async Task ExecuteAsync_Timeout_ReturnsTimeoutError()
    {
        SkipIfUnityNotRunning();
        var result = await _service.ExecuteAsync(ProjectPath, "System.Threading.Thread.Sleep(100000)", "script", 3000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Timed out");
    }
}
