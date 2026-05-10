using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Tests.Commands;

public class LogsCommandTests
{
    [Fact]
    public async Task LogsCommand_WithJson_ReturnsZero()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LogEntry>
            {
                new()
                {
                    FilePath = "Assets/Test.cs",
                    Line = 10,
                    Column = 5,
                    Severity = "error",
                    Code = "CS0103",
                    Message = "The name 'x' does not exist"
                }
            });

        var projectDetector = Substitute.For<IProjectDetector>();

        var app = CreateLogsApp(logService, projectDetector);

        var result = await app.RunAsync(["logs", "--json"]);

        result.Should().Be(0);
    }

    [Fact]
    public async Task LogsCommand_WithErrorsFlag_PassesToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LogEntry>());

        var projectDetector = Substitute.For<IProjectDetector>();

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--errors", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_WithTail_PassesToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LogEntry>());

        var projectDetector = Substitute.For<IProjectDetector>();

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--tail", "10", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(10, false, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_WithProject_PassesToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LogEntry>());

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect("/path/to/project").Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--project", "/path/to/project", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, false, "/path/to/project", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_AutoDetectProject_PassesPathToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LogEntry>());

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/auto/detected/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, false, "/auto/detected/project", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogsCommand_AutoDetectFails_PassesNullToService()
    {
        var logService = Substitute.For<ILogService>();
        logService.GetLogEntriesAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LogEntry>());

        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.DetectFromCurrentDirectory(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateLogsApp(logService, projectDetector);

        await app.RunAsync(["logs", "--json"]);

        await logService.Received(1).GetLogEntriesAsync(50, false, null, Arg.Any<CancellationToken>());
    }

    private static CommandApp CreateLogsApp(ILogService logService, IProjectDetector projectDetector)
    {
        var services = new ServiceCollection();
        services.AddSingleton(logService);
        services.AddSingleton(projectDetector);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<LogsCommand>("logs");
        });
        return app;
    }
}
