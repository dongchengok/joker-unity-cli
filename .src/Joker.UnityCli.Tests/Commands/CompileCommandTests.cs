using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;
using Xunit;

namespace Joker.UnityCli.Tests.Commands;

[Collection("ConsoleOutput")]
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

    [Fact]
    public async Task CompileCommand_AutoDetectProject_Success()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/auto/detected",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = []
        });

        var compileService = Substitute.For<ICompileService>();
        compileService.CompileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompileResult { Success = true, Status = "compiled" });

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--json"]);

        result.Should().Be(0);
        await compileService.Received(1).CompileAsync(
            "/auto/detected",
            300000,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileCommand_AutoDetectProject_NotFound_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var compileService = Substitute.For<ICompileService>();

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task CompileCommand_TextOutput_Success()
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
            .Returns(new CompileResult { Success = true, Status = "compiled", DurationMs = 1000 });

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--project", "/path/to/project"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task CompileCommand_TextOutput_UpToDate()
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
            .Returns(new CompileResult { Success = true, Status = "up_to_date", DurationMs = 100 });

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--project", "/path/to/project"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task CompileCommand_TextOutput_Failure()
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

        var result = await app.RunAsync(["compile", "--project", "/path/to/project"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task CompileCommand_TextOutput_Timeout()
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
            .Returns(new CompileResult { Success = false, Status = "timeout", DurationMs = 300000 });

        var app = CreateApp(projectDetector, compileService);

        var result = await app.RunAsync(["compile", "--project", "/path/to/project"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task CompileCommand_JsonOutput_Success_WritesValidJsonToStdout()
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
            .Returns(new CompileResult { Success = true, Status = "compiled", DurationMs = 1000 });

        var app = CreateApp(projectDetector, compileService);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await app.RunAsync(["compile", "--project", "/path/to/project", "--json"]);

            result.Should().Be(0);
            var stdout = sw.ToString();
            stdout.Should().NotBeNullOrEmpty();
            var json = System.Text.Json.JsonDocument.Parse(stdout);
            json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task CompileCommand_JsonOutput_Failure_ContainsErrorArray()
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
                Errors = ["CS0234: Type not found", "CS0103: Name not found"],
                DurationMs = 3000
            });

        var app = CreateApp(projectDetector, compileService);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await app.RunAsync(["compile", "--project", "/path/to/project", "--json"]);

            result.Should().Be(1);
            var stdout = sw.ToString();
            stdout.Should().NotBeNullOrEmpty();
            var json = System.Text.Json.JsonDocument.Parse(stdout);
            json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            var errors = json.RootElement.GetProperty("errors");
            errors.GetArrayLength().Should().Be(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task CompileCommand_DefaultTimeout_Is300Seconds()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/auto/detected",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = []
        });

        var compileService = Substitute.For<ICompileService>();
        compileService.CompileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompileResult { Success = true, Status = "compiled" });

        var app = CreateApp(projectDetector, compileService);

        await app.RunAsync(["compile", "--json"]);

        // Default timeout is 300 seconds = 300000ms
        await compileService.Received(1).CompileAsync(
            "/auto/detected",
            300000,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileCommand_ExplicitProject_UsesExactPath()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/explicit/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = []
        });

        var compileService = Substitute.For<ICompileService>();
        compileService.CompileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompileResult { Success = true, Status = "compiled" });

        var app = CreateApp(projectDetector, compileService);

        await app.RunAsync(["compile", "--project", "/explicit/path/to/project", "--json"]);

        projectDetector.Received(1).Detect("/explicit/path/to/project");
        await compileService.Received(1).CompileAsync(
            "/explicit/path/to/project",
            Arg.Any<int>(),
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
