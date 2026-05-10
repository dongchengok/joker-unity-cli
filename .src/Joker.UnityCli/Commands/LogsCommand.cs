using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class LogsCommand : AsyncCommand<LogsCommand.Settings>
{
    private readonly ILogService _logService;
    private readonly IProjectDetector _projectDetector;

    public LogsCommand(ILogService logService, IProjectDetector projectDetector)
    {
        _logService = logService;
        _projectDetector = projectDetector;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandOption("--errors")]
        [Description("Show only compilation errors")]
        public bool ErrorsOnly { get; set; }

        [CommandOption("--tail")]
        [Description("Number of recent entries to show (default: 50)")]
        public int Tail { get; set; } = 50;

        [CommandOption("-p|--project <PATH>")]
        [Description("Unity project path (auto-detected if omitted)")]
        public string? ProjectPath { get; set; }
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

        var entries = await _logService.GetLogEntriesAsync(settings.Tail, settings.ErrorsOnly, projectPath, cancellationToken);

        if (settings.JsonOutput)
        {
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            Console.WriteLine(json);
            return 0;
        }

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No matching log entries found.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("Severity");
        table.AddColumn("Code");
        table.AddColumn("Location");
        table.AddColumn("Message");

        foreach (var entry in entries)
        {
            var severity = entry.Severity == "error" ? "[red]error[/]" : "[yellow]warning[/]";
            var location = $"{entry.FilePath}:{entry.Line}:{entry.Column}";
            table.AddRow(severity, entry.Code, location, entry.Message.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
