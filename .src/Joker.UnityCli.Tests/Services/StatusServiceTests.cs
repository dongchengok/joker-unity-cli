using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class StatusServiceTests : IDisposable
{
    private readonly string _tempDir;

    public StatusServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerStatusTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task GetStatusAsync_NoServerFile_ReturnsNotFound()
    {
        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);
        result.Status.Should().Be("not_found");
    }

    [Fact]
    public async Task GetStatusAsync_WithReadyStatus_ReturnsReady()
    {
        var port = PortHelper.FindAvailablePort();
        var jokerDir = Path.Combine(_tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = 9999, status = "ready" }));

        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("ready");
        result.Port.Should().Be(port);
        result.Pid.Should().Be(9999);
        result.ServerResponding.Should().BeFalse(); // no real server running
    }

    [Fact]
    public async Task GetStatusAsync_CompilingStatus_ReturnsCompiling()
    {
        var port = PortHelper.FindAvailablePort();
        var jokerDir = Path.Combine(_tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = 9999, status = "compiling" }));

        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("compiling");
        result.ServerResponding.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_WithoutStatusField_ReturnsUnknown()
    {
        var port = PortHelper.FindAvailablePort();
        var jokerDir = Path.Combine(_tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = 9999 }));

        var service = new StatusService();
        var result = await service.GetStatusAsync(_tempDir, CancellationToken.None);

        result.Status.Should().Be("unknown");
    }
}
