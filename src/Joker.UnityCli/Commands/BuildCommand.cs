using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class BuildCommand : AsyncCommand<BuildCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly IUnityLocator _unityLocator;
    private readonly IBuildService _buildService;

    public BuildCommand(
        IProjectDetector projectDetector,
        IUnityLocator unityLocator,
        IBuildService buildService)
    {
        _projectDetector = projectDetector;
        _unityLocator = unityLocator;
        _buildService = buildService;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "[PLATFORM]")]
        [Description("Build target platform (e.g. Win64, Android, iOS).")]
        public string? Platform { get; set; }

        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project.")]
        public string? ProjectPath { get; set; }

        [CommandOption("-u|--unity <PATH_OR_VERSION>")]
        [Description("Path to Unity executable or version string (e.g. 2022.3.20f1).")]
        public string? Unity { get; set; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output path for the build.")]
        public string? OutputPath { get; set; }

        [CommandOption("-s|--scenes <SCENES>")]
        [Description("Comma-separated list of scenes to include.")]
        public string? Scenes { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Determine project path
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

        // Validate platform
        var platform = settings.Platform;
        if (string.IsNullOrWhiteSpace(platform))
        {
            if (settings.JsonOutput)
            {
                WriteJsonError("Platform is required. Usage: build <PLATFORM>");
                return 1;
            }

            AnsiConsole.MarkupLine("[red]Error:[/] Platform is required. Usage: build <PLATFORM>");
            return 1;
        }

        // Locate Unity
        var unity = _unityLocator.Locate(settings.Unity);
        if (unity == null)
        {
            if (settings.JsonOutput)
            {
                WriteJsonError($"Could not locate Unity installation{(settings.Unity != null ? $" for '{settings.Unity}'" : "")}.");
                return 1;
            }

            AnsiConsole.MarkupLine($"[red]Error:[/] Could not locate Unity installation{(settings.Unity != null ? $" for '{settings.Unity}'" : "")}.");
            return 1;
        }

        var outputPath = settings.OutputPath ?? Path.Combine(projectPath, "Builds", platform);
        var scenes = settings.Scenes?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var executeMethod = "JokerBuildPipeline.BuildPlayer";
        var buildTarget = platform;

        if (!settings.JsonOutput)
        {
            AnsiConsole.MarkupLine($"[cyan]Building {projectPath}[/] for [green]{platform}[/]...");
            AnsiConsole.MarkupLine($"  Unity: {unity.Path} ({unity.Version})");
            AnsiConsole.MarkupLine($"  Output: {outputPath}");
        }

        var result = await _buildService.BuildAsync(
            projectPath,
            unity.Path,
            buildTarget,
            executeMethod,
            outputPath,
            scenes);

        if (settings.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonSerializerOptions));
            return result.Success ? 0 : 1;
        }

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Build succeeded[/] in {result.Duration.TotalSeconds:F1}s");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Build failed.[/]");
            if (!string.IsNullOrEmpty(result.LogPath))
            {
                AnsiConsole.MarkupLine($"  Log: {result.LogPath}");
            }
            return 1;
        }
    }

    private static void WriteJsonError(string message)
    {
        var errorObj = new { error = message };
        Console.Error.WriteLine(JsonSerializer.Serialize(errorObj, JsonSerializerOptions));
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
