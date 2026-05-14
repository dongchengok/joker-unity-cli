using System.IO;
using FluentAssertions;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.PortDiscovery;

public class CompileServicePortReaderTests
{
    [Fact]
    public void TryReadServerPort_ValidFile_ReturnsPort()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(12345);

        var port = CompileService.TryReadServerPort(fixture.ProjectPath);

        port.Should().Be(12345);
    }

    [Fact]
    public void TryReadServerPort_NoFile_ReturnsNull()
    {
        using var fixture = new TempProjectFixture();

        var port = CompileService.TryReadServerPort(fixture.ProjectPath);

        port.Should().BeNull();
    }

    [Fact]
    public void TryReadServerPort_InvalidJson_ReturnsNull()
    {
        using var fixture = new TempProjectFixture();
        var jokerDir = Path.Combine(fixture.ProjectPath, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"), "invalid json");

        var port = CompileService.TryReadServerPort(fixture.ProjectPath);

        port.Should().BeNull();
    }

    [Fact]
    public void TryReadServerPort_ZeroPort_ReturnsNull()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(0);

        var port = CompileService.TryReadServerPort(fixture.ProjectPath);

        port.Should().BeNull("because port 0 is not valid");
    }
}
