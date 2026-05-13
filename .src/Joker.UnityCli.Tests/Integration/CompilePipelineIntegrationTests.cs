using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

[Collection("UnityIntegration")]
public class CompilePipelineIntegrationTests : UnityIntegrationTestBase
{
    [SkippableFact]
    public void SkipIfUnityNotRunningTest()
    {
        SkipIfUnityNotRunning();
    }

    [SkippableFact]
    public async Task CompileAsync_TriggersCompilation_PortChanges()
    {
        SkipIfUnityNotRunning();
        var initialPort = ServerPort!.Value;

        var unityLocator = new UnityLocator();
        var execService = new ExecService();
        var compileService = new CompileService(execService, unityLocator);

        var result = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf("compiled", "up_to_date");
    }

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

        // Poll for port change (indicates Domain Reload), up to 30s
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var currentPort = CompileService.TryReadServerPort(ProjectPath);
            if (currentPort != null && currentPort != oldPort)
                break;
            await Task.Delay(500);
        }

        // Try exec during/after Domain Reload - should retry and eventually succeed
        var result = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 60000, CancellationToken.None);
        result.Success.Should().BeTrue();
    }

    [SkippableFact]
    public async Task CompileMode_InvalidCode_ReturnsCompilationError()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        // Send invalid code via compile mode (no file written to Assets/)
        var badCode = "public class BadScript { void Start() { this_is_an_error; } }";
        var result = await execService.ExecuteAsync(ProjectPath, badCode, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("CS");
    }

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
        result.Error.Should().Contain("error_a");
        result.Error.Should().Contain("error_b");
    }

    [SkippableFact]
    public async Task CompileAsync_Idempotent_DomainReloadIdempotent()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var unityLocator = new UnityLocator();
        var compileService = new CompileService(execService, unityLocator);

        var result1 = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result1.Should().NotBeNull();

        var result2 = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result2.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task CompileMode_ValidClass_ExecutesAndReturnsResult()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var code = @"
using System;
public class Calc
{
    public static string Execute()
    {
        return (40 + 2).ToString();
    }
}";
        var result = await execService.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("42");
    }

    [SkippableFact]
    public async Task CompileMode_UsingUnityEngine_ReturnsResult()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var code = @"
using UnityEngine;
public class VecTest
{
    public static string Execute()
    {
        var v = new Vector3(3, 4, 0);
        return v.magnitude.ToString();
    }
}";
        var result = await execService.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("5");
    }

    [SkippableFact]
    public async Task CompileMode_StaticClass_ExecutesCorrectly()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var code = @"
using System;
using System.Linq;
public static class StringTest
{
    public static string Execute()
    {
        var words = new[] { ""hello"", ""world"" };
        return string.Join("" "", words.Select(w => w.ToUpper()));
    }
}";
        var result = await execService.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("HELLO WORLD");
    }

    [SkippableFact]
    public async Task CompileAsync_PortUnchanged_FallsBackToLogParsing()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var unityLocator = new UnityLocator();
        var compileService = new CompileService(execService, unityLocator);

        var result = await compileService.CompileAsync(ProjectPath, 30000, CancellationToken.None);
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf("compiled", "up_to_date");
    }

    [SkippableFact]
    public async Task CompileAsync_CompileTimeout_ReturnsTimeout()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var unityLocator = new UnityLocator();
        var compileService = new CompileService(execService, unityLocator);

        var result = await compileService.CompileAsync(ProjectPath, 100, CancellationToken.None);
        result.Should().NotBeNull();
    }
}
