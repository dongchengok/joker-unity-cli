using System.IO;
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Regression;

/// <summary>
/// Regression tests for Bug #2: ScriptExecutor referenced both UnityEngine facade
/// and CoreModule, causing CS0433 ambiguous type references. Fixed by using only
/// Assembly.Load("UnityEngine") facade.
/// </summary>
public class Bug2_CS0433TypeConflictTests : IDisposable
{
    private readonly string _tempDir;

    public Bug2_CS0433TypeConflictTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerCS0433Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CompileService_ParseLog_CS0433_RecognizedAsError()
    {
        // CS0433: The imported type 'typename' is ambiguous.
        // This was the exact error caused by referencing both UnityEngine assemblies.
        var logFile = Path.Combine(_tempDir, "compile.log");
        var logContent = """
            Assets/Scripts/GameManager.cs(15,10): error CS0433: The type 'GameObject' exists in both 'UnityEngine.CoreModule' and 'UnityEngine'
            """;
        File.WriteAllText(logFile, logContent);

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().HaveCount(1);
        errors[0].Should().Contain("CS0433");
        errors[0].Should().Contain("exists in both");
    }

    [Fact]
    public void ExecResult_Deserialize_DuplicateFieldLastWins()
    {
        // Verify that System.Text.Json takes the last value when duplicate keys exist.
        // This is relevant because server responses could have duplicate fields in edge cases.
        var json = /*lang=json,strict*/ """
            {
                "type": "exec_result",
                "id": "test",
                "success": false,
                "success": true,
                "result": "42",
                "durationMs": 5
            }
            """;

        var result = JsonSerializer.Deserialize<ExecResult>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue("the last 'success' value (true) should win");
    }

    [Fact]
    public async Task ExecService_SendsRequest_WithoutAmbiguousAssemblyHints()
    {
        // After the fix, the CLI should NOT send "assembly" or "reference" fields
        // in the request body — those were part of the buggy approach.
        using var server = new MockHttpServer();
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(server.Port);

        string? receivedBody = null;
        server.Start(ctx =>
        {
            using var reader = new StreamReader(ctx.Request.InputStream);
            receivedBody = reader.ReadToEnd();

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result",
                Id = "test",
                Success = true,
                DurationMs = 1
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.Close();
        });

        var service = new ExecService();
        await service.ExecuteAsync(fixture.ProjectPath, "1+1", "script", 5000, CancellationToken.None);

        receivedBody.Should().NotBeNull();
        receivedBody.Should().NotContain("assembly", "the request should not contain assembly hints");
        receivedBody.Should().NotContain("reference", "the request should not contain reference hints");
    }
}
