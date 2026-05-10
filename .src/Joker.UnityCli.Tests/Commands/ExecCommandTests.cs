using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Tests.Commands;

public class ExecCommandTests
{
    [Fact]
    public async Task ExecCommand_WithMissingCode_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        var execService = Substitute.For<IExecService>();

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ExecCommand_WithNonexistentProject_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var execService = Substitute.For<IExecService>();

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "Debug.Log(\"hello\")", "--project", "/invalid", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ExecCommand_WithBothCodeAndFile_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        var execService = Substitute.For<IExecService>();

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "Debug.Log(\"hello\")", "--file", "/some/script.cs", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ExecCommand_WithNonexistentFile_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        var execService = Substitute.For<IExecService>();

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "--file", "/nonexistent/script.cs", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ExecCommand_WithValidCode_ReturnsZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecResult
            {
                Success = true,
                Id = "abc12345",
                Output = "Hello World",
                DurationMs = 150
            });

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "Debug.Log(\"hello\")", "--project", "/path/to/project", "--json"]);

        result.Should().Be(0);
        await execService.Received(1).ExecuteAsync(
            "/path/to/project",
            "Debug.Log(\"hello\")",
            "script",
            30000,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecCommand_WithFileMode_SetsCompileMode()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "public class Test {}");

            var projectDetector = Substitute.For<IProjectDetector>();
            projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
            {
                Name = "TestProject",
                Path = "/path/to/project",
                UnityVersion = "2022.3.20f1",
                PackageDependencies = new List<string>()
            });

            var execService = Substitute.For<IExecService>();
            execService.ExecuteAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ExecResult
                {
                    Success = true,
                    Id = "abc12345",
                    DurationMs = 200
                });

            var app = CreateExecApp(projectDetector, execService);

            var result = await app.RunAsync(["exec", "--file", tempFile, "--project", "/path/to/project", "--json"]);

            result.Should().Be(0);
            await execService.Received(1).ExecuteAsync(
                "/path/to/project",
                "public class Test {}",
                "compile",
                30000,
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecCommand_WithExecFailure_ReturnsNonZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var execService = Substitute.For<IExecService>();
        execService.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ExecResult
            {
                Success = false,
                Id = "abc12345",
                Error = "Compilation error",
                DurationMs = 50
            });

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "invalid code", "--project", "/path/to/project", "--json"]);

        result.Should().Be(1);
    }

    private static CommandApp CreateExecApp(IProjectDetector projectDetector, IExecService execService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        services.AddSingleton(execService);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<ExecCommand>("exec");
        });
        return app;
    }
}
