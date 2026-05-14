using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly IStatusService _statusService;

    public StatusCommand(IProjectDetector projectDetector, IStatusService statusService)
    {
        _projectDetector = projectDetector;
        _statusService = statusService;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project.")]
        public string? ProjectPath { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string projectPath;

        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            var project = _projectDetector.Detect(settings.ProjectPath);
            if (project == null)
            {
                if (settings.JsonOutput)
                {
                    WriteJsonError("No Unity project found at the specified path.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found at the specified path.");
                return 1;
            }

            projectPath = project.Path;
        }
        else
        {
            var project = _projectDetector.DetectFromCurrentDirectory(Environment.CurrentDirectory);
            if (project == null)
            {
                if (settings.JsonOutput)
                {
                    WriteJsonError("No Unity project found in current directory or parents.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found in current directory or parents.");
                return 1;
            }

            projectPath = project.Path;
        }

        var status = await _statusService.GetStatusAsync(projectPath, cancellationToken);

        if (settings.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
            return (status.Status == "ready" && status.ServerResponding) ? 0 : 1;
        }

        var color = status.Status switch
        {
            "ready" => "green",
            "compiling" => "yellow",
            _ => "red"
        };

        AnsiConsole.MarkupLine($"Unity Editor Status: [{color}]{status.Status}[/]");
        if (status.Port > 0)
            AnsiConsole.MarkupLine($"  Port: {status.Port}");
        if (status.Pid > 0)
            AnsiConsole.MarkupLine($"  PID:  {status.Pid}");
        AnsiConsole.MarkupLine($"  Server Responding: {(status.ServerResponding ? "[green]Yes[/]" : "[red]No[/]")}");

        return (status.Status == "ready" && status.ServerResponding) ? 0 : 1;
    }

    private static void WriteJsonError(string message)
    {
        var errorObj = new { error = message };
        Console.Error.WriteLine(JsonSerializer.Serialize(errorObj, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
