using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using NSubstitute;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

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

    [Fact]
    public async Task CompileAsync_NoServerJson_ReturnsServerNotFound()
    {
        var projectDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(projectDir);

        var execService = Substitute.For<IExecService>();
        var service = new CompileService(execService);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("server_not_found");
        result.Errors.Should().ContainMatch("*Unity Editor*not running*");

        await execService.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileAsync_ServerJsonWithZeroPort_ReturnsServerNotFound()
    {
        var projectDir = Path.Combine(_tempDir, "project");
        var jokerDir = Path.Combine(projectDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port = 0, pid = 9999, status = "ready" }));

        var execService = Substitute.For<IExecService>();
        var service = new CompileService(execService);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("server_not_found");
    }

    [Fact]
    public async Task CompileAsync_TcpPath_PortChange_ReturnsCompiled()
    {
        var initialPort = 12345;
        var newPort = 54321;
        var projectDir = CreateProjectWithServer(initialPort);
        var serverJsonPath = Path.Combine(projectDir, ".joker-unity", "server.json");

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    File.WriteAllText(serverJsonPath,
                        JsonSerializer.Serialize(new { port = newPort, pid = 9999, status = "ready" }));
                });
                return new ExecResult { Success = true, Result = "triggered" };
            });

        var service = new CompileService(execService);

        var result = await service.CompileAsync(projectDir, 10000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Status.Should().Be("compiled");
    }

    [Fact]
    public async Task CompileAsync_TcpTriggerFails_ReturnsFailed()
    {
        var projectDir = CreateProjectWithServer(12345);

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ExecResult>(_ => throw new Exception("TCP connection refused"));

        var service = new CompileService(execService);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().ContainMatch("*Failed to trigger compilation*");
    }

    [Fact]
    public async Task CompileAsync_TcpTriggerReturnsFailure_ReturnsFailed()
    {
        var projectDir = CreateProjectWithServer(12345);

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult { Success = false, Error = "trigger failed" });

        var service = new CompileService(execService);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().ContainMatch("*trigger failed*");
    }

    [Fact]
    public async Task CompileAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var projectDir = CreateProjectWithServer(12345);

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult { Success = true, Result = "triggered" });

        var service = new CompileService(execService);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.CompileAsync(projectDir, 30000, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private string CreateProjectWithServer(int port, int pid = 9999, string status = "ready")
    {
        var projectDir = Path.Combine(_tempDir, $"project_{Guid.NewGuid():N}");
        var jokerDir = Path.Combine(projectDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid, status }));
        return projectDir;
    }
}
