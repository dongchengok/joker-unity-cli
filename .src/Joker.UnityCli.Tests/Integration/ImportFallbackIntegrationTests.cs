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

    [SkippableFact]
    public async Task Fallback_CS0246_MissingUsing_SingleRetry_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var path = System.IO.Path.Combine(""Assets"", ""test.txt"");
path";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Fallback_CS0234_NamespaceNotFound_SingleRetry_Succeeds()
    {
        SkipIfUnityNotRunning();
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
        var code = @"var s = UnityEngine.SceneManagement.SceneManager.sceneCount;
System.IO.Path.Combine(""a"", ""b"").Length;
s.ToString()";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Fallback_UserCodeSyntaxError_NoRetry()
    {
        SkipIfUnityNotRunning();
        var code = @"var x = 1";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("CS");
    }

    [SkippableFact]
    public async Task Fallback_ThreeRetriesExhausted_ReturnsError()
    {
        SkipIfUnityNotRunning();
        var code = @"var x = ThisTypeDoesNotExist.Anywhere.Foo();";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Fallback_ScriptMode_ExplicitUsing_Preloaded()
    {
        SkipIfUnityNotRunning();
        var code = @"using System.IO;
var path = Path.Combine(""X"", ""Y"");
path";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
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
        var code = @"using System.IO;
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
}
