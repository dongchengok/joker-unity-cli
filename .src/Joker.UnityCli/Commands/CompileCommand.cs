using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class CompileCommand : AsyncCommand<CompileCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly ICompileService _compileService;

    public CompileCommand(IProjectDetector projectDetector, ICompileService compileService)
    {
        _projectDetector = projectDetector;
        _compileService = compileService;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandOption("-p|--project <PATH>")]
        [Description("Unity project path (auto-detected if omitted)")]
        public string? ProjectPath { get; set; }

        [CommandOption("-t|--timeout <SECONDS>")]
        [Description("Timeout in seconds (default: 300)")]
        public int Timeout { get; set; } = 300;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string? projectPath = null;

        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            var project = _projectDetector.Detect(settings.ProjectPath);
            projectPath = project?.Path;
        }
        else
        {
            var project = _projectDetector.DetectFromCurrentDirectory(Environment.CurrentDirectory);
            projectPath = project?.Path;
        }

        if (projectPath == null)
        {
            if (settings.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = "No Unity project found." }, JsonOptions));
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found.");
            }
            return 1;
        }

        var result = await _compileService.CompileAsync(projectPath, settings.Timeout * 1000, cancellationToken);

        if (settings.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.Success ? 0 : 1;
        }

        if (result.Success)
        {
            if (result.Status == "up_to_date")
                AnsiConsole.MarkupLine("[green]Already up to date.[/]");
            else
                AnsiConsole.MarkupLine("[green]Compilation succeeded.[/]");
            return 0;
        }

        if (result.Status == "timeout")
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Compilation timed out.");
            return 1;
        }

        AnsiConsole.MarkupLine("[red]Compilation failed.[/]");
        foreach (var error in result.Errors)
            AnsiConsole.MarkupLine($"  [red]{error.EscapeMarkup()}[/]");

        return 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
