using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
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

    [Fact]
    public async Task ExecuteAsync_WithMockServer_ReturnsResult()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

        var serverTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            using var reader = new StreamReader(client.GetStream());
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            var line = await reader.ReadLineAsync();
            var response = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true,
                Result = "42", DurationMs = 5
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await writer.WriteLineAsync(response);
            client.Close();
        });

        var service = new ExecService();
        var result = await service.ExecuteAsync(tempDir, "6*7", "script", 5000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().Be("42");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void ReadServerPort_WhenFileMissing_ThrowsFileNotFoundException()
    {
        var act = () => ExecService.ReadServerPort(Path.GetTempPath());
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_SendsCorrectMode()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

        string? receivedLine = null;
        var serverTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            using var reader = new StreamReader(client.GetStream());
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            receivedLine = await reader.ReadLineAsync();
            var response = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true, DurationMs = 1
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await writer.WriteLineAsync(response);
            client.Close();
        });

        var service = new ExecService();
        await service.ExecuteAsync(tempDir, "code", "compile", 5000, CancellationToken.None);

        receivedLine.Should().NotBeNull();
        var request = JsonSerializer.Deserialize<ExecRequest>(receivedLine!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        request!.Mode.Should().Be("compile");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }
}