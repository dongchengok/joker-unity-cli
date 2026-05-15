using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Tests.Commands;

[Collection("ConsoleOutput")]
public class StatusCommandTests
{
    [Fact]
    public async Task StatusCommand_WithNonexistentProject_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);

        var statusService = Substitute.For<IStatusService>();

        var app = CreateStatusApp(projectDetector, statusService);

        var result = await app.RunAsync(["status", "--project", "/invalid", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task StatusCommand_AutoDetectProjectNotFound_ReturnsError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var statusService = Substitute.For<IStatusService>();

        var app = CreateStatusApp(projectDetector, statusService);

        var result = await app.RunAsync(["status", "--json"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task StatusCommand_WithReadyStatus_ReturnsZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ServerStatus
            {
                Status = "ready",
                Port = 8080,
                Pid = 12345,
                ServerResponding = true
            });

        var app = CreateStatusApp(projectDetector, statusService);

        var result = await app.RunAsync(["status", "--project", "/path/to/project"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task StatusCommand_WithCompilingStatus_ReturnsNonZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ServerStatus
            {
                Status = "compiling",
                Port = 8080,
                Pid = 12345,
                ServerResponding = false
            });

        var app = CreateStatusApp(projectDetector, statusService);

        var result = await app.RunAsync(["status", "--project", "/path/to/project"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task StatusCommand_WithStoppedStatus_ReturnsNonZero()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ServerStatus
            {
                Status = "stopped",
                Port = 0,
                Pid = 0,
                ServerResponding = false
            });

        var app = CreateStatusApp(projectDetector, statusService);

        var result = await app.RunAsync(["status", "--project", "/path/to/project"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task StatusCommand_JsonOutput_Ready_WritesValidJson()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ServerStatus
            {
                Status = "ready",
                Port = 8080,
                Pid = 12345,
                ServerResponding = true
            });

        var app = CreateStatusApp(projectDetector, statusService);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = await app.RunAsync(["status", "--project", "/path/to/project", "--json"]);

            result.Should().Be(0);
            var stdout = sw.ToString();
            stdout.Should().NotBeNullOrEmpty();
            var json = JsonDocument.Parse(ExtractJson(stdout));
            json.RootElement.GetProperty("status").GetString().Should().Be("ready");
            json.RootElement.GetProperty("port").GetInt32().Should().Be(8080);
            json.RootElement.GetProperty("pid").GetInt32().Should().Be(12345);
            json.RootElement.GetProperty("serverResponding").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task StatusCommand_JsonOutput_NotFound_WritesError()
    {
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);

        var statusService = Substitute.For<IStatusService>();

        var app = CreateStatusApp(projectDetector, statusService);

        var originalError = Console.Error;
        using var errorWriter = new StringWriter();
        Console.SetError(errorWriter);

        try
        {
            var result = await app.RunAsync(["status", "--project", "/invalid", "--json"]);

            result.Should().Be(1);
            var stderr = errorWriter.ToString();
            stderr.Should().NotBeNullOrEmpty();
            var json = JsonDocument.Parse(ExtractJson(stderr));
            json.RootElement.GetProperty("error").GetString().Should().Be("No Unity project found at the specified path.");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Extracts the first valid JSON object from a string that may contain
    /// extra output from AnsiConsole or other sources.
    /// </summary>
    private static string ExtractJson(string output)
    {
        var startIndex = output.IndexOf('{');
        if (startIndex < 0) return output;

        var depth = 0;
        for (var i = startIndex; i < output.Length; i++)
        {
            if (output[i] == '{') depth++;
            else if (output[i] == '}') depth--;

            if (depth == 0) return output.Substring(startIndex, i - startIndex + 1);
        }

        return output;
    }

    private static CommandApp CreateStatusApp(IProjectDetector projectDetector, IStatusService statusService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        services.AddSingleton(statusService);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<StatusCommand>("status");
        });
        return app;
    }
}
