using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Tests.Commands;

[Collection("ConsoleOutput")]
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
    public async Task ExecCommand_AutoDetectProject_Success()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/auto/detected",
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

        var result = await app.RunAsync(["exec", "Debug.Log(\"hello\")", "--json"]);

        result.Should().Be(0);
        await execService.Received(1).ExecuteAsync(
            "/auto/detected",
            "Debug.Log(\"hello\")",
            "script",
            30000,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecCommand_AutoDetectProject_NotFound_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var execService = Substitute.For<IExecService>();

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "Debug.Log(\"hello\")", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task ExecCommand_TextOutput_Success()
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
                Output = "Line1\nLine2",
                Result = "42",
                DurationMs = 150
            });

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "6*7", "--project", "/path/to/project"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task ExecCommand_TextOutput_Failure()
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
                Error = "Syntax error",
                Output = "partial output",
                DurationMs = 50
            });

        var app = CreateExecApp(projectDetector, execService);

        var result = await app.RunAsync(["exec", "invalid code", "--project", "/path/to/project"]);

        result.Should().Be(1);
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

    [Fact]
    public async Task ExecCommand_WhenServerNotRunning_JsonOutput_WritesErrorToStderr()
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
            .Returns<ExecResult>(_ => throw new FileNotFoundException("Server not found"));

        var app = CreateExecApp(projectDetector, execService);

        var originalStderr = Console.Error;
        using var stderrWriter = new StringWriter();
        Console.SetError(stderrWriter);

        try
        {
            var result = await app.RunAsync(["exec", "code", "--project", "/path/to/project", "--json"]);

            result.Should().Be(1);
            var stderr = stderrWriter.ToString();
            stderr.Should().Contain("Unity server not running");
        }
        finally
        {
            Console.SetError(originalStderr);
        }
    }

    [Fact]
    public async Task ExecCommand_WhenConnectionFails_JsonOutput_WritesErrorToStderr()
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
            .Returns<ExecResult>(_ => throw new HttpRequestException("Connection refused"));

        var app = CreateExecApp(projectDetector, execService);

        var originalStderr = Console.Error;
        using var stderrWriter = new StringWriter();
        Console.SetError(stderrWriter);

        try
        {
            var result = await app.RunAsync(["exec", "code", "--project", "/path/to/project", "--json"]);

            result.Should().Be(1);
            var stderr = stderrWriter.ToString();
            stderr.Should().Contain("Cannot connect");
        }
        finally
        {
            Console.SetError(originalStderr);
        }
    }

    [Fact]
    public async Task ExecCommand_WhenTimeout_TextOutput_ReturnsNonZero()
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
            .Returns<ExecResult>(_ => throw new OperationCanceledException("Timeout"));

        var app = CreateExecApp(projectDetector, execService);

        // ExecCommand does not catch OperationCanceledException, so it propagates.
        // Spectre.Console.Cli wraps the exception and returns non-zero exit code.
        var result = await app.RunAsync(["exec", "while(true){}", "--project", "/path/to/project"]);

        result.Should().NotBe(0);
    }

    [Fact]
    public async Task ExecCommand_CustomTimeout_PassedToService()
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
                Output = "done",
                DurationMs = 100
            });

        var app = CreateExecApp(projectDetector, execService);

        await app.RunAsync(["exec", "1+1", "--project", "/path/to/project", "--timeout", "12345", "--json"]);

        await execService.Received(1).ExecuteAsync(
            "/path/to/project",
            "1+1",
            "script",
            12345,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecCommand_JsonOutput_Success_WritesValidJsonToStdout()
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
                Result = "2",
                DurationMs = 150
            });

        var app = CreateExecApp(projectDetector, execService);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await app.RunAsync(["exec", "1+1", "--project", "/path/to/project", "--json"]);

            result.Should().Be(0);
            var stdout = sw.ToString();
            stdout.Should().NotBeNullOrEmpty();
            var json = System.Text.Json.JsonDocument.Parse(stdout);
            json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("id").GetString().Should().Be("abc12345");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecCommand_JsonOutput_Failure_WritesValidJsonToStdout()
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
                Error = "Syntax error",
                DurationMs = 50
            });

        var app = CreateExecApp(projectDetector, execService);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await app.RunAsync(["exec", "invalid", "--project", "/path/to/project", "--json"]);

            result.Should().Be(1);
            var stdout = sw.ToString();
            stdout.Should().NotBeNullOrEmpty();
            var json = System.Text.Json.JsonDocument.Parse(stdout);
            json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("error").GetString().Should().Be("Syntax error");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecCommand_FileMode_EmptyFile_ReturnsError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "");

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
                    Error = "Empty script",
                    DurationMs = 10
                });

            var app = CreateExecApp(projectDetector, execService);

            var result = await app.RunAsync(["exec", "--file", tempFile, "--project", "/path/to/project", "--json"]);

            result.Should().Be(1);
        }
        finally
        {
            File.Delete(tempFile);
        }
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
