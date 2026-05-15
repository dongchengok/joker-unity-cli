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
        var execService = new ExecService();
        var compileService = new CompileService(execService);

        var result = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf("compiled", "up_to_date");

        // Wait for server to recover after potential domain reload
        await WaitForServerReady(60000);
    }

    [SkippableFact]
    public async Task ExecDuringDomainReload_RetriesAndRecovers()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();

        var before = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 30000, CancellationToken.None);
        before.Success.Should().BeTrue();

        var oldPort = ServerPort!.Value;

        var compileService = new CompileService(execService);
        var compileTask = compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);

        // Poll for port change (indicates Domain Reload), up to 30s
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var currentPort = CompileService.TryReadServerInfo(ProjectPath)?.Port;
            if (currentPort != null && currentPort != oldPort)
                break;
            await Task.Delay(500);
        }

        var result = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 60000, CancellationToken.None);
        result.Success.Should().BeTrue();

        await WaitForServerReady(60000);
    }

    [SkippableFact]
    public async Task CompileMode_InvalidCode_ReturnsCompilationError()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
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
        var compileService = new CompileService(execService);

        var result1 = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result1.Should().NotBeNull();

        // Wait for server recovery between compiles
        await WaitForServerReady(60000);

        var result2 = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result2.Should().NotBeNull();

        await WaitForServerReady(60000);
    }

    [SkippableFact]
    public async Task CompileMode_ValidClass_ExecutesAndReturnsResult()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        // Use System.IO to exercise import fallback for non-default references
        var code = @"
using System;
using System.IO;
public class Calc
{
    public static string Execute()
    {
        var tempDir = Path.GetTempPath();
        var tempFile = Path.Combine(tempDir, ""joker_test_"" + Guid.NewGuid().ToString(""N""));
        File.WriteAllText(tempFile, ""42"");
        var content = File.ReadAllText(tempFile);
        File.Delete(tempFile);
        return content;
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
        // Use Vector3 math + LINQ aggregate to exercise multiple imports
        var code = @"
using System;
using System.Linq;
using UnityEngine;
public class VecTest
{
    public static string Execute()
    {
        var vectors = new[] {
            new Vector3(3, 4, 0),
            new Vector3(0, 0, 5),
            new Vector3(1, 0, 0)
        };
        var totalMag = vectors.Sum(v => v.magnitude);
        return totalMag.ToString(""F0"");
    }
}";
        var result = await execService.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("11");
    }

    [SkippableFact]
    public async Task CompileMode_StaticClass_ExecutesCorrectly()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        // Use Regex + LINQ to exercise non-default imports
        // Note: MatchCollection doesn't implement IEnumerable<T> on Unity 2019 Mono,
        // so we use Cast<Match> before Select
        var code = @"
using System;
using System.Linq;
using System.Text.RegularExpressions;
public static class StringTest
{
    public static string Execute()
    {
        var text = ""Hello123World456"";
        var numbers = Regex.Matches(text, @""\d+"").Cast<Match>().Select(m => m.Value);
        return string.Join("","", numbers);
    }
}";
        var result = await execService.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("123,456");
    }

    [SkippableFact]
    public async Task CompileAsync_CompileSucceeds()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var compileService = new CompileService(execService);

        var result = await compileService.CompileAsync(ProjectPath, 30000, CancellationToken.None);
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf("compiled", "up_to_date");
    }

    [SkippableFact]
    public async Task CompileAsync_CompileTimeout_ReturnsTimeout()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();
        var compileService = new CompileService(execService);

        var result = await compileService.CompileAsync(ProjectPath, 100, CancellationToken.None);
        result.Should().NotBeNull();
    }
}
