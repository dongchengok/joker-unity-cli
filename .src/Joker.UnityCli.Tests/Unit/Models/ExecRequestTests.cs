using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.Models;

public class ExecRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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

        var json = JsonSerializer.Serialize(request, JsonOptions);

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
        var request = JsonSerializer.Deserialize<ExecRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.Type.Should().Be("exec");
        request.Id.Should().Be("test-id-123");
        request.Code.Should().Be("Debug.Log(\"Hello World\");");
        request.Timeout.Should().Be(60000);
        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecRequest_Defaults_ModeIsScript()
    {
        var request = new ExecRequest();

        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecRequest_Defaults_TypeIsExec()
    {
        var request = new ExecRequest();

        request.Type.Should().Be("exec");
    }

    [Fact]
    public void ExecRequest_Defaults_TimeoutIs30000()
    {
        var request = new ExecRequest();

        request.Timeout.Should().Be(30000);
    }

    [Fact]
    public void ExecRequest_Serialize_AllFieldsPresent()
    {
        var request = new ExecRequest
        {
            Id = "all-fields-test",
            Code = "var x = 1;",
            Timeout = 5000,
            Mode = "compile"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);

        // Verify every expected field is present in the serialized output
        json.Should().Contain("\"type\":");
        json.Should().Contain("\"id\":");
        json.Should().Contain("\"code\":");
        json.Should().Contain("\"timeout\":");
        json.Should().Contain("\"mode\":");

        // Round-trip: deserialize and verify all values match
        var deserialized = JsonSerializer.Deserialize<ExecRequest>(json, JsonOptions);
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(request.Type);
        deserialized.Id.Should().Be(request.Id);
        deserialized.Code.Should().Be(request.Code);
        deserialized.Timeout.Should().Be(request.Timeout);
        deserialized.Mode.Should().Be(request.Mode);
    }
}
