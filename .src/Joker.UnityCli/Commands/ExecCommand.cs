using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class ExecCommand : AsyncCommand<ExecCommand.Settings>
{
    private readonly IProjectDetector _projectDetector;
    private readonly IExecService _execService;

    public ExecCommand(
        IProjectDetector projectDetector,
        IExecService execService)
    {
        _projectDetector = projectDetector;
        _execService = execService;
    }

    public class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "[CODE]")]
        [Description("C# code to execute in script mode.")]
        public string? Code { get; set; }

        [CommandOption("-f|--file <PATH>")]
        [Description("Path to a C# file to compile and execute.")]
        public string? FilePath { get; set; }

        [CommandOption("-p|--project <PATH>")]
        [Description("Path to the Unity project.")]
        public string? ProjectPath { get; set; }

        [CommandOption("-t|--timeout <MS>")]
        [Description("Execution timeout in milliseconds.")]
        public int Timeout { get; set; } = 30000;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.Code) && !string.IsNullOrWhiteSpace(settings.FilePath))
        {
            if (settings.JsonOutput)
            {
                WriteJsonError("Cannot specify both CODE argument and --file option.");
                return 1;
            }

            AnsiConsole.MarkupLine("[red]Error:[/] Cannot specify both CODE argument and --file option.");
            return 1;
        }

        string code;
        string mode;

        if (!string.IsNullOrWhiteSpace(settings.FilePath))
        {
            if (!File.Exists(settings.FilePath))
            {
                if (settings.JsonOutput)
                {
                    WriteJsonError($"File not found: {settings.FilePath}");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {settings.FilePath}");
                return 1;
            }

            code = await File.ReadAllTextAsync(settings.FilePath, cancellationToken);
            mode = "compile";
        }
        else if (!string.IsNullOrWhiteSpace(settings.Code))
        {
            code = settings.Code;
            mode = "script";
        }
        else
        {
            if (settings.JsonOutput)
            {
                WriteJsonError("No code provided. Specify CODE argument or use --file <PATH>.");
                return 1;
            }

            AnsiConsole.MarkupLine("[red]Error:[/] No code provided. Specify CODE argument or use --file <PATH>.");
            return 1;
        }

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

        try
        {
            var result = await _execService.ExecuteAsync(projectPath, code, mode, settings.Timeout, cancellationToken);

            if (settings.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonSerializerOptions));
                return result.Success ? 0 : 1;
            }

            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]Execution succeeded[/] in {result.DurationMs}ms");
                if (!string.IsNullOrEmpty(result.Output))
                {
                    AnsiConsole.MarkupLine(result.Output);
                }
                if (!string.IsNullOrEmpty(result.Result))
                {
                    AnsiConsole.MarkupLine($"  Result: {result.Result}");
                }
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Execution failed.[/]");
                if (!string.IsNullOrEmpty(result.Error))
                {
                    AnsiConsole.MarkupLine($"  [red]{result.Error}[/]");
                }
                if (!string.IsNullOrEmpty(result.Output))
                {
                    AnsiConsole.MarkupLine(result.Output);
                }
                return 1;
            }
        }
        catch (FileNotFoundException)
        {
            if (settings.JsonOutput)
            {
                WriteJsonError("Unity server not running. Start the Unity Editor with the Joker plugin.");
                return 1;
            }

            AnsiConsole.MarkupLine("[red]Error:[/] Unity server not running. Start the Unity Editor with the Joker plugin.");
            return 1;
        }
        catch (HttpRequestException)
        {
            if (settings.JsonOutput)
            {
                WriteJsonError("Cannot connect to Unity server. Ensure the Editor is running with the Joker plugin.");
                return 1;
            }

            AnsiConsole.MarkupLine("[red]Error:[/] Cannot connect to Unity server. Ensure the Editor is running with the Joker plugin.");
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
