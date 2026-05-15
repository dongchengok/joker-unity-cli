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
        // Use System.IO.Path without explicit using — fallback should add reference
        var code = @"var path = System.IO.Path.Combine(""Assets"", ""test.txt"");
path";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Contain("Assets").And.Contain("test.txt");
    }

    [SkippableFact]
    public async Task Fallback_CS0234_NamespaceNotFound_SingleRetry_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Access SceneManagement without explicit using — fallback resolves namespace
        var code = @"var count = UnityEngine.SceneManagement.SceneManager.sceneCount;
count.ToString()";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        int.Parse(result.Result).Should().BeGreaterThanOrEqualTo(0);
    }

    [SkippableFact]
    public async Task Fallback_MultipleErrors_MultiRetry_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Combine SceneManagement + System.IO in one snippet — both need fallback resolution
        var code = @"var s = UnityEngine.SceneManagement.SceneManager.sceneCount;
var path = System.IO.Path.GetTempPath();
$""{s}:{path}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Contain(":");
    }

    [SkippableFact]
    public async Task Fallback_UserCodeSyntaxError_NoRetry()
    {
        SkipIfUnityNotRunning();
        // Missing semicolon — cannot be auto-fixed
        var code = @"var x = 1";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("CS");
    }

    [SkippableFact]
    public async Task Fallback_ThreeRetriesExhausted_ReturnsError()
    {
        SkipIfUnityNotRunning();
        // Completely unknown type — fallback cannot resolve
        var code = @"var x = ThisTypeDoesNotExist.Anywhere.Foo();";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Fallback_ScriptMode_ExplicitUsing_Preloaded()
    {
        SkipIfUnityNotRunning();
        // Explicit using System.IO should preload the reference
        var code = @"using System.IO;
var path = Path.Combine(""X"", ""Y"");
path";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Contain("X").And.Contain("Y");
    }

    [SkippableFact]
    public async Task Fallback_CompileMode_ExplicitUsing_Preloaded()
    {
        SkipIfUnityNotRunning();
        // Compile mode with System.IO — read/write temp file to exercise real IO
        var code = @"using System;
using System.IO;
public class Test
{
    public static string Execute()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), ""joker_fb_"" + Guid.NewGuid().ToString(""N""));
        File.WriteAllText(tempFile, ""fallback_works"");
        var content = File.ReadAllText(tempFile);
        File.Delete(tempFile);
        return content;
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("fallback_works");
    }

    [SkippableFact]
    public async Task Fallback_EvalMode_CompilationErrorException_Analyzed()
    {
        SkipIfUnityNotRunning();
        // Regex is not in default references — fallback should resolve System assembly
        var code = @"using System.Text.RegularExpressions;
var r = new Regex(@""\d+"");
r.Matches(""abc123def456"").Count.ToString()";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("2"); // two number groups
    }

    [SkippableFact]
    public async Task Fallback_CompileMode_EmitError_Analyzed()
    {
        SkipIfUnityNotRunning();
        // Use DirectoryInfo + FileInfo — exercise System.IO types beyond simple File/Path
        var code = @"using System;
using System.IO;
using System.Linq;
public class Test
{
    public static string Execute()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), ""joker_dir_"" + Guid.NewGuid().ToString(""N""));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, ""a.txt""), ""aaa"");
        File.WriteAllText(Path.Combine(tempDir, ""b.txt""), ""bbb"");
        var files = new DirectoryInfo(tempDir).GetFiles();
        var names = string.Join("","", files.Select(f => f.Name).OrderBy(n => n));
        Directory.Delete(tempDir, true);
        return names;
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("a.txt,b.txt");
    }
}
