using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using NSubstitute;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

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

public class CompileServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CompileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerCompileTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // === ParseLogForErrors tests ===

    [Fact]
    public void ParseLogForErrors_WithErrors_ReturnsErrorMessages()
    {
        var logFile = Path.Combine(_tempDir, "build.log");
        var logContent = """
            Some log line
            Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
            Another line
            Assets/Scripts/Game.cs(30,1): error CS1002: ; expected
            """;
        File.WriteAllText(logFile, logContent);

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().HaveCount(2);
        errors[0].Should().Contain("CS0234");
        errors[0].Should().Contain("The name 'foo' does not exist");
        errors[1].Should().Contain("CS1002");
    }

    [Fact]
    public void ParseLogForErrors_WarningsNotIncluded()
    {
        var logFile = Path.Combine(_tempDir, "build_log.log");
        var logContent = """
            Assets/Scripts/Player.cs(10,5): warning CS0162: Unreachable code detected
            """;
        File.WriteAllText(logFile, logContent);

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogForErrors_NoErrors_ReturnsEmptyList()
    {
        var logFile = Path.Combine(_tempDir, "clean.log");
        File.WriteAllText(logFile, "Compilation succeeded\nNo errors\n");

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogForErrors_NonexistentFile_ReturnsEmptyList()
    {
        var errors = CompileService.ParseLogForErrors(Path.Combine(_tempDir, "nonexistent.log"));

        errors.Should().BeEmpty();
    }

    // === TryReadServerPort tests ===

    [Fact]
    public void TryReadServerPort_ValidFile_ReturnsPort()
    {
        var projectDir = CreateProjectWithServer(12345);

        var port = CompileService.TryReadServerPort(projectDir);

        port.Should().Be(12345);
    }

    [Fact]
    public void TryReadServerPort_NoFile_ReturnsNull()
    {
        var port = CompileService.TryReadServerPort(_tempDir);

        port.Should().BeNull();
    }

    [Fact]
    public void TryReadServerPort_InvalidJson_ReturnsNull()
    {
        var projectDir = Path.Combine(_tempDir, "badproject");
        var jokerDir = Path.Combine(projectDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"), "invalid json");

        var port = CompileService.TryReadServerPort(projectDir);

        port.Should().BeNull();
    }

    // === CompileAsync integration tests ===

    [Fact]
    public async Task CompileAsync_TcpNotAvailable_NoUnity_ReturnsFailed()
    {
        var projectDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(projectDir);

        var execService = Substitute.For<IExecService>();
        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns((UnityInstallation?)null);

        var service = new CompileService(execService, unityLocator);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().ContainMatch("*Unity*not found*");
    }

    // === TCP path - robustness tests ===

    [Fact]
    public async Task CompileAsync_TcpPath_PortChange_ReturnsCompiled()
    {
        // Setup: project with initial server.json at port 12345
        var initialPort = 12345;
        var newPort = 54321;
        var projectDir = CreateProjectWithServer(initialPort);
        var serverJsonPath = Path.Combine(projectDir, ".joker-unity", "server.json");

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                // Simulate port change after trigger: update server.json in background
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    File.WriteAllText(serverJsonPath,
                        JsonSerializer.Serialize(new { port = newPort, pid = 9999 }));
                });
                return new ExecResult { Success = true, Result = "triggered" };
            });

        var unityLocator = Substitute.For<IUnityLocator>();
        var service = new CompileService(execService, unityLocator);

        var result = await service.CompileAsync(projectDir, 10000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Status.Should().Be("compiled");
    }

    [Fact]
    public async Task CompileAsync_TcpTriggerFails_FallsBackToBatchmode()
    {
        // Setup: project with server.json but ExecService throws
        var projectDir = CreateProjectWithServer(12345);

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ExecResult>(_ => throw new Exception("TCP connection refused"));

        // No Unity installation available, so batchmode also fails
        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns((UnityInstallation?)null);

        var service = new CompileService(execService, unityLocator);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        // TCP path fails (exception), falls back to batchmode, which also fails (no Unity)
        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().ContainMatch("*Unity*not found*");
    }

    [Fact]
    public async Task CompileAsync_PortFileMissing_FallsBackToBatchmode()
    {
        // Setup: project WITHOUT server.json
        var projectDir = Path.Combine(_tempDir, $"noServer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDir);

        var execService = Substitute.For<IExecService>();
        // ExecService should never be called since TryReadServerPort returns null first

        // No Unity installation available, so batchmode also fails
        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns((UnityInstallation?)null);

        var service = new CompileService(execService, unityLocator);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        // No server.json → TryReadServerPort returns null → skip TCP → batchmode → no Unity → fail
        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().ContainMatch("*Unity*not found*");

        // Verify ExecService was never called (TCP path was skipped entirely)
        await execService.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private string CreateProjectWithServer(int port, int pid = 9999)
    {
        var projectDir = Path.Combine(_tempDir, $"project_{Guid.NewGuid():N}");
        var jokerDir = Path.Combine(projectDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid }));
        return projectDir;
    }
}
