using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Tests.Commands;

[Collection("ConsoleOutput")]
public class InfoCommandTests
{
    private static CommandApp CreateApp(IProjectDetector projectDetector)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<InfoCommand>("info");
        });
        return app;
    }

    [Fact]
    public async Task Execute_WithValidProject_ReturnsZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string> { "com.unity.ugui" }
        });

        var app = CreateApp(projectDetector);

        // Act
        var result = await app.RunAsync(["info", "--project", "/path/to/project"]);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task Execute_WithNoProjectFound_ReturnsNonZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateApp(projectDetector);

        // Act
        var result = await app.RunAsync(["info", "--project", "/invalid/path"]);

        // Assert
        result.Should().NotBe(0);
    }

    [Fact]
    public async Task Execute_WithProjectPath_CallsDetectWithCorrectPath()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect("/custom/path").Returns(new UnityProject
        {
            Name = "Custom",
            Path = "/custom/path",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var app = CreateApp(projectDetector);

        // Act
        await app.RunAsync(["info", "--project", "/custom/path"]);

        // Assert
        projectDetector.Received(1).Detect("/custom/path");
    }

    [Fact]
    public async Task Execute_WithValidProject_DetectIsCalled()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "DetectedProject",
            Path = "/some/path",
            UnityVersion = "2022.3.0f1",
            PackageDependencies = new List<string>()
        });

        var app = CreateApp(projectDetector);

        // Act
        await app.RunAsync(["info", "-p", "/some/path"]);

        // Assert
        projectDetector.Received(1).Detect("/some/path");
    }
}
