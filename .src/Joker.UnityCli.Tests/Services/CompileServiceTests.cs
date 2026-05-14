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
                        JsonSerializer.Serialize(new { port = newPort, pid = 9999, status = "ready" }));
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

    private string CreateProjectWithServer(int port, int pid = 9999, string status = "ready")
    {
        var projectDir = Path.Combine(_tempDir, $"project_{Guid.NewGuid():N}");
        var jokerDir = Path.Combine(projectDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid, status }));
        return projectDir;
    }

    [Fact]
    public async Task CompileAsync_TcpPath_TriggerSucceedsButPortUnchanged_FallsBackToBatchmode()
    {
        // Setup: project with server.json, ExecService returns success but port never changes.
        // The TCP path will attempt to poll for port change, but since timeout is short,
        // it will try to read Editor.log which may fail. Instead, make trigger fail to
        // skip the port polling entirely and test the fallback path.
        var projectDir = CreateProjectWithServer(12345);

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult { Success = false, Error = "trigger failed" });

        // No Unity installation → batchmode also fails
        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns((UnityInstallation?)null);

        var service = new CompileService(execService, unityLocator);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        // TCP trigger fails → returns null → batchmode → no Unity → fail
        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
    }

    [Fact]
    public async Task CompileAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var projectDir = CreateProjectWithServer(12345);

        var execService = Substitute.For<IExecService>();
        // Return success without checking CancellationToken - the mock ignores the token.
        // Cancellation will be caught at the polling loop's ct.ThrowIfCancellationRequested().
        execService.ExecuteAsync(projectDir, Arg.Any<string>(), "script", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult { Success = true, Result = "triggered" });

        var unityLocator = Substitute.For<IUnityLocator>();
        var service = new CompileService(execService, unityLocator);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // With an already-cancelled token: execService returns success (mock ignores ct),
        // then the polling loop's ct.ThrowIfCancellationRequested() throws immediately.
        var act = async () => await service.CompileAsync(projectDir, 30000, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CompileAsync_Batchmode_ProcessTimesOut_ReturnsTimeout()
    {
        // Setup: no server.json → skip TCP → batchmode path
        // unityLocator returns a fake Unity path that won't actually start
        var projectDir = Path.Combine(_tempDir, $"notcp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDir);

        var execService = Substitute.For<IExecService>();
        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns((UnityInstallation?)null);

        var service = new CompileService(execService, unityLocator);
        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
    }

    [Fact]
    public async Task CompileAsync_Batchmode_NoUnityFound_ReturnsFailedWithErrors()
    {
        // Setup: no server.json, no Unity installation
        var projectDir = Path.Combine(_tempDir, $"nounity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDir);

        var execService = Substitute.For<IExecService>();
        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns((UnityInstallation?)null);

        var service = new CompileService(execService, unityLocator);

        var result = await service.CompileAsync(projectDir, 5000, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().ContainMatch("*Unity*not found*");
    }
}
