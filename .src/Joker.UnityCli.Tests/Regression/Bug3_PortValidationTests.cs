using FluentAssertions;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Regression;

/// <summary>
/// Regression tests for Bug #3: ReadServerPort returned Port=0 instead of
/// throwing when the port file contained an invalid (zero) value.
/// Fixed by validating Port > 0.
/// </summary>
public class Bug3_PortValidationTests
{
    [Fact]
    public void ReadServerPort_ZeroPort_ThrowsIOException()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(0);

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<IOException>()
            .WithMessage("*Failed to read server port file*");
    }

    [Fact]
    public void ReadServerPort_NegativePort_ThrowsIOException()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(-1);

        var act = () => ExecService.ReadServerPort(fixture.ProjectPath);

        act.Should().Throw<IOException>()
            .WithMessage("*Failed to read server port file*");
    }

    [Fact]
    public void TryReadServerInfo_ZeroPort_ReturnsPortZero()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(0);

        var info = CompileService.TryReadServerInfo(fixture.ProjectPath);

        info.Should().NotBeNull("port 0 should still be deserialized");
        info!.Port.Should().Be(0);
    }

    [Fact]
    public void TryReadServerInfo_NegativePort_ReturnsNegativePort()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(-1);

        var info = CompileService.TryReadServerInfo(fixture.ProjectPath);

        info.Should().NotBeNull("negative port should still be deserialized");
        info!.Port.Should().Be(-1);
    }
}
