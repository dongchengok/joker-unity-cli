using System.ComponentModel;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class AssetsCommand : Command<AssetsCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly IAssetService _assetService;

    public AssetsCommand(IProjectDetector projectDetector, IAssetService assetService)
    {
        _projectDetector = projectDetector;
        _assetService = assetService;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[QUERY]")]
        [Description("Optional search query to filter assets.")]
        public string? Query { get; set; }

        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project.")]
        public string? ProjectPath { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Determine project path
        string projectPath;
        if (!string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            var project = _projectDetector.Detect(settings.ProjectPath);
            if (project == null)
            {
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
                AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found in current directory or parents.");
                return 1;
            }
            projectPath = project.Path;
        }

        var assetsPath = Path.Combine(projectPath, "Assets");

        IEnumerable<Models.AssetInfo> assets;
        if (!string.IsNullOrWhiteSpace(settings.Query))
        {
            assets = _assetService.SearchAssets(assetsPath, settings.Query);
            AnsiConsole.MarkupLine($"[cyan]Searching assets matching:[/] [yellow]'{settings.Query}'[/]");
        }
        else
        {
            assets = _assetService.ListAssets(assetsPath);
            AnsiConsole.MarkupLine("[cyan]Listing all assets:[/]");
        }

        var assetList = assets.ToList();
        if (assetList.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No assets found.[/]");
            return 0;
        }

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("[bold]Extension[/]");
        table.AddColumn("[bold]Path[/]");

        foreach (var asset in assetList)
        {
            table.AddRow(asset.Extension, asset.RelativePath);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]Total: {assetList.Count} asset(s)[/]");

        return 0;
    }
}
