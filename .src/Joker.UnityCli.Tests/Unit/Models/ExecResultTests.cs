using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.Models;

public class ExecResultTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void ExecResult_Serialize_Success_CamelCase_Json()
    {
        var result = new ExecResult
        {
            Id = "test-id-123",
            Success = true,
            Result = "Success",
            Output = "Hello World",
            DurationMs = 1500
        };

        var json = JsonSerializer.Serialize(result, JsonOptions);

        json.Should().Contain("\"type\":\"exec_result\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"result\":\"Success\"");
        json.Should().Contain("\"output\":\"Hello World\"");
        json.Should().Contain("\"durationMs\":1500");
    }

    [Fact]
    public void ExecResult_Deserialize_Error_Case()
    {
        var json = "{\"type\":\"exec_result\",\"id\":\"test-id-123\",\"success\":false,\"error\":\"Compilation error\",\"durationMs\":1000}";
        var result = JsonSerializer.Deserialize<ExecResult>(json, JsonOptions);

        result.Should().NotBeNull();
        result!.Type.Should().Be("exec_result");
        result.Id.Should().Be("test-id-123");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Compilation error");
        result.DurationMs.Should().Be(1000);
        result.Result.Should().BeNull();
        result.Output.Should().BeNull();
    }

    [Fact]
    public void ExecResult_Defaults_TypeIsExecResult()
    {
        var result = new ExecResult();

        result.Type.Should().Be("exec_result");
    }

    [Fact]
    public void ExecResult_Defaults_IdIsEmpty()
    {
        var result = new ExecResult();

        result.Id.Should().Be("");
    }

    [Fact]
    public void ExecResult_Defaults_SuccessIsFalse()
    {
        var result = new ExecResult();

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void ExecResult_Deserialize_WithOutput()
    {
        var json = "{\"type\":\"exec_result\",\"id\":\"out-test\",\"success\":true,\"result\":\"42\",\"output\":\"log line 1\\nlog line 2\",\"durationMs\":200}";
        var result = JsonSerializer.Deserialize<ExecResult>(json, JsonOptions);

        result.Should().NotBeNull();
        result!.Output.Should().Be("log line 1\nlog line 2");
        result.Result.Should().Be("42");
        result.Success.Should().BeTrue();
    }
}
