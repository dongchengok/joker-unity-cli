using System.IO;
using FluentAssertions;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.PortDiscovery;

public class ExecServicePortReaderTests
{
    [Fact]
    public void ReadServerPort_WhenFileMissing_ThrowsFileNotFoundException()
    {
        using var fixture = new TempProjectFixture();

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ReadServerPort_InvalidJson_ThrowsIOException()
    {
        using var fixture = new TempProjectFixture();
        var jokerDir = Path.Combine(fixture.ProjectPath, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"), "not json");

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<IOException>().WithMessage("*Failed to read server port file*");
    }

    [Fact]
    public void ReadServerPort_MissingPortField_ThrowsIOException()
    {
        using var fixture = new TempProjectFixture();
        var jokerDir = Path.Combine(fixture.ProjectPath, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(Path.Combine(jokerDir, "server.json"), "{\"pid\":1234}");

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<IOException>().WithMessage("*Failed to read server port file*");
    }

    [Fact]
    public void ReadServerPort_ValidPort_ReturnsPort()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(12345);

        var port = ExecService.ReadServerPort(fixture.ProjectPath);

        port.Should().Be(12345);
    }

    [Fact]
    public void ReadServerPort_ZeroPort_ThrowsIOException()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(0);

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<IOException>().WithMessage("*Failed to read server port file*");
    }

    [Fact]
    public void ReadServerPort_NegativePort_ThrowsIOException()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(-1);

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<IOException>().WithMessage("*Failed to read server port file*");
    }

    [Fact]
    public void ReadServerPort_CreatesCorrectFilePath()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(8080);

        var expectedFile = Path.Combine(fixture.ProjectPath, ".joker-unity", "server.json");
        File.Exists(expectedFile).Should().BeTrue("the port file should be at the expected path");

        var port = ExecService.ReadServerPort(fixture.ProjectPath);
        port.Should().Be(8080);
    }
}
