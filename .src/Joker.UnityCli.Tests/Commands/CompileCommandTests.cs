using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;
using Xunit;

namespace Joker.UnityCli.Tests.Commands;

public class CompileCommandTests
{
    [Fact]
    public async Task CompileCommand_WithValidProject_ReturnsZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = []
        });

        var compileService = Substitute.For<ICompileService>();
        compileService.CompileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompileResult
            {
                Success = true,
                Status = "compiled",
                DurationMs = 1000
            });

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--project", "/path/to/project", "--json"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task CompileCommand_WithCompilationFailure_ReturnsNonZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = []
        });

        var compileService = Substitute.For<ICompileService>();
        compileService.CompileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompileResult
            {
                Success = false,
                Status = "failed",
                Errors = ["CS0234: The name 'foo' does not exist"],
                DurationMs = 3000
            });

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--project", "/path/to/project", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task CompileCommand_WithNoProject_ReturnsNonZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var compileService = Substitute.For<ICompileService>();

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task CompileCommand_PassesTimeoutToService()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = []
        });

        var compileService = Substitute.For<ICompileService>();
        compileService.CompileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompileResult { Success = true, Status = "compiled" });

        var app = CreateApp(projectDetector, compileService);

        await app.RunAsync(["compile", "--project", "/path/to/project", "--timeout", "600", "--json"]);

        await compileService.Received(1).CompileAsync(
            "/path/to/project",
            600000,
            Arg.Any<CancellationToken>());
    }

    private static CommandApp CreateApp(IProjectDetector projectDetector, ICompileService compileService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        services.AddSingleton(compileService);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<CompileCommand>("compile");
        });
        return app;
    }
}
