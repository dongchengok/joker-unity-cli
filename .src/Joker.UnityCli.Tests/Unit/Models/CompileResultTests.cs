using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.Models;

public class CompileResultTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void CompileResult_Defaults()
    {
        var result = new CompileResult();

        result.Success.Should().BeFalse();
        result.Status.Should().Be("");
        result.Errors.Should().BeEmpty();
        result.DurationMs.Should().Be(0);
    }

    [Fact]
    public void CompileResult_Serializes_To_CamelCase_Json()
    {
        var result = new CompileResult
        {
            Success = true,
            Status = "compiled",
            Errors = new List<string>(),
            DurationMs = 5000
        };

        var json = JsonSerializer.Serialize(result, JsonOptions);

        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"status\":\"compiled\"");
        json.Should().Contain("\"errors\":[]");
        json.Should().Contain("\"durationMs\":5000");
    }

    [Fact]
    public void CompileResult_Deserializes_From_CamelCase_Json()
    {
        var json = "{\"success\":false,\"status\":\"failed\",\"errors\":[\"CS0234: The name 'foo' does not exist\"],\"durationMs\":3000}";
        var result = JsonSerializer.Deserialize<CompileResult>(json, JsonOptions);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Be("CS0234: The name 'foo' does not exist");
        result.DurationMs.Should().Be(3000);
    }

    [Fact]
    public void CompileResult_Serializes_WithErrors()
    {
        var result = new CompileResult
        {
            Success = false,
            Status = "failed",
            Errors = new List<string> { "CS0234: error 1", "CS1002: error 2" },
            DurationMs = 2000
        };

        var json = JsonSerializer.Serialize(result, JsonOptions);

        json.Should().Contain("\"CS0234: error 1\"");
        json.Should().Contain("\"CS1002: error 2\"");
    }
}
