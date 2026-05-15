using System.IO;
using FluentAssertions;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.PortDiscovery;

public class CompileServicePortReaderTests
{
    [Fact]
    public void TryReadServerInfo_ValidFile_ReturnsPort()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(12345);

        var info = CompileService.TryReadServerInfo(fixture.ProjectPath);

        info.Should().NotBeNull();
        info!.Port.Should().Be(12345);
    }

    [Fact]
    public void TryReadServerInfo_NoFile_ReturnsNull()
    {
        using var fixture = new TempProjectFixture();

        var info = CompileService.TryReadServerInfo(fixture.ProjectPath);

        info.Should().BeNull();
    }

    [Fact]
    public void TryReadServerInfo_InvalidJson_ReturnsNull()
    {
        using var fixture = new TempProjectFixture();
        var jokerDir = Path.Combine(fixture.ProjectPath, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"), "invalid json");

        var info = CompileService.TryReadServerInfo(fixture.ProjectPath);

        info.Should().BeNull();
    }

    [Fact]
    public void TryReadServerInfo_ZeroPort_ReturnsPortZero()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(0);

        var info = CompileService.TryReadServerInfo(fixture.ProjectPath);

        info.Should().NotBeNull();
        info!.Port.Should().Be(0);
    }
}
