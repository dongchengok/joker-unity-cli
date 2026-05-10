using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class ExecServiceTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ExecServiceTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void ExecRequest_Serializes_To_CamelCase_Json()
    {
        var request = new ExecRequest
        {
            Id = "test-id-123",
            Code = "Debug.Log(\"Hello World\");",
            Timeout = 60000,
            Mode = "script"
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        json.Should().Contain("\"type\":\"exec\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"code\":\"Debug.Log(\\u0022Hello World\\u0022);\"");
        json.Should().Contain("\"timeout\":60000");
        json.Should().Contain("\"mode\":\"script\"");
    }

    [Fact]
    public void ExecRequest_Deserializes_From_CamelCase_Json()
    {
        var json = "{\"type\":\"exec\",\"id\":\"test-id-123\",\"code\":\"Debug.Log(\\u0022Hello World\\u0022);\",\"timeout\":60000,\"mode\":\"script\"}";
        var request = JsonSerializer.Deserialize<ExecRequest>(json, _jsonOptions);

        request.Should().NotBeNull();
        request!.Type.Should().Be("exec");
        request.Id.Should().Be("test-id-123");
        request.Code.Should().Be("Debug.Log(\"Hello World\");");
        request.Timeout.Should().Be(60000);
        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecResult_Serializes_To_CamelCase_Json()
    {
        var result = new ExecResult
        {
            Id = "test-id-123",
            Success = true,
            Result = "Success",
            Output = "Hello World",
            DurationMs = 1500
        };

        var json = JsonSerializer.Serialize(result, _jsonOptions);
        json.Should().Contain("\"type\":\"exec_result\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"result\":\"Success\"");
        json.Should().Contain("\"output\":\"Hello World\"");
        json.Should().Contain("\"durationMs\":1500");
    }

    [Fact]
    public void ExecResult_Deserializes_Error_Case()
    {
        var json = "{\"type\":\"exec_result\",\"id\":\"test-id-123\",\"success\":false,\"error\":\"Compilation error\",\"durationMs\":1000}";
        var result = JsonSerializer.Deserialize<ExecResult>(json, _jsonOptions);

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
    public void ExecRequest_Default_Mode_Is_Script()
    {
        var request = new ExecRequest();

        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecResult_Defaults()
    {
        var result = new ExecResult();

        result.Type.Should().Be("exec_result");
    }
}