using System.ComponentModel;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class InfoCommand : Command<InfoCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;

    public InfoCommand(IProjectDetector projectDetector)
    {
        _projectDetector = projectDetector;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project. If not specified, detects from current directory.")]
        public string? ProjectPath { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        UnityProject? project;

        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            project = _projectDetector.Detect(settings.ProjectPath);
        }
        else
        {
            project = _projectDetector.DetectFromCurrentDirectory(Environment.CurrentDirectory);
        }

        if (project == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found at the specified path.");
            return 1;
        }

        var table = new Table();
        table.Border = TableBorder.Rounded;

        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("Name", project.Name);
        table.AddRow("Path", project.Path);
        table.AddRow("Unity Version", project.UnityVersion);

        AnsiConsole.Write(table);

        if (project.PackageDependencies.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Package Dependencies:[/]");
            var depGrid = new Grid()
                .AddColumn()
                .AddColumn();

            foreach (var dep in project.PackageDependencies)
            {
                depGrid.AddRow("  •", dep);
            }

            AnsiConsole.Write(depGrid);
        }

        return 0;
    }
}
